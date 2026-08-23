using System.Text.Json;
using CodexQuotaTitle.Processes;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Sessions;
using CodexQuotaTitle.Windows;

namespace CodexQuotaTitle.Integration;

public sealed record IntegrationProbeResult(string ReportPath, ProfileIdentity Profile, bool ActiveSessionUpdated);

public sealed class IntegrationProbeRunner
{
    private readonly ProcessInspectionService _processInspection = new();
    private readonly HookBootstrapLog _bootstrapLog;

    public IntegrationProbeRunner(HookBootstrapLog? bootstrapLog = null)
    {
        _bootstrapLog = bootstrapLog ?? new HookBootstrapLog();
    }

    public async Task<IntegrationProbeResult> RunAsync(
        IntegrationHookInput hookInput,
        int? hookLauncherProcessId = null,
        string? readyEventName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        PluginRuntimePaths? runtimePaths = null;
        ProfileIdentity? profile = null;
        var stage = "plugin-runtime-paths";

        try
        {
            _bootstrapLog.TryAppend("HELPER_PLUGIN_RUNTIME_PATHS_REQUESTED");
            runtimePaths = PluginRuntimePaths.FromEnvironment();
            stage = "plugin-data-directory";
            Directory.CreateDirectory(runtimePaths.PluginData);
            _bootstrapLog.TryAppend("BOOTSTRAP_TO_PLUGIN_DATA_OK");
            stage = "profile-identity";
            profile = ProfileIdentity.FromEnvironment();
            stage = "active-session";
            var activeSessionStore = ActiveSessionStore.ForProfile(profile);
            var activeSessionUpdated = activeSessionStore.Update(
                hookInput.SessionId,
                hookInput.TranscriptPath,
                DateTimeOffset.UtcNow);
            SignalReady(readyEventName);

            stage = "process-correlation";
            var helperProcessId = Environment.ProcessId;
            var helperProcessChain = _processInspection.GetProcessChain(helperProcessId);
            var launcherProcessChain = hookLauncherProcessId is > 0
                ? _processInspection.GetProcessChain(hookLauncherProcessId.Value)
                : Array.Empty<InspectedProcess>();
            var relatedProcessIds = helperProcessChain
                .Concat(launcherProcessChain)
                .Select(process => process.ProcessId)
                .Distinct()
                .ToArray();

            stage = "rollout-quotas";
            using var quotaSource = new RolloutQuotaSource(profile);
            var quotaResult = quotaSource.Refresh();
            var quotas = quotaResult.Quotas;

            stage = "window-correlation";
            var windows = OperatingSystem.IsWindows()
                ? new WindowTitleService().ListRelevantWindows(relatedProcessIds)
                : Array.Empty<WindowCandidate>();
            var report = new IntegrationProbeReport(
                DateTimeOffset.UtcNow,
                hookInput,
                runtimePaths,
                profile,
                new ProbeProcessDiagnostics(
                    hookLauncherProcessId,
                    helperProcessId,
                    helperProcessChain.FirstOrDefault()?.ParentProcessId,
                    launcherProcessChain,
                    helperProcessChain),
                activeSessionUpdated,
                quotaResult.Status,
                quotaResult.ActiveSessionId,
                quotaResult.RolloutAvailable,
                quotaResult.LastQuotaUpdatedAtUtc,
                quotas is null ? null : QuotaConsoleFormatter.Format(quotas),
                quotas is null ? Array.Empty<ProbeQuotaSnapshot>() : ToProbeQuotas(quotas),
                ToProbeWindows(windows));

            var reportPath = Path.Combine(
                runtimePaths.PluginData,
                $"integration-probe-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{profile.ProfileHash}.json");
            stage = "report-serialization";
            var reportJson = SerializeReport(report);
            stage = "report-write";
            File.WriteAllText(reportPath, reportJson);
            return new IntegrationProbeResult(reportPath, profile, activeSessionUpdated);
        }
        catch (Exception exception)
        {
            _bootstrapLog.AppendException($"HELPER_FAILED_STAGE={stage}", exception);
            if (runtimePaths is not null)
            {
                await TryWriteFailureReportAsync(runtimePaths, hookInput, stage, exception);
            }

            throw;
        }
        finally
        {
            SignalReady(readyEventName);
        }
    }

    private static IReadOnlyList<ProbeQuotaSnapshot> ToProbeQuotas(QuotaReadResult quotas) =>
        quotas.AllSnapshots()
            .Distinct()
            .Select(snapshot => new ProbeQuotaSnapshot(
                snapshot.LimitId,
                snapshot.LimitName,
                ToProbeWindow("primary", snapshot.Primary),
                ToProbeWindow("secondary", snapshot.Secondary)))
            .ToList();

    private static ProbeQuotaWindow? ToProbeWindow(string name, RateLimitWindow? window) =>
        window is null
            ? null
            : new ProbeQuotaWindow(
                name,
                window.WindowDurationMins,
                window.UsedPercent,
                window.RemainingPercent,
                window.ResetsAt?.ToLocalTime());

    private static IReadOnlyList<ProbeWindowCandidate> ToProbeWindows(IEnumerable<WindowCandidate> windows) =>
        windows.Select(window => new ProbeWindowCandidate(
                $"0x{window.Handle.ToInt64():X}",
                window.ProcessId,
                window.ProcessName,
                window.ClassName,
                window.Title,
                window.IsVisible,
                window.ProcessChain,
                window.SharedProcessIds))
            .ToList();

    private static Task WriteReportAsync<TReport>(string path, TReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(path, SerializeReport(report));
        return Task.CompletedTask;
    }

    private static string SerializeReport<TReport>(TReport report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        return JsonSerializer.Serialize(report, options);
    }

    private static async Task TryWriteFailureReportAsync(
        PluginRuntimePaths runtimePaths,
        IntegrationHookInput hookInput,
        string stage,
        Exception exception)
    {
        try
        {
            var report = new IntegrationProbeFailureReport(
                DateTimeOffset.UtcNow,
                hookInput,
                stage,
                exception.GetType().Name,
                exception.InnerException?.GetType().Name);
            var path = Path.Combine(
                runtimePaths.PluginData,
                $"integration-probe-failure-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json");
            await WriteReportAsync(path, report, CancellationToken.None);
        }
        catch
        {
            // Diagnostic reporting must never change the hook's clean failure behavior.
        }
    }

    private static void SignalReady(string? readyEventName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(readyEventName))
        {
            return;
        }

        try
        {
            using var readyEvent = EventWaitHandle.OpenExisting(readyEventName);
            readyEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The manual probe mode has no launcher to acknowledge.
        }
    }
}

public sealed record IntegrationProbeReport(
    DateTimeOffset TimestampUtc,
    IntegrationHookInput Hook,
    PluginRuntimePaths Plugin,
    ProfileIdentity Profile,
    ProbeProcessDiagnostics Process,
    bool ActiveSessionUpdated,
    string QuotaSourceStatus,
    string? ActiveSessionId,
    bool RolloutAvailable,
    DateTimeOffset? LastQuotaUpdatedAtUtc,
    string? QuotaSummary,
    IReadOnlyList<ProbeQuotaSnapshot> Quotas,
    IReadOnlyList<ProbeWindowCandidate> Windows);

public sealed record IntegrationProbeFailureReport(
    DateTimeOffset TimestampUtc,
    IntegrationHookInput Hook,
    string Stage,
    string FailureType,
    string? InnerFailureType);

public sealed record ProbeProcessDiagnostics(
    int? HookLauncherProcessId,
    int HelperProcessId,
    int? HelperParentProcessId,
    IReadOnlyList<InspectedProcess> HookLauncherProcessChain,
    IReadOnlyList<InspectedProcess> HelperProcessChain);

public sealed record ProbeQuotaSnapshot(
    string? LimitId,
    string? LimitName,
    ProbeQuotaWindow? Primary,
    ProbeQuotaWindow? Secondary);

public sealed record ProbeQuotaWindow(
    string Name,
    int? WindowDurationMins,
    decimal UsedPercent,
    decimal RemainingPercent,
    DateTimeOffset? ResetsAtLocal);

public sealed record ProbeWindowCandidate(
    string Handle,
    uint ProcessId,
    string ProcessName,
    string ClassName,
    string Title,
    bool IsVisible,
    IReadOnlyList<InspectedProcess> ProcessChain,
    IReadOnlyList<int> SharedProcessIds);
