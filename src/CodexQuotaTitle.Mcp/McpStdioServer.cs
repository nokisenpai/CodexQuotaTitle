using System.Text.Json;
using System.Text.Json.Nodes;
using CodexQuotaTitle.Settings;
using CodexQuotaTitle.Titles;
using CodexQuotaTitle.Windows;

namespace CodexQuotaTitle.Mcp;

public sealed class McpStdioServer
{
    private const string ServerName = "codex_quota_title";
    private const string ServerVersion = "0.1.1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly McpBootstrapLog _bootstrapLog;
    private readonly int _processId;
    private readonly McpStartupContext _startupContext;
    private readonly McpHelperBootstrapper _helperBootstrapper;
    private readonly ICodexQuotaTitleSettingsStoreFactory _settingsStoreFactory;
    private readonly QuotaTemplateRenderer _templateRenderer;

    public McpStdioServer(
        McpBootstrapLog bootstrapLog,
        int processId,
        McpStartupContext startupContext,
        McpHelperBootstrapper helperBootstrapper,
        ICodexQuotaTitleSettingsStoreFactory? settingsStoreFactory = null,
        QuotaTemplateRenderer? templateRenderer = null)
    {
        _bootstrapLog = bootstrapLog;
        _processId = processId;
        _startupContext = startupContext;
        _helperBootstrapper = helperBootstrapper;
        _settingsStoreFactory = settingsStoreFactory ?? new CodexQuotaTitleSettingsStoreFactory();
        _templateRenderer = templateRenderer ?? new QuotaTemplateRenderer();
    }

    public async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await input.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                await HandleLineAsync(line, output, cancellationToken);
            }
        }

        return 0;
    }

    private async Task HandleLineAsync(string line, TextWriter output, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            await WriteResponseAsync(output, null, error: CreateError(-32700, "Parse error"), cancellationToken: cancellationToken);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                await WriteResponseAsync(output, GetRequestId(root), error: CreateError(-32600, "Invalid request"), cancellationToken: cancellationToken);
                return;
            }

            var method = methodElement.GetString()!;
            var requestId = GetRequestId(root);
            if (requestId is null)
            {
                HandleNotification(method);
                return;
            }

            try
            {
                await WriteResponseAsync(output, requestId, HandleRequest(method, root), cancellationToken: cancellationToken);
            }
            catch (McpRequestException exception)
            {
                await WriteResponseAsync(output, requestId, error: CreateError(exception.Code, exception.Message), cancellationToken: cancellationToken);
            }
        }
    }

    private JsonObject HandleRequest(string method, JsonElement request) => method switch
    {
        "initialize" => CreateInitializeResponse(request),
        "ping" => new JsonObject(),
        "tools/list" => CreateToolsListResponse(),
        "tools/call" => CreateToolCallResponse(request),
        _ => throw new McpRequestException(-32601, $"Method not found: {method}"),
    };

    private void HandleNotification(string method)
    {
        if (!string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            return;
        }

        _bootstrapLog.TryAppend("MCP_INITIALIZED");
        try
        {
            _helperBootstrapper.EnsureHelper();
        }
        catch (Exception exception)
        {
            _bootstrapLog.TryAppend($"MCP_HELPER_BOOTSTRAP_FAILED EXCEPTION_TYPE={exception.GetType().Name}");
        }
    }

    private static JsonObject CreateInitializeResponse(JsonElement request)
    {
        var protocolVersion = request.TryGetProperty("params", out var parameters) &&
            parameters.TryGetProperty("protocolVersion", out var requestedVersion) &&
            requestedVersion.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(requestedVersion.GetString())
            ? requestedVersion.GetString()!
            : "2025-03-26";
        return new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
        };
    }

    private JsonObject CreateToolsListResponse()
    {
        _bootstrapLog.TryAppend("MCP_TOOLS_LIST");
        return new JsonObject
        {
            ["tools"] = new JsonArray(
                CreateTool("codex_quota_title_status", "Codex quota title status", "Returns profile, helper, quota, settings, and correlated-window status.", true, new JsonObject()),
                CreateTool("codex_quota_title_get_settings", "Get title settings", "Returns the persistent profile settings.", true, new JsonObject()),
                CreateTool(
                    "codex_quota_title_set_template",
                    "Set title template",
                    "Validates and persists the profile template; the helper refreshes it immediately.",
                    false,
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["template"] = new JsonObject { ["type"] = "string" } },
                        ["required"] = new JsonArray("template"),
                        ["additionalProperties"] = false,
                    })),
        };
    }

    private static JsonObject CreateTool(string name, string title, string description, bool readOnly, JsonObject inputSchema) =>
        new()
        {
            ["name"] = name,
            ["title"] = title,
            ["description"] = description,
            ["inputSchema"] = inputSchema.Count == 0
                ? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false }
                : inputSchema,
            ["annotations"] = new JsonObject { ["readOnlyHint"] = readOnly },
        };

    private JsonObject CreateToolCallResponse(JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            throw new McpRequestException(-32602, "Unknown tool");
        }

        return nameElement.GetString() switch
        {
            "codex_quota_title_status" => CreateToolResponse(CreateStatus(), "MCP_STATUS_TOOL_CALLED"),
            "codex_quota_title_get_settings" => CreateToolResponse(CreateSettingsResponse(), "MCP_GET_SETTINGS_TOOL_CALLED"),
            "codex_quota_title_set_template" => CreateToolResponse(SetTemplate(parameters), "MCP_SET_TEMPLATE_TOOL_CALLED"),
            _ => throw new McpRequestException(-32602, "Unknown tool"),
        };
    }

    private JsonObject CreateStatus()
    {
        var helperStatus = _helperBootstrapper.GetStatus();
        var window = helperStatus.Windows.FirstOrDefault(candidate =>
            candidate.IsVisible && string.Equals(candidate.ClassName, AutomaticTitleUpdateService.ExpectedWindowClassName, StringComparison.Ordinal));
        return new JsonObject
        {
            ["status"] = "active",
            ["pluginVersion"] = ServerVersion,
            ["helperVersion"] = helperStatus.HelperVersion,
            ["pid"] = _processId,
            ["codexHomeObserved"] = _startupContext.CodexHomeObserved,
            ["codexHomeSource"] = _startupContext.CodexHomeSource,
            ["codexHomeAvailable"] = _startupContext.CodexHomeAvailable,
            ["profileIdentity"] = helperStatus.ProfileIdentity,
            ["helperRunning"] = helperStatus.HelperRunning,
            ["helperPid"] = helperStatus.HelperPid,
            ["appServerConnected"] = helperStatus.AppServerConnected,
            ["template"] = helperStatus.ActiveTemplate,
            ["dateFormat"] = helperStatus.DateFormat,
            ["enabled"] = helperStatus.Enabled,
            ["quotaWindows"] = JsonSerializer.SerializeToNode(helperStatus.Quotas, SerializerOptions),
            ["candidateWindowCount"] = helperStatus.CandidateWindowCount,
            ["windowCorrelation"] = helperStatus.WindowCorrelation,
            ["correlatedWindow"] = JsonSerializer.SerializeToNode(window, SerializerOptions),
            ["lastRenderedTitle"] = helperStatus.LastRenderedTitle,
            ["lastTitleUpdatedAtUtc"] = helperStatus.LastTitleUpdatedAtUtc,
            ["lastError"] = helperStatus.LastError,
        };
    }

    private JsonObject CreateSettingsResponse()
    {
        _bootstrapLog.TryAppend("MCP_GET_SETTINGS_TOOL_CALLED");
        var result = GetSettingsStore().LoadOrCreate();
        if (result.Error is not null)
        {
            _bootstrapLog.TryAppend($"MCP_SETTINGS_LOAD_FALLBACK REASON={result.Error}");
        }

        return SettingsToJson(result.Settings);
    }

    private JsonObject SetTemplate(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("arguments", out var arguments) ||
            !arguments.TryGetProperty("template", out var templateElement) ||
            templateElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(templateElement.GetString()))
        {
            throw new McpRequestException(-32602, "The template argument is required.");
        }

        var template = templateElement.GetString()!;
        var validation = _templateRenderer.Validate(template);
        if (!validation.IsValid)
        {
            throw new McpRequestException(-32602, validation.Error!);
        }

        var store = GetSettingsStore();
        var loadResult = store.LoadOrCreate();
        if (loadResult.Error is not null)
        {
            throw new McpRequestException(-32000, $"Unable to update settings: {loadResult.Error}");
        }

        var current = loadResult.Settings;
        var updated = current with { Template = template };
        try
        {
            store.UpdateTemplatePreservingJsonc(template);
        }
        catch (InvalidDataException exception)
        {
            throw new McpRequestException(-32000, $"Unable to update settings: {exception.Message}");
        }
        catch (IOException exception)
        {
            throw new McpRequestException(-32000, $"Unable to save settings: {exception.GetType().Name}");
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new McpRequestException(-32000, $"Unable to save settings: {exception.GetType().Name}");
        }
        _helperBootstrapper.EnsureHelper();
        foreach (var placeholder in validation.UnknownPlaceholders)
        {
            _bootstrapLog.TryAppend($"MCP_TEMPLATE_UNKNOWN_PLACEHOLDER={placeholder}");
        }

        var result = SettingsToJson(updated);
        result["unknownPlaceholders"] = JsonSerializer.SerializeToNode(validation.UnknownPlaceholders, SerializerOptions);
        result["helperRefreshRequested"] = true;
        result["preview"] = CreatePreview(updated);
        return result;
    }

    private string? CreatePreview(CodexQuotaTitleSettings settings)
    {
        var helperStatus = _helperBootstrapper.GetStatus();
        var candidate = helperStatus.Windows.FirstOrDefault(window =>
            window.IsVisible && string.Equals(window.ClassName, AutomaticTitleUpdateService.ExpectedWindowClassName, StringComparison.Ordinal));
        if (candidate is null)
        {
            return null;
        }

        try
        {
            var baseTitle = new TitleOwnership().GetBaseTitle(candidate.Title);
            return _templateRenderer
                .RenderParts(settings.Template, HelperQuotaReadResultConverter.ToQuotaReadResult(helperStatus.Quotas), settings.DateFormat)
                .Render(baseTitle);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private CodexQuotaTitleSettingsStore GetSettingsStore()
    {
        var profile = _helperBootstrapper.GetProfileIdentity();
        if (profile is null)
        {
            throw new McpRequestException(-32000, "ProfileIdentity is unavailable because CODEX_HOME could not be resolved.");
        }

        return _settingsStoreFactory.Create(profile);
    }

    private static JsonObject SettingsToJson(CodexQuotaTitleSettings settings) => new()
    {
        ["template"] = settings.Template,
        ["dateFormat"] = settings.DateFormat,
        ["enabled"] = settings.Enabled,
    };

    private JsonObject CreateToolResponse(JsonObject status, string marker)
    {
        _bootstrapLog.TryAppend(marker);
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = status.ToJsonString(SerializerOptions) }),
            ["structuredContent"] = status,
            ["isError"] = false,
        };
    }

    private static JsonNode? GetRequestId(JsonElement root) =>
        root.TryGetProperty("id", out var id) ? JsonNode.Parse(id.GetRawText()) : null;

    private static JsonObject CreateError(int code, string message) => new() { ["code"] = code, ["message"] = message };

    private static async Task WriteResponseAsync(TextWriter output, JsonNode? requestId, JsonObject? result = null, JsonObject? error = null, CancellationToken cancellationToken = default)
    {
        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = requestId };
        if (error is not null)
        {
            response["error"] = error;
        }
        else
        {
            response["result"] = result ?? new JsonObject();
        }

        await output.WriteLineAsync(response.ToJsonString(SerializerOptions).AsMemory(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private sealed class McpRequestException : Exception
    {
        public McpRequestException(int code, string message)
            : base(message)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
