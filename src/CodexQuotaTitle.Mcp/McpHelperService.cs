using System.Diagnostics;
using CodexQuotaTitle.Accounts;
using CodexQuotaTitle.AppServer;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Sessions;
using CodexQuotaTitle.Settings;
using CodexQuotaTitle.Titles;
using CodexQuotaTitle.Windows;

namespace CodexQuotaTitle.Mcp;

public interface IHelperAppServerSession : IAsyncDisposable
{
    event EventHandler<QuotaSnapshot>? RateLimitsUpdated;

    Task<CodexAccountSummary> ReadAccountAsync(CancellationToken cancellationToken);

    Task<QuotaReadResult> ReadRateLimitsAsync(CancellationToken cancellationToken);
}

public interface IHelperAppServerSessionFactory
{
    Task<IHelperAppServerSession> StartAsync(CodexAppServerLaunchRequest request, CancellationToken cancellationToken);
}

public sealed record CodexAppServerLaunchRequest(string CodexHome, string Executable);

public interface IHostProcessLifetime
{
    Task WaitForExitAsync(int processId, CancellationToken cancellationToken);
}

public interface IHostWindowProvider
{
    IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(int processId);
}

public sealed record McpHelperServiceRunResult(bool AcquiredProfileSingleton, HelperRuntimeState? FinalState);

public sealed class McpHelperService
{
    public const string HelperVersion = "0.1.6";
    private readonly IHostProcessLifetime _hostProcessLifetime;
    private readonly IHostWindowProvider _hostWindowProvider;
    private readonly ICodexQuotaTitleSettingsStoreFactory _settingsStoreFactory;
    private readonly ISettingsChangeWatcherFactory _settingsChangeWatcherFactory;
    private readonly IAutomaticTitleUpdateService _titleUpdateService;
    private readonly QuotaTemplateRenderer _templateRenderer;
    private readonly TimeSpan _refreshInterval;
    private readonly IRolloutQuotaSourceFactory _rolloutQuotaSourceFactory;

    public McpHelperService(
        IHostProcessLifetime? hostProcessLifetime = null,
        IHostWindowProvider? hostWindowProvider = null,
        ICodexQuotaTitleSettingsStoreFactory? settingsStoreFactory = null,
        ISettingsChangeWatcherFactory? settingsChangeWatcherFactory = null,
        IAutomaticTitleUpdateService? titleUpdateService = null,
        QuotaTemplateRenderer? templateRenderer = null,
        TimeSpan? refreshInterval = null,
        IRolloutQuotaSourceFactory? rolloutQuotaSourceFactory = null)
    {
        _hostProcessLifetime = hostProcessLifetime ?? new ProcessHostLifetime();
        _hostWindowProvider = hostWindowProvider ?? new WindowsHostWindowProvider();
        _settingsStoreFactory = settingsStoreFactory ?? new CodexQuotaTitleSettingsStoreFactory();
        _settingsChangeWatcherFactory = settingsChangeWatcherFactory ?? new SettingsChangeWatcherFactory();
        _titleUpdateService = titleUpdateService ?? new AutomaticTitleUpdateService();
        _templateRenderer = templateRenderer ?? new QuotaTemplateRenderer();
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(60);
        _rolloutQuotaSourceFactory = rolloutQuotaSourceFactory ?? new RolloutQuotaSourceFactory();
    }

    public McpHelperService(
        IHelperAppServerSessionFactory? ignoredAppServerSessionFactory,
        IHostProcessLifetime? hostProcessLifetime = null,
        IHostWindowProvider? hostWindowProvider = null,
        ICodexQuotaTitleSettingsStoreFactory? settingsStoreFactory = null,
        ISettingsChangeWatcherFactory? settingsChangeWatcherFactory = null,
        IAutomaticTitleUpdateService? titleUpdateService = null,
        QuotaTemplateRenderer? templateRenderer = null,
        TimeSpan? refreshInterval = null,
        ICodexAppServerExecutableResolver? appServerExecutableResolver = null)
        : this(
            hostProcessLifetime,
            hostWindowProvider,
            settingsStoreFactory,
            settingsChangeWatcherFactory,
            titleUpdateService,
            templateRenderer,
            refreshInterval)
    {
        _ = ignoredAppServerSessionFactory;
        _ = appServerExecutableResolver;
    }

    public async Task<McpHelperServiceRunResult> RunAsync(
        McpHelperStartupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stateStore = new HelperRuntimeStateStore(request.RuntimePaths);
        var log = new HelperServiceLog(request.RuntimePaths.DirectoryPath);
        using var singleton = ProfileSingleton.TryAcquire(request.Profile);
        if (!singleton.IsOwner)
        {
            log.TryAppend($"HELPER_SERVICE_ALREADY_RUNNING PROFILE_IDENTITY={request.Profile.ProfileHash}");
            return new McpHelperServiceRunResult(false, stateStore.TryRead());
        }

        using var hostLifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var hostExitTask = _hostProcessLifetime.WaitForExitAsync(request.HostChatGptPid, hostLifetimeCancellation.Token);
        var settingsStore = _settingsStoreFactory.Create(request.Profile);
        var settingsResult = settingsStore.LoadOrCreate();
        var state = CreateInitialState(request, settingsResult.Settings);
        stateStore.Write(state);
        LogSettingsLoad(log, settingsResult);
        log.TryAppend("HELPER_SERVICE_STARTED");
        log.TryAppend($"PROFILE_IDENTITY={request.Profile.ProfileHash}");
        log.TryAppend($"CODEX_HOME={request.CodexHome}");
        log.TryAppend($"HOST_CHATGPT_PID={request.HostChatGptPid}");
        if (request.HostCodexPid is > 0)
        {
            log.TryAppend($"HOST_CODEX_PID={request.HostCodexPid}");
        }

        try
        {
            using var quotaSource = _rolloutQuotaSourceFactory.Create(request.Profile);
            using var settingsWatcher = _settingsChangeWatcherFactory.Create(settingsStore.Paths);
            state = RefreshQuotas(state, quotaSource, stateStore, log, "startup");
            state = RefreshTitle(state, request, settingsStore, stateStore, log, "startup");

            while (!cancellationToken.IsCancellationRequested)
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var periodicTask = Task.Delay(_refreshInterval, waitCancellation.Token);
                var settingsTask = settingsWatcher.WaitForChangeAsync(waitCancellation.Token);
                var quotaTask = quotaSource.WaitForChangeAsync(waitCancellation.Token);
                var completed = await Task.WhenAny(hostExitTask, periodicTask, settingsTask, quotaTask);
                waitCancellation.Cancel();

                if (completed == hostExitTask)
                {
                    await hostExitTask;
                    log.TryAppend("HELPER_SERVICE_STOPPED_HOST_CHATGPT_EXITED");
                    break;
                }

                if (completed == settingsTask)
                {
                    await settingsTask;
                    state = RefreshTitle(state, request, settingsStore, stateStore, log, "settings-change");
                    continue;
                }

                if (completed == quotaTask)
                {
                    await quotaTask;
                    state = RefreshQuotas(state, quotaSource, stateStore, log, "watcher");
                    state = RefreshTitle(state, request, settingsStore, stateStore, log, "rollout-change");
                    continue;
                }

                state = RefreshQuotas(state, quotaSource, stateStore, log, "periodic-resync");
                state = RefreshTitle(state, request, settingsStore, stateStore, log, "periodic");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation follows the same ownership-safe cleanup path as a host shutdown.
        }
        catch (Exception exception)
        {
            state = state with
            {
                QuotaSourceStatus = RolloutQuotaSourceStatuses.Failed,
                LastError = DiagnosticMessageSanitizer.Sanitize(exception.Message),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            stateStore.Write(state);
            log.AppendException("ROLLOUT_QUOTA_SOURCE_FAILED", exception);
        }
        finally
        {
            hostLifetimeCancellation.Cancel();
            state = CleanupTitle(state, request, settingsStore, stateStore, log);
            state = state with
            {
                IsRunning = false,
                AppServerConnected = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            stateStore.Write(state);
        }

        return new McpHelperServiceRunResult(true, state);
    }

    private static HelperRuntimeState RefreshQuotas(
        HelperRuntimeState state,
        IRolloutQuotaSource quotaSource,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log,
        string trigger)
    {
        var result = quotaSource.Refresh();
        state = state with
        {
            QuotaSource = "rollout",
            QuotaSourceStatus = result.Status,
            ActiveSessionId = result.ActiveSessionId,
            RolloutAvailable = result.RolloutAvailable,
            Quotas = result.Quotas is null ? Array.Empty<HelperQuotaSnapshot>() : ToRuntimeQuotas(result.Quotas),
            LastQuotaUpdatedAtUtc = result.LastQuotaUpdatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        stateStore.Write(state);
        log.TryAppend($"ROLLOUT_QUOTA_SOURCE_TRIGGER={trigger} STATUS={result.Status}");
        return state;
    }

    private HelperRuntimeState RefreshTitle(
        HelperRuntimeState state,
        McpHelperStartupRequest request,
        CodexQuotaTitleSettingsStore settingsStore,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log,
        string trigger)
    {
        var settingsResult = settingsStore.LoadOrCreate();
        LogSettingsLoad(log, settingsResult);
        var settings = settingsResult.Settings;
        var validation = _templateRenderer.Validate(settings.Template);
        if (!validation.IsValid)
        {
            log.TryAppend($"TITLE_TEMPLATE_INVALID REASON={validation.Error}");
            settings = CodexQuotaTitleSettings.Defaults with { Enabled = settings.Enabled };
        }
        else
        {
            foreach (var placeholder in validation.UnknownPlaceholders)
            {
                log.TryAppend($"TITLE_TEMPLATE_UNKNOWN_PLACEHOLDER={placeholder}");
            }
        }

        var windows = _hostWindowProvider.ListTopLevelWindowsForProcess(request.HostChatGptPid);
        var visibleWindowCount = windows.Count(window => window.IsVisible && !string.IsNullOrWhiteSpace(window.Title));
        var correlation = visibleWindowCount == 1
            ? AutomaticTitleUpdateService.UnambiguousCorrelation
            : "WINDOW_CORRELATION_AMBIGUOUS";
        var titleResult = _titleUpdateService.Apply(new AutomaticTitleUpdateRequest(
            request.HostChatGptPid,
            correlation,
            windows,
            settings.Enabled,
            settings.Template,
            settings.DateFormat,
            HelperQuotaReadResultConverter.ToQuotaReadResult(state),
            state.CustomTitleBaseTitle));

        state = state with
        {
            CandidateWindowCount = windows.Count,
            WindowCorrelation = correlation,
            Windows = ToRuntimeWindows(windows),
            ActiveTemplate = settings.Template,
            DateFormat = settings.DateFormat,
            Enabled = settings.Enabled,
            LastRenderedTitle = titleResult.RenderedTitle ?? (titleResult.Cleaned ? null : state.LastRenderedTitle),
            CustomTitleBaseTitle = titleResult.Success ? titleResult.CustomTitleBaseTitle : state.CustomTitleBaseTitle,
            LastTitleUpdatedAtUtc = titleResult.Changed ? DateTimeOffset.UtcNow : state.LastTitleUpdatedAtUtc,
            LastError = titleResult.Success ? null : titleResult.FailureReason,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        stateStore.Write(state);
        log.TryAppend($"TITLE_REFRESH_TRIGGER={trigger} WINDOW_CANDIDATE_COUNT={windows.Count} {correlation}");
        if (titleResult.Success && titleResult.Changed)
        {
            log.TryAppend(titleResult.Cleaned ? "TITLE_CLEANUP_OK" : "TITLE_AUTO_UPDATE_OK");
        }
        else if (!titleResult.Success)
        {
            log.TryAppend($"TITLE_AUTO_UPDATE_SKIPPED REASON={titleResult.FailureReason}");
        }

        return state;
    }

    private HelperRuntimeState CleanupTitle(
        HelperRuntimeState state,
        McpHelperStartupRequest request,
        CodexQuotaTitleSettingsStore settingsStore,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log)
    {
        var settings = settingsStore.LoadOrCreate().Settings;
        var windows = _hostWindowProvider.ListTopLevelWindowsForProcess(request.HostChatGptPid);
        var visibleWindowCount = windows.Count(window => window.IsVisible && !string.IsNullOrWhiteSpace(window.Title));
        var correlation = visibleWindowCount == 1
            ? AutomaticTitleUpdateService.UnambiguousCorrelation
            : "WINDOW_CORRELATION_AMBIGUOUS";
        var result = _titleUpdateService.Apply(new AutomaticTitleUpdateRequest(
            request.HostChatGptPid,
            correlation,
            windows,
            false,
            settings.Template,
            settings.DateFormat,
            HelperQuotaReadResultConverter.ToQuotaReadResult(state),
            state.CustomTitleBaseTitle));
        if (result.Success && result.Cleaned)
        {
            log.TryAppend("TITLE_CLEANUP_OK");
        }
        else if (!result.Success || result.OwnershipLost)
        {
            log.TryAppend($"TITLE_CLEANUP_SKIPPED REASON={result.FailureReason ?? "ownership-lost"}");
        }

        state = state with
        {
            LastRenderedTitle = result.Cleaned ? null : state.LastRenderedTitle,
            CustomTitleBaseTitle = result.Success ? result.CustomTitleBaseTitle : state.CustomTitleBaseTitle,
            LastError = result.Success ? null : result.FailureReason,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        stateStore.Write(state);
        return state;
    }

    private static HelperRuntimeState CreateInitialState(McpHelperStartupRequest request, CodexQuotaTitleSettings settings) =>
        new(
            request.Profile.ProfileHash,
            request.Origin,
            Environment.ProcessId,
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
            PluginVersion = HelperVersion,
            HelperVersion = HelperVersion,
            ActiveTemplate = settings.Template,
            DateFormat = settings.DateFormat,
            Enabled = settings.Enabled,
            QuotaSource = "rollout",
            QuotaSourceStatus = RolloutQuotaSourceStatuses.WaitingSession,
        };

    private static void LogSettingsLoad(HelperServiceLog log, CodexQuotaTitleSettingsLoadResult result)
    {
        if (result.Error is not null)
        {
            log.TryAppend($"SETTINGS_LOAD_FALLBACK REASON={result.Error}");
        }
    }

    private static IReadOnlyList<HelperQuotaSnapshot> ToRuntimeQuotas(QuotaReadResult quotas) =>
        quotas.AllSnapshots()
            .Distinct()
            .Select(snapshot => new HelperQuotaSnapshot(
                snapshot.LimitId,
                snapshot.LimitName,
                ToRuntimeWindow(snapshot.Primary),
                ToRuntimeWindow(snapshot.Secondary)))
            .ToList();

    private static HelperQuotaWindow? ToRuntimeWindow(RateLimitWindow? window) =>
        window is null
            ? null
            : new HelperQuotaWindow(
                window.WindowDurationMins,
                window.UsedPercent,
                window.RemainingPercent,
                window.ResetsAt?.ToLocalTime());

    private static IReadOnlyList<HelperWindowCandidate> ToRuntimeWindows(IEnumerable<WindowCandidate> windows) =>
        windows.Select(window => new HelperWindowCandidate(
                $"0x{window.Handle.ToInt64():X}",
                window.ProcessId,
                window.IsVisible,
                window.ClassName,
                window.Title))
            .ToList();
}

public sealed class ProcessHostLifetime : IHostProcessLifetime
{
    public async Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (ArgumentException)
        {
            // The desktop host already exited before the helper completed its startup.
        }
        catch (InvalidOperationException)
        {
            // Process handles can become invalid while the desktop host is shutting down.
        }
    }
}

public sealed class WindowsHostWindowProvider : IHostWindowProvider
{
    public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return Array.Empty<WindowCandidate>();
        }

        return new WindowTitleService().ListTopLevelWindowsForProcess((uint)processId);
    }
}
