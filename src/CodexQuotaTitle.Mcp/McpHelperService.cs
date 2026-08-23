using System.Diagnostics;
using CodexQuotaTitle.Accounts;
using CodexQuotaTitle.AppServer;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Quotas;
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
    public const string HelperVersion = "0.1.5";
    private readonly IHelperAppServerSessionFactory _appServerSessionFactory;
    private readonly IHostProcessLifetime _hostProcessLifetime;
    private readonly IHostWindowProvider _hostWindowProvider;
    private readonly ICodexQuotaTitleSettingsStoreFactory _settingsStoreFactory;
    private readonly ISettingsChangeWatcherFactory _settingsChangeWatcherFactory;
    private readonly IAutomaticTitleUpdateService _titleUpdateService;
    private readonly QuotaTemplateRenderer _templateRenderer;
    private readonly TimeSpan _refreshInterval;
    private readonly ICodexAppServerExecutableResolver _appServerExecutableResolver;

    public McpHelperService(
        IHelperAppServerSessionFactory? appServerSessionFactory = null,
        IHostProcessLifetime? hostProcessLifetime = null,
        IHostWindowProvider? hostWindowProvider = null,
        ICodexQuotaTitleSettingsStoreFactory? settingsStoreFactory = null,
        ISettingsChangeWatcherFactory? settingsChangeWatcherFactory = null,
        IAutomaticTitleUpdateService? titleUpdateService = null,
        QuotaTemplateRenderer? templateRenderer = null,
        TimeSpan? refreshInterval = null,
        ICodexAppServerExecutableResolver? appServerExecutableResolver = null)
    {
        _appServerSessionFactory = appServerSessionFactory ?? new CodexHelperAppServerSessionFactory();
        _hostProcessLifetime = hostProcessLifetime ?? new ProcessHostLifetime();
        _hostWindowProvider = hostWindowProvider ?? new WindowsHostWindowProvider();
        _settingsStoreFactory = settingsStoreFactory ?? new CodexQuotaTitleSettingsStoreFactory();
        _settingsChangeWatcherFactory = settingsChangeWatcherFactory ?? new SettingsChangeWatcherFactory();
        _titleUpdateService = titleUpdateService ?? new AutomaticTitleUpdateService();
        _templateRenderer = templateRenderer ?? new QuotaTemplateRenderer();
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(60);
        _appServerExecutableResolver = appServerExecutableResolver ?? new CodexAppServerExecutableResolver();
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
        var quotaNotificationGate = new object();
        QuotaSnapshot? latestQuotaNotification = null;
        var hostExitedDuringStartup = false;
        try
        {
            try
            {
                using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupTimeout.CancelAfter(TimeSpan.FromSeconds(20));

                void RecordExecutableResolution(CodexAppServerExecutableResolution resolution)
                {
                    state = state with
                    {
                        AppServerExecutableSource = resolution.Source,
                        AppServerExecutableAvailable = resolution.Available,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };
                    stateStore.Write(state);
                    log.TryAppend(
                        $"APP_SERVER_EXECUTABLE_SOURCE={resolution.Source} " +
                        $"AVAILABLE={resolution.Available.ToString().ToUpperInvariant()}");
                }

                Task<IHelperAppServerSession> StartAppServerAsync(CodexAppServerExecutableResolution resolution) =>
                    AwaitStartupOperationAsync(
                        _appServerSessionFactory.StartAsync(
                            new CodexAppServerLaunchRequest(request.CodexHome, resolution.Executable),
                            startupTimeout.Token),
                        hostExitTask,
                        startupTimeout);

                await ThrowIfHostExitedDuringStartupAsync(hostExitTask);
                var executableResolution = _appServerExecutableResolver.Resolve(request.HostCodexPid);
                if (string.Equals(executableResolution.Source, "host-process", StringComparison.Ordinal) &&
                    !File.Exists(executableResolution.Executable))
                {
                    executableResolution = _appServerExecutableResolver.Resolve(null);
                }

                RecordExecutableResolution(executableResolution);
                try
                {
                    appServerSession = await StartAppServerAsync(executableResolution).ConfigureAwait(false);
                }
                catch (CodexAppServerException) when (
                    string.Equals(executableResolution.Source, "host-process", StringComparison.Ordinal) &&
                    !File.Exists(executableResolution.Executable))
                {
                    executableResolution = _appServerExecutableResolver.Resolve(null);
                    RecordExecutableResolution(executableResolution);
                    appServerSession = await StartAppServerAsync(executableResolution).ConfigureAwait(false);
                }
                appServerSession.RateLimitsUpdated += (_, snapshot) =>
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

                    log.TryAppend("RATE_LIMITS_UPDATED_NOTIFICATION");
                };
                state = state with
                {
                    AppServerConnected = true,
                    AppServerStatus = "connected",
                    AppServerLastError = null,
                    AppServerLastExceptionType = null,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                stateStore.Write(state);
                log.TryAppend("APP_SERVER_INITIALIZED");

                await ThrowIfHostExitedDuringStartupAsync(hostExitTask);
                var account = await AwaitStartupOperationAsync(
                    appServerSession.ReadAccountAsync(startupTimeout.Token),
                    hostExitTask,
                    startupTimeout);
                state = state with
                {
                    Account = new HelperAccountDiagnostic(account.Type, account.PlanType, account.RequiresOpenAiAuth),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                stateStore.Write(state);
                log.TryAppend("ACCOUNT_READ_OK");

                await ThrowIfHostExitedDuringStartupAsync(hostExitTask);
                var quotas = await AwaitStartupOperationAsync(
                    appServerSession.ReadRateLimitsAsync(startupTimeout.Token),
                    hostExitTask,
                    startupTimeout);
                state = state with { Quotas = ToRuntimeQuotas(quotas), UpdatedAtUtc = DateTimeOffset.UtcNow };
                stateStore.Write(state);
                log.TryAppend("RATE_LIMITS_READ_OK");
            }
            catch (HostChatGptExitedDuringStartupException)
            {
                log.TryAppend("HELPER_SERVICE_STOPPED_HOST_CHATGPT_EXITED");
                hostExitedDuringStartup = true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                state = CreateAppServerFailureState(
                    state,
                    "timed-out",
                    new OperationCanceledException("The Codex App Server startup timed out."));
                stateStore.Write(state);
                log.TryAppend("APP_SERVER_TIMED_OUT");
            }
            catch (CodexAppServerException exception)
            {
                state = CreateAppServerFailureState(state, "unavailable", exception);
                stateStore.Write(state);
                log.AppendException("APP_SERVER_UNAVAILABLE", exception);
            }
            catch (Exception exception)
            {
                state = CreateAppServerFailureState(state, "failed", exception);
                stateStore.Write(state);
                log.AppendException("APP_SERVER_FAILED", exception);
            }
            if (!hostExitedDuringStartup)
            {
                try
                {
                    await ThrowIfHostExitedDuringStartupAsync(hostExitTask);
                }
                catch (HostChatGptExitedDuringStartupException)
                {
                    log.TryAppend("HELPER_SERVICE_STOPPED_HOST_CHATGPT_EXITED");
                    hostExitedDuringStartup = true;
                }
            }

            if (!hostExitedDuringStartup)
            {
                state = RefreshTitle(state, request, settingsStore, stateStore, log, "startup");
                using var settingsWatcher = _settingsChangeWatcherFactory.Create(settingsStore.Paths);

                while (!cancellationToken.IsCancellationRequested)
                {
                    using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var periodicTask = Task.Delay(_refreshInterval, waitCancellation.Token);
                    var settingsTask = settingsWatcher.WaitForChangeAsync(waitCancellation.Token);
                    var notificationTask = quotaNotificationSignal.WaitAsync(waitCancellation.Token);
                    var completed = await Task.WhenAny(hostExitTask, periodicTask, settingsTask, notificationTask);
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
                            state = state with
                            {
                                Quotas = ToRuntimeQuotas(new QuotaReadResult(notification, new Dictionary<string, QuotaSnapshot>())),
                                UpdatedAtUtc = DateTimeOffset.UtcNow,
                            };
                            stateStore.Write(state);
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
                            state = state with { Quotas = ToRuntimeQuotas(quotas), UpdatedAtUtc = DateTimeOffset.UtcNow };
                            stateStore.Write(state);
                            log.TryAppend("RATE_LIMITS_PERIODIC_RESYNC_OK");
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                        {
                            log.AppendException("RATE_LIMITS_PERIODIC_RESYNC_FAILED", exception);
                        }
                    }

                    state = RefreshTitle(state, request, settingsStore, stateStore, log, "periodic");
                }
            }
        }
        finally
        {
            hostLifetimeCancellation.Cancel();
            state = CleanupTitle(state, request, settingsStore, stateStore, log);
            if (appServerSession is not null)
            {
                await appServerSession.DisposeAsync();
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

    private static async Task<T> AwaitStartupOperationAsync<T>(
        Task<T> operation,
        Task hostExitTask,
        CancellationTokenSource startupCancellation)
    {
        if (await Task.WhenAny(operation, hostExitTask) == hostExitTask)
        {
            startupCancellation.Cancel();
            await hostExitTask;
            throw new HostChatGptExitedDuringStartupException();
        }

        return await operation;
    }

    private static async Task ThrowIfHostExitedDuringStartupAsync(Task hostExitTask)
    {
        if (!hostExitTask.IsCompleted)
        {
            return;
        }

        await hostExitTask;
        throw new HostChatGptExitedDuringStartupException();
    }

    private sealed class HostChatGptExitedDuringStartupException : Exception
    {
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
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

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

public sealed class CodexHelperAppServerSessionFactory : IHelperAppServerSessionFactory
{
    public async Task<IHelperAppServerSession> StartAsync(
        CodexAppServerLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CodexAppServerClient.StartAsync(
            new CodexAppServerOptions(
                Executable: request.Executable,
                CodexHome: request.CodexHome,
                ClientVersion: McpHelperService.HelperVersion),
            cancellationToken);
        return new CodexHelperAppServerSession(client);
    }
}

public sealed class CodexHelperAppServerSession : IHelperAppServerSession
{
    private readonly CodexAppServerClient _client;

    public CodexHelperAppServerSession(CodexAppServerClient client)
    {
        _client = client;
    }

    public event EventHandler<QuotaSnapshot>? RateLimitsUpdated
    {
        add => _client.RateLimitsUpdated += value;
        remove => _client.RateLimitsUpdated -= value;
    }

    public Task<CodexAccountSummary> ReadAccountAsync(CancellationToken cancellationToken) =>
        _client.ReadAccountAsync(cancellationToken);

    public Task<QuotaReadResult> ReadRateLimitsAsync(CancellationToken cancellationToken) =>
        _client.ReadRateLimitsAsync(cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
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
