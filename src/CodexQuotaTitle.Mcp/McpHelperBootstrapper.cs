using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Sessions;

namespace CodexQuotaTitle.Mcp;

public sealed record McpHelperStartupRequest(
    string CodexHome,
    ProfileIdentity Profile,
    int HostChatGptPid,
    int? HostCodexPid,
    string Origin,
    HelperRuntimePaths RuntimePaths);

public sealed record McpHelperBootstrapResult(bool StartedNewHelper, int? HelperPid, string Reason);

public sealed record McpHelperStatus(
    string? ProfileIdentity,
    bool HelperRunning,
    int? HelperPid,
    string QuotaSource,
    string QuotaSourceStatus,
    string? ActiveSessionId,
    bool RolloutAvailable,
    DateTimeOffset? LastQuotaUpdatedAtUtc,
    IReadOnlyList<HelperQuotaSnapshot> Quotas,
    int? CandidateWindowCount,
    string? WindowCorrelation,
    string? PluginVersion,
    string? HelperVersion,
    string? ActiveTemplate,
    string? DateFormat,
    bool? Enabled,
    string? LastRenderedTitle,
    DateTimeOffset? LastTitleUpdatedAtUtc,
    string? LastError,
    IReadOnlyList<HelperWindowCandidate> Windows)
{
    public string? QuotaSelection { get; init; }

    public bool AppServerConnected { get; init; }

    public string AppServerStatus { get; init; } = "not-started";

    public string? AppServerLastError { get; init; }

    public string? AppServerLastExceptionType { get; init; }

    public string AppServerExecutableSource { get; init; } = "not-started";

    public bool? AppServerExecutableAvailable { get; init; }
}

public interface IHelperProcessStarter
{
    int Start(McpHelperStartupRequest request);
}

public interface IProcessLiveness
{
    bool IsRunning(int processId);
}

public sealed class McpHelperBootstrapper
{
    private readonly McpBootstrapLog _bootstrapLog;
    private readonly McpStartupContext _startupContext;
    private readonly McpProcessTree _processTree;
    private readonly IHelperProcessStarter _helperProcessStarter;
    private readonly IProcessLiveness _processLiveness;

    public McpHelperBootstrapper(
        McpBootstrapLog bootstrapLog,
        McpStartupContext startupContext,
        McpProcessTree processTree,
        IHelperProcessStarter? helperProcessStarter = null,
        IProcessLiveness? processLiveness = null)
    {
        _bootstrapLog = bootstrapLog;
        _startupContext = startupContext;
        _processTree = processTree;
        _helperProcessStarter = helperProcessStarter ?? new SelfMcpHelperProcessStarter();
        _processLiveness = processLiveness ?? new CurrentProcessLiveness();
    }

    public McpHelperBootstrapResult EnsureHelper()
    {
        if (!_startupContext.CodexHomeAvailable || string.IsNullOrWhiteSpace(_startupContext.CodexHome))
        {
            _bootstrapLog.TryAppend("MCP_HELPER_NOT_STARTED_CODEX_HOME_UNAVAILABLE");
            return new McpHelperBootstrapResult(false, null, "codex-home-unavailable");
        }

        if (_startupContext.ProfileIdentity is not { } profile)
        {
            _bootstrapLog.TryAppend("MCP_HELPER_NOT_STARTED_PROFILE_IDENTITY_UNAVAILABLE");
            return new McpHelperBootstrapResult(false, null, "profile-identity-unavailable");
        }

        var hostChatGptPid = _processTree.FindProcessId("ChatGPT.exe");
        if (hostChatGptPid is not > 0)
        {
            _bootstrapLog.TryAppend("MCP_HELPER_NOT_STARTED_HOST_CHATGPT_PID_UNAVAILABLE");
            return new McpHelperBootstrapResult(false, null, "host-chatgpt-pid-unavailable");
        }

        var request = new McpHelperStartupRequest(
            _startupContext.CodexHome,
            profile,
            hostChatGptPid.Value,
            _processTree.FindProcessId("codex.exe"),
            "mcp",
            HelperRuntimePaths.ForProfile(profile.ProfileHash));

        using var bootstrapGate = ProfileBootstrapGate.Acquire(profile);
        var stateStore = new HelperRuntimeStateStore(request.RuntimePaths);
        var existingState = stateStore.TryRead();
        if (existingState is not null &&
            string.Equals(existingState.ProfileIdentity, profile.ProfileHash, StringComparison.Ordinal) &&
            existingState.IsRunning &&
            IsHelperRunning(existingState.HelperPid))
        {
            _bootstrapLog.TryAppend($"MCP_HELPER_ALREADY_RUNNING PID={existingState.HelperPid}");
            return new McpHelperBootstrapResult(false, existingState.HelperPid, "already-running");
        }

        _bootstrapLog.TryAppend($"MCP_HELPER_START_REQUESTED PROFILE_IDENTITY={profile.ProfileHash} HOST_CHATGPT_PID={hostChatGptPid}");
        var helperPid = _helperProcessStarter.Start(request);
        stateStore.Write(CreateProvisionalState(request, helperPid));
        _bootstrapLog.TryAppend($"MCP_HELPER_PROCESS_STARTED PID={helperPid}");
        return new McpHelperBootstrapResult(true, helperPid, "started");
    }

    public McpHelperStatus GetStatus()
    {
        if (_startupContext.ProfileIdentity is not { } profile)
        {
            return new McpHelperStatus(
                ProfileIdentity: null,
                HelperRunning: false,
                HelperPid: null,
                QuotaSource: "rollout",
                QuotaSourceStatus: RolloutQuotaSourceStatuses.RolloutNotFound,
                ActiveSessionId: null,
                RolloutAvailable: false,
                LastQuotaUpdatedAtUtc: null,
                Quotas: Array.Empty<HelperQuotaSnapshot>(),
                CandidateWindowCount: null,
                WindowCorrelation: null,
                PluginVersion: null,
                HelperVersion: null,
                ActiveTemplate: null,
                DateFormat: null,
                Enabled: null,
                LastRenderedTitle: null,
                LastTitleUpdatedAtUtc: null,
                LastError: null,
                Windows: Array.Empty<HelperWindowCandidate>())
            {
                QuotaSelection = RolloutQuotaSelections.ProfileFallback,
                AppServerConnected = false,
            };
        }

        var state = new HelperRuntimeStateStore(HelperRuntimePaths.ForProfile(profile.ProfileHash)).TryRead();
        var helperRunning = state is not null && state.IsRunning && IsHelperRunning(state.HelperPid);
        return new McpHelperStatus(
            profile.ProfileHash,
            helperRunning,
            helperRunning ? state!.HelperPid : null,
            state?.QuotaSource ?? "rollout",
            state?.QuotaSourceStatus ?? RolloutQuotaSourceStatuses.RolloutNotFound,
            state?.ActiveSessionId,
            state?.RolloutAvailable ?? false,
            state?.LastQuotaUpdatedAtUtc,
            state?.Quotas ?? Array.Empty<HelperQuotaSnapshot>(),
            state?.CandidateWindowCount,
            state?.WindowCorrelation,
            state?.PluginVersion,
            state?.HelperVersion,
            state?.ActiveTemplate,
            state?.DateFormat,
            state?.Enabled,
            state?.LastRenderedTitle,
            state?.LastTitleUpdatedAtUtc,
            state?.LastError,
            state?.Windows ?? Array.Empty<HelperWindowCandidate>())
        {
            QuotaSelection = state?.QuotaSelection ?? RolloutQuotaSelections.ProfileFallback,
            AppServerConnected = state?.AppServerConnected ?? false,
            AppServerStatus = state?.AppServerStatus ?? "not-started",
            AppServerLastError = state?.AppServerLastError,
            AppServerLastExceptionType = state?.AppServerLastExceptionType,
            AppServerExecutableSource = state?.AppServerExecutableSource ?? "not-started",
            AppServerExecutableAvailable = state?.AppServerExecutableAvailable,
        };
    }

    public ProfileIdentity? GetProfileIdentity() => _startupContext.ProfileIdentity;

    private bool IsHelperRunning(int processId)
    {
        try
        {
            return _processLiveness.IsRunning(processId);
        }
        catch (Win32Exception exception)
        {
            _bootstrapLog.TryAppend(
                $"MCP_HELPER_LIVENESS_UNAVAILABLE PID={processId} NATIVE_ERROR_CODE={exception.NativeErrorCode}");
            return false;
        }
    }

    private static HelperRuntimeState CreateProvisionalState(McpHelperStartupRequest request, int helperPid) =>
        new(
            request.Profile.ProfileHash,
            request.Origin,
            helperPid,
            request.HostChatGptPid,
            request.HostCodexPid,
            true,
            false,
            null,
            Array.Empty<HelperQuotaSnapshot>(),
            0,
            "WINDOW_CORRELATION_AMBIGUOUS",
            Array.Empty<HelperWindowCandidate>(),
            DateTimeOffset.UtcNow)
        {
            PluginVersion = McpHelperService.HelperVersion,
            HelperVersion = McpHelperService.HelperVersion,
        };
}

public sealed class CurrentProcessLiveness : IProcessLiveness
{
    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed class SelfMcpHelperProcessStarter : IHelperProcessStarter
{
    public int Start(McpHelperStartupRequest request)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to resolve the MCP executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = GetEntryAssemblyPathForDotnetHost();
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("Unable to resolve the MCP entry assembly.");
            }

            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add("--mcp-helper");
        startInfo.ArgumentList.Add("--codex-home");
        startInfo.ArgumentList.Add(request.CodexHome);
        startInfo.ArgumentList.Add("--profile-identity");
        startInfo.ArgumentList.Add(request.Profile.ProfileHash);
        startInfo.ArgumentList.Add("--host-chatgpt-pid");
        startInfo.ArgumentList.Add(request.HostChatGptPid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (request.HostCodexPid is > 0)
        {
            startInfo.ArgumentList.Add("--host-codex-pid");
            startInfo.ArgumentList.Add(request.HostCodexPid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        startInfo.ArgumentList.Add("--origin");
        startInfo.ArgumentList.Add(request.Origin);
        startInfo.ArgumentList.Add("--runtime-directory");
        startInfo.ArgumentList.Add(request.RuntimePaths.DirectoryPath);
        startInfo.Environment["CODEX_HOME"] = request.CodexHome;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the profile helper.");
        process.OutputDataReceived += static (_, _) => { };
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _ = DisposeAfterExitAsync(process);
        return process.Id;
    }

    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "This path is used only when Environment.ProcessPath identifies the dotnet host; single-file builds execute the native app path branch.")]
    private static string? GetEntryAssemblyPathForDotnetHost() =>
        Assembly.GetEntryAssembly()?.Location;

    private static async Task DisposeAfterExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The helper process already exited while output drains were being attached.
        }
        finally
        {
            process.Dispose();
        }
    }
}
