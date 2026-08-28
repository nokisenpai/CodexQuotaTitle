using CodexQuotaTitle.Accounts;
using CodexQuotaTitle.AppServer;
using CodexQuotaTitle.Quotas;

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

internal sealed record HelperAppServerConnectResult(
    IHelperAppServerSession? Session,
    QuotaReadResult? Quotas,
    CodexAccountSummary? Account,
    CodexAppServerExecutableResolution? ExecutableResolution,
    string Status,
    Exception? Failure,
    Exception? AccountFailure,
    bool HostExited)
{
    public bool Connected => Session is not null && Quotas is not null;
}

internal sealed class HelperAppServerConnector
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private readonly IHelperAppServerSessionFactory _sessionFactory;
    private readonly ICodexAppServerExecutableResolver _executableResolver;

    public HelperAppServerConnector(
        IHelperAppServerSessionFactory sessionFactory,
        ICodexAppServerExecutableResolver executableResolver)
    {
        _sessionFactory = sessionFactory;
        _executableResolver = executableResolver;
    }

    public async Task<HelperAppServerConnectResult> ConnectAsync(
        McpHelperStartupRequest request,
        Task hostExitTask,
        CancellationToken cancellationToken)
    {
        IHelperAppServerSession? session = null;
        CodexAppServerExecutableResolution? resolution = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartupTimeout);
            await ThrowIfHostExitedAsync(hostExitTask);

            resolution = _executableResolver.Resolve(request.HostCodexPid);
            if (IsHostProcessSource(resolution.Source) &&
                !File.Exists(resolution.Executable))
            {
                resolution = _executableResolver.Resolve(null);
            }

            try
            {
                session = await StartSessionAsync(request, resolution, hostExitTask, timeout).ConfigureAwait(false);
            }
            catch (CodexAppServerException) when (
                IsHostProcessSource(resolution.Source) &&
                !File.Exists(resolution.Executable))
            {
                resolution = _executableResolver.Resolve(null);
                session = await StartSessionAsync(request, resolution, hostExitTask, timeout).ConfigureAwait(false);
            }

            var quotas = await AwaitHostBoundOperationAsync(
                session.ReadRateLimitsAsync(timeout.Token),
                hostExitTask,
                timeout);

            CodexAccountSummary? account = null;
            Exception? accountFailure = null;
            try
            {
                account = await AwaitHostBoundOperationAsync(
                    session.ReadAccountAsync(timeout.Token),
                    hostExitTask,
                    timeout);
            }
            catch (HostExitedException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                accountFailure = exception;
            }

            return new HelperAppServerConnectResult(
                session,
                quotas,
                account,
                resolution,
                "connected",
                null,
                accountFailure,
                false);
        }
        catch (HostExitedException)
        {
            await DisposeAsync(session);
            return new HelperAppServerConnectResult(null, null, null, resolution, "host-exited", null, null, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeAsync(session);
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await DisposeAsync(session);
            return new HelperAppServerConnectResult(null, null, null, resolution, "timed-out", exception, null, false);
        }
        catch (CodexAppServerException exception)
        {
            await DisposeAsync(session);
            return new HelperAppServerConnectResult(null, null, null, resolution, "unavailable", exception, null, false);
        }
        catch (Exception exception)
        {
            await DisposeAsync(session);
            return new HelperAppServerConnectResult(null, null, null, resolution, "failed", exception, null, false);
        }
    }

    private Task<IHelperAppServerSession> StartSessionAsync(
        McpHelperStartupRequest request,
        CodexAppServerExecutableResolution resolution,
        Task hostExitTask,
        CancellationTokenSource timeout) =>
        AwaitHostBoundOperationAsync(
            _sessionFactory.StartAsync(
                new CodexAppServerLaunchRequest(request.CodexHome, resolution.Executable),
                timeout.Token),
            hostExitTask,
            timeout);

    private static async Task<T> AwaitHostBoundOperationAsync<T>(
        Task<T> operation,
        Task hostExitTask,
        CancellationTokenSource operationCancellation)
    {
        if (await Task.WhenAny(operation, hostExitTask) == hostExitTask)
        {
            operationCancellation.Cancel();
            await hostExitTask;
            throw new HostExitedException();
        }

        return await operation;
    }

    private static async Task ThrowIfHostExitedAsync(Task hostExitTask)
    {
        if (!hostExitTask.IsCompleted)
        {
            return;
        }

        await hostExitTask;
        throw new HostExitedException();
    }

    private static async ValueTask DisposeAsync(IHelperAppServerSession? session)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await session.DisposeAsync();
        }
        catch
        {
            // A failed App Server cleanup must not prevent the rollout fallback from starting.
        }
    }

    private static bool IsHostProcessSource(string source) =>
        source.StartsWith("host-process-", StringComparison.Ordinal);

    private sealed class HostExitedException : Exception
    {
    }
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
