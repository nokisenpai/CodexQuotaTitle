using System.Diagnostics;
using CodexQuotaTitle.AppServer;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Sessions;
using CodexQuotaTitle.Settings;
using CodexQuotaTitle.Titles;
using CodexQuotaTitle.Windows;

namespace CodexQuotaTitle.Mcp;

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
    public const string HelperVersion = "0.1.9";
    private readonly HelperAppServerConnector _appServerConnector;
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
        IRolloutQuotaSourceFactory? rolloutQuotaSourceFactory = null,
        IHelperAppServerSessionFactory? appServerSessionFactory = null,
        ICodexAppServerExecutableResolver? appServerExecutableResolver = null)
    {
        _appServerConnector = new HelperAppServerConnector(
            appServerSessionFactory ?? new CodexHelperAppServerSessionFactory(),
            appServerExecutableResolver ?? new CodexAppServerExecutableResolver());
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
        IHelperAppServerSessionFactory? appServerSessionFactory,
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
            refreshInterval,
            appServerSessionFactory: appServerSessionFactory,
            appServerExecutableResolver: appServerExecutableResolver)
    {
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

        IHelperAppServerSession? appServerSession = null;
        using var quotaNotificationSignal = new SemaphoreSlim(0, 1);
        using var periodicRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var quotaNotificationGate = new object();
        QuotaSnapshot? latestQuotaNotification = null;
        EventHandler<QuotaSnapshot>? quotaNotificationHandler = null;
        try
        {
            using var quotaSource = _rolloutQuotaSourceFactory.Create(request.Profile);
            using var settingsWatcher = _settingsChangeWatcherFactory.Create(settingsStore.Paths);
            state = RefreshRolloutQuotas(state, quotaSource, stateStore, log, "startup", allowReplacement: true);
            state = RefreshTitle(state, request, settingsStore, stateStore, log, "startup-rollout");

            var appServerStart = await TryStartAppServerAsync(
                request,
                state,
                stateStore,
                log,
                hostExitTask,
                cancellationToken);
            state = appServerStart.State;
            appServerSession = appServerStart.Session;
            if (appServerStart.HostExited)
            {
                log.TryAppend("HELPER_SERVICE_STOPPED_HOST_CHATGPT_EXITED");
            }

            if (appServerSession is not null)
            {
                quotaNotificationHandler = (_, snapshot) =>
                {
                    lock (quotaNotificationGate)
                    {
                        latestQuotaNotification = snapshot;
                    }

                    try
                    {
                        quotaNotificationSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // The newest notification is retained; one refresh is sufficient.
                    }
                    catch (ObjectDisposedException)
                    {
                        // The helper is shutting down.
                    }
                };
                appServerSession.RateLimitsUpdated += quotaNotificationHandler;
                state = RefreshTitle(state, request, settingsStore, stateStore, log, "startup-app-server");
            }

            var periodicTask = Task.Delay(_refreshInterval, periodicRefreshCancellation.Token);
            while (!cancellationToken.IsCancellationRequested)
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var settingsTask = settingsWatcher.WaitForChangeAsync(waitCancellation.Token);
                var rolloutTask = quotaSource.WaitForChangeAsync(waitCancellation.Token);
                var notificationTask = quotaNotificationSignal.WaitAsync(waitCancellation.Token);
                var completed = await Task.WhenAny(hostExitTask, periodicTask, settingsTask, rolloutTask, notificationTask);
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

                if (completed == rolloutTask)
                {
                    await rolloutTask;
                    state = RefreshRolloutQuotas(
                        state,
                        quotaSource,
                        stateStore,
                        log,
                        "watcher",
                        allowReplacement: appServerSession is null);
                    state = RefreshTitle(state, request, settingsStore, stateStore, log, "rollout-change");
                    continue;
                }

                if (completed == notificationTask)
                {
                    QuotaSnapshot? notification;
                    lock (quotaNotificationGate)
                    {
                        notification = latestQuotaNotification;
                        latestQuotaNotification = null;
                    }

                    if (notification is not null)
                    {
                        state = ApplyAppServerNotification(state, notification, stateStore, log);
                    }

                    state = RefreshTitle(state, request, settingsStore, stateStore, log, "rate-limits-notification");
                    continue;
                }

                if (appServerSession is not null)
                {
                    try
                    {
                        using var refreshTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        refreshTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                        var quotas = await appServerSession.ReadRateLimitsAsync(refreshTimeout.Token);
                        state = ApplyAppServerQuotas(state, quotas, stateStore, log, "periodic-resync");
                    }
                    catch (Exception exception) when (
                        exception is not OperationCanceledException ||
                        !cancellationToken.IsCancellationRequested)
                    {
                        state = CreateAppServerFailureState(state, "failed", exception);
                        stateStore.Write(state);
                        log.AppendException("RATE_LIMITS_PERIODIC_RESYNC_FAILED", exception);
                        if (quotaNotificationHandler is not null)
                        {
                            appServerSession.RateLimitsUpdated -= quotaNotificationHandler;
                        }

                        await DisposeAppServerSessionAsync(appServerSession, log);
                        appServerSession = null;
                    }
                }

                state = RefreshRolloutQuotas(
                    state,
                    quotaSource,
                    stateStore,
                    log,
                    "periodic-resync",
                    allowReplacement: appServerSession is null);
                state = RefreshTitle(state, request, settingsStore, stateStore, log, "periodic");
                periodicTask = Task.Delay(_refreshInterval, periodicRefreshCancellation.Token);
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
            log.AppendException("QUOTA_REFRESH_FAILED", exception);
        }
        finally
        {
            periodicRefreshCancellation.Cancel();
            hostLifetimeCancellation.Cancel();
            state = CleanupTitle(state, request, settingsStore, stateStore, log);
            if (appServerSession is not null)
            {
                if (quotaNotificationHandler is not null)
                {
                    appServerSession.RateLimitsUpdated -= quotaNotificationHandler;
                }

                await DisposeAppServerSessionAsync(appServerSession, log);
            }

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

    private async Task<AppServerStartResult> TryStartAppServerAsync(
        McpHelperStartupRequest request,
        HelperRuntimeState state,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log,
        Task hostExitTask,
        CancellationToken cancellationToken)
    {
        var result = await _appServerConnector.ConnectAsync(request, hostExitTask, cancellationToken);
        if (result.ExecutableResolution is { } resolution)
        {
            state = state with
            {
                AppServerExecutableSource = resolution.Source,
                AppServerExecutableAvailable = resolution.Available,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            stateStore.Write(state);
            log.TryAppend(
                $"APP_SERVER_EXECUTABLE_SOURCE={resolution.Source} " +
                $"AVAILABLE={resolution.Available.ToString().ToUpperInvariant()}");
        }

        if (result.HostExited)
        {
            return new AppServerStartResult(null, state, true);
        }

        if (!result.Connected)
        {
            var failure = result.Status == "timed-out"
                ? new OperationCanceledException("The Codex App Server startup timed out.")
                : result.Failure ?? new CodexAppServerException("The Codex App Server is unavailable.");
            state = CreateAppServerFailureState(state, result.Status, failure);
            stateStore.Write(state);
            if (result.Status == "timed-out")
            {
                log.TryAppend("APP_SERVER_TIMED_OUT");
            }
            else
            {
                log.AppendException(
                    result.Status == "unavailable" ? "APP_SERVER_UNAVAILABLE" : "APP_SERVER_FAILED",
                    failure);
            }

            return new AppServerStartResult(null, state, false);
        }

        log.TryAppend("APP_SERVER_INITIALIZED");
        state = ApplyAppServerQuotas(state, result.Quotas!, stateStore, log, "startup");
        if (result.Account is { } account)
        {
            state = state with
            {
                Account = new HelperAccountDiagnostic(account.Type, account.PlanType, account.RequiresOpenAiAuth),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            stateStore.Write(state);
            log.TryAppend("ACCOUNT_READ_OK");
        }
        else if (result.AccountFailure is not null)
        {
            log.AppendException("ACCOUNT_READ_FAILED", result.AccountFailure);
        }

        return new AppServerStartResult(result.Session, state, false);
    }

    private static HelperRuntimeState ApplyAppServerQuotas(
        HelperRuntimeState state,
        QuotaReadResult quotas,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log,
        string trigger)
    {
        state = state with
        {
            QuotaSource = "app-server",
            QuotaSourceStatus = "connected",
            QuotaSelection = null,
            Quotas = ToRuntimeQuotas(quotas),
            LastQuotaUpdatedAtUtc = DateTimeOffset.UtcNow,
            AppServerConnected = true,
            AppServerStatus = "connected",
            AppServerLastError = null,
            AppServerLastExceptionType = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        stateStore.Write(state);
        log.TryAppend($"APP_SERVER_QUOTA_SOURCE_TRIGGER={trigger} STATUS=connected");
        return state;
    }

    private static HelperRuntimeState ApplyAppServerNotification(
        HelperRuntimeState state,
        QuotaSnapshot notification,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log)
    {
        var runtimeNotification = ToRuntimeQuota(notification);
        var quotas = state.Quotas.ToList();
        var matchingIndex = string.IsNullOrWhiteSpace(runtimeNotification.LimitId)
            ? (quotas.Count == 1 ? 0 : -1)
            : quotas.FindIndex(item =>
                string.Equals(item.LimitId, runtimeNotification.LimitId, StringComparison.Ordinal));
        if (matchingIndex >= 0)
        {
            quotas[matchingIndex] = runtimeNotification;
        }
        else
        {
            quotas.Add(runtimeNotification);
        }

        state = state with
        {
            QuotaSource = "app-server",
            QuotaSourceStatus = "connected",
            QuotaSelection = null,
            Quotas = quotas,
            LastQuotaUpdatedAtUtc = DateTimeOffset.UtcNow,
            AppServerConnected = true,
            AppServerStatus = "connected",
            AppServerLastError = null,
            AppServerLastExceptionType = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        stateStore.Write(state);
        log.TryAppend("APP_SERVER_QUOTA_SOURCE_TRIGGER=notification STATUS=connected");
        return state;
    }

    private static HelperRuntimeState CreateAppServerFailureState(
        HelperRuntimeState state,
        string appServerStatus,
        Exception exception) =>
        state with
        {
            AppServerConnected = false,
            AppServerStatus = appServerStatus,
            AppServerLastError = DiagnosticMessageSanitizer.Sanitize(exception.Message),
            AppServerLastExceptionType = exception.GetType().Name,
            QuotaSourceStatus = string.Equals(state.QuotaSource, "app-server", StringComparison.Ordinal)
                ? "stale"
                : state.QuotaSourceStatus,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static async ValueTask DisposeAppServerSessionAsync(
        IHelperAppServerSession session,
        HelperServiceLog log)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            log.AppendException("APP_SERVER_DISPOSE_FAILED", exception);
        }
    }

    private sealed record AppServerStartResult(
        IHelperAppServerSession? Session,
        HelperRuntimeState State,
        bool HostExited);

    private static HelperRuntimeState RefreshRolloutQuotas(
        HelperRuntimeState state,
        IRolloutQuotaSource quotaSource,
        HelperRuntimeStateStore stateStore,
        HelperServiceLog log,
        string trigger,
        bool allowReplacement)
    {
        var result = quotaSource.Refresh();
        var rolloutIsNewer = result.LastQuotaUpdatedAtUtc is not null &&
                             (state.LastQuotaUpdatedAtUtc is null ||
                              result.LastQuotaUpdatedAtUtc > state.LastQuotaUpdatedAtUtc);
        var replaceQuotas = result.Quotas is not null &&
                            (state.Quotas.Count == 0 ||
                             !string.Equals(state.QuotaSource, "app-server", StringComparison.Ordinal) ||
                             (allowReplacement && rolloutIsNewer));
        state = state with
        {
            QuotaSource = replaceQuotas ? "rollout" : state.QuotaSource,
            QuotaSourceStatus = replaceQuotas ? result.Status : state.QuotaSourceStatus,
            QuotaSelection = replaceQuotas ? result.QuotaSelection : state.QuotaSelection,
            ActiveSessionId = result.ActiveSessionId,
            RolloutAvailable = result.RolloutAvailable,
            Quotas = replaceQuotas ? ToRuntimeQuotas(result.Quotas!) : state.Quotas,
            LastQuotaUpdatedAtUtc = replaceQuotas ? result.LastQuotaUpdatedAtUtc : state.LastQuotaUpdatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        stateStore.Write(state);
        log.TryAppend(
            $"ROLLOUT_QUOTA_SOURCE_TRIGGER={trigger} STATUS={result.Status} " +
            $"APPLIED={replaceQuotas.ToString().ToUpperInvariant()}");
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
            QuotaSourceStatus = RolloutQuotaSourceStatuses.RolloutNotFound,
            QuotaSelection = RolloutQuotaSelections.ProfileFallback,
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
            .Select(ToRuntimeQuota)
            .ToList();

    private static HelperQuotaSnapshot ToRuntimeQuota(QuotaSnapshot snapshot) =>
        new(
            snapshot.LimitId,
            snapshot.LimitName,
            ToRuntimeWindow(snapshot.Primary),
            ToRuntimeWindow(snapshot.Secondary));

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
