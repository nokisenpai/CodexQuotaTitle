using System.Text.Json;

namespace CodexQuotaTitle.Mcp;

public sealed record HelperRuntimePaths(string DirectoryPath)
{
    public string StatePath => Path.Combine(DirectoryPath, "helper-state.json");

    public static HelperRuntimePaths ForProfile(string profileHash) =>
        new(Path.Combine(Path.GetTempPath(), "CodexQuotaTitle", "runtime", profileHash));
}

public sealed record HelperRuntimeState(
    string ProfileIdentity,
    string Origin,
    int HelperPid,
    int HostChatGptPid,
    int? HostCodexPid,
    bool IsRunning,
    bool AppServerConnected,
    HelperAccountDiagnostic? Account,
    IReadOnlyList<HelperQuotaSnapshot> Quotas,
    int CandidateWindowCount,
    string WindowCorrelation,
    IReadOnlyList<HelperWindowCandidate> Windows,
    DateTimeOffset UpdatedAtUtc)
{
    public string? PluginVersion { get; init; }

    public string? HelperVersion { get; init; }

    public string? ActiveTemplate { get; init; }

    public string? DateFormat { get; init; }

    public bool Enabled { get; init; }

    public string AppServerStatus { get; init; } = "not-started";

    public string? AppServerLastError { get; init; }

    public string? AppServerLastExceptionType { get; init; }

    public string? LastRenderedTitle { get; init; }

    public string? CustomTitleBaseTitle { get; init; }

    public DateTimeOffset? LastTitleUpdatedAtUtc { get; init; }

    public string? LastError { get; init; }
}

public sealed record HelperAccountDiagnostic(string? Type, string? PlanType, bool RequiresOpenAiAuth);

public sealed record HelperQuotaSnapshot(
    string? LimitId,
    string? LimitName,
    HelperQuotaWindow? Primary,
    HelperQuotaWindow? Secondary);

public sealed record HelperQuotaWindow(
    int? WindowDurationMins,
    decimal UsedPercent,
    decimal RemainingPercent,
    DateTimeOffset? ResetsAtLocal);

public sealed record HelperWindowCandidate(
    string Handle,
    uint ProcessId,
    bool IsVisible,
    string ClassName,
    string Title);

public sealed class HelperRuntimeStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly HelperRuntimePaths _paths;

    public HelperRuntimeStateStore(HelperRuntimePaths paths)
    {
        _paths = paths;
    }

    public HelperRuntimeState? TryRead()
    {
        try
        {
            if (!File.Exists(_paths.StatePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<HelperRuntimeState>(File.ReadAllText(_paths.StatePath), SerializerOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(HelperRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(_paths.DirectoryPath);
        var temporaryPath = string.Concat(_paths.StatePath, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, SerializerOptions));
            File.Move(temporaryPath, _paths.StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
