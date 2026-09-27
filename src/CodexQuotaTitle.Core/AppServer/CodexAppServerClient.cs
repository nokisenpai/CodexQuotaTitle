using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexQuotaTitle.Accounts;
using CodexQuotaTitle.Quotas;

namespace CodexQuotaTitle.AppServer;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private const string RateLimitsUpdatedMethod = "account/rateLimits/updated";
    private readonly Process _process;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly Task<string> _standardErrorDrain;
    private int _nextRequestId;

    private CodexAppServerClient(Process process)
    {
        _process = process;
        _reader = process.StandardOutput;
        _writer = process.StandardInput;
        _writer.AutoFlush = true;
        _standardErrorDrain = process.StandardError.ReadToEndAsync();
    }

    public event EventHandler<QuotaSnapshot>? RateLimitsUpdated;

    public static async Task<CodexAppServerClient> StartAsync(
        CodexAppServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CodexAppServerOptions();

        var startInfo = new ProcessStartInfo
        {
            FileName = CodexExecutableResolver.Resolve(options.Executable),
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        if (!string.IsNullOrWhiteSpace(options.CodexHome))
        {
            startInfo.Environment["CODEX_HOME"] = options.CodexHome;
        }

        startInfo.ArgumentList.Add("app-server");

        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new CodexAppServerException("Unable to start codex app-server.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            process.Dispose();
            throw new CodexAppServerException("Unable to start codex app-server.", exception);
        }

        var client = new CodexAppServerClient(process);
        try
        {
            await client.InitializeAsync(options.ClientName, options.ClientTitle, options.ClientVersion, cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<QuotaReadResult> ReadRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("account/rateLimits/read", parameters: null, cancellationToken);
        return QuotaNormalizer.ParseReadResult(result);
    }

    public async Task<CodexAccountSummary> ReadAccountAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("account/read", new { refreshToken = false }, cancellationToken);
        return CodexAccountSummary.Parse(result);
    }

    /// <summary>
    /// Pumps App Server notifications after all outstanding requests have completed.
    /// This client has one stdout reader, so this method must not run concurrently with a request.
    /// </summary>
    public async Task PumpNotificationsAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var message = await ReadMessageAsync(cancellationToken);
            if (message is null)
            {
                return;
            }

            HandleNotification(message.RootElement);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writer.Dispose();

        if (!_process.HasExited)
        {
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _process.WaitForExitAsync(shutdownTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(CancellationToken.None);
            }
        }

        _reader.Dispose();
        await _standardErrorDrain;
        _process.Dispose();
    }

    private async Task InitializeAsync(
        string clientName,
        string? clientTitle,
        string clientVersion,
        CancellationToken cancellationToken)
    {
        var initializeParameters = new
        {
            clientInfo = new
            {
                name = clientName,
                title = clientTitle,
                version = clientVersion,
            },
            capabilities = new
            {
                experimentalApi = false,
                requestAttestation = false,
            },
        };

        await SendRequestAsync("initialize", initializeParameters, cancellationToken);
        await SendNotificationAsync("initialized", cancellationToken);
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var request = new
        {
            id = requestId,
            method,
            @params = parameters,
        };

        await WriteMessageAsync(request, cancellationToken);

        while (true)
        {
            using var response = await ReadMessageAsync(cancellationToken);
            if (response is null)
            {
                throw new CodexAppServerException("codex app-server closed its output stream before responding.");
            }

            var root = response.RootElement;
            if (root.TryGetProperty("id", out var responseId) &&
                responseId.ValueKind == JsonValueKind.Number &&
                responseId.GetInt32() == requestId)
            {
                if (root.TryGetProperty("error", out _))
                {
                    throw new CodexAppServerException($"codex app-server rejected '{method}'.");
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    throw new CodexAppServerException($"codex app-server returned no result for '{method}'.");
                }

                return result.Clone();
            }

            HandleNotification(root);
        }
    }

    private async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new { method }, cancellationToken);
    }

    private async Task WriteMessageAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(json);
    }

    private async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var line = await _reader.ReadLineAsync().WaitAsync(cancellationToken);
        return line is null ? null : JsonDocument.Parse(line);
    }

    private void HandleNotification(JsonElement message)
    {
        if (!message.TryGetProperty("method", out var method) ||
            !string.Equals(method.GetString(), RateLimitsUpdatedMethod, StringComparison.Ordinal) ||
            !message.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("rateLimits", out var rateLimits))
        {
            return;
        }

        var snapshot = QuotaNormalizer.ParseSnapshot(rateLimits);
        if (snapshot is not null)
        {
            RateLimitsUpdated?.Invoke(this, snapshot);
        }
    }
}

public sealed record CodexAppServerOptions(
    string Executable = "codex",
    string? WorkingDirectory = null,
    string? CodexHome = null,
    string ClientName = "CodexQuotaTitle",
    string? ClientTitle = "CodexQuotaTitle",
    string ClientVersion = "0.1.10");

public sealed class CodexAppServerException : Exception
{
    public CodexAppServerException(string message)
        : base(message)
    {
    }

    public CodexAppServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
