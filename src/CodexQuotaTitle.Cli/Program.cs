using CodexQuotaTitle.AppServer;
using CodexQuotaTitle.Integration;
using CodexQuotaTitle.Mcp;
using CodexQuotaTitle.Profiles;
using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Windows;

if (args is ["rate-limits"])
{
    return await PrintRateLimitsAsync();
}

if (args is ["windows"])
{
    return PrintWindows();
}

if (args is ["--hook-launcher"])
{
    await new HookLauncher().RunAsync();
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "--integration-probe", StringComparison.Ordinal))
{
    return await RunIntegrationProbeAsync(args[1..]);
}

if (args is ["--win32-title-roundtrip"])
{
    return RunWin32TitleRoundTrip();
}

if (args is ["--title-write-test"])
{
    return await RunTitleWriteTestAsync();
}

return PrintUsage();

static async Task<int> PrintRateLimitsAsync()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var executable = Environment.GetEnvironmentVariable("CODEXQUOTATITLE_CODEX_EXECUTABLE");
    var options = string.IsNullOrWhiteSpace(executable) ? null : new CodexAppServerOptions(Executable: executable);
    await using var client = await CodexAppServerClient.StartAsync(options, timeout.Token);
    var quotas = await client.ReadRateLimitsAsync(timeout.Token);
    Console.WriteLine(QuotaConsoleFormatter.Format(quotas));
    return 0;
}

static int PrintWindows()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("The windows command is available only on Windows.");
        return 1;
    }

    var windows = new WindowTitleService().ListCodexCandidates();
    if (windows.Count == 0)
    {
        Console.WriteLine("No relevant Codex/ChatGPT/OpenAI windows found.");
        return 0;
    }

    foreach (var window in windows)
    {
        Console.WriteLine(
            $"HWND=0x{window.Handle.ToInt64():X} PID={window.ProcessId} Process={window.ProcessName} " +
            $"Class={window.ClassName} Visible={window.IsVisible} Title={window.Title}");
    }

    return 0;
}

static async Task<int> RunIntegrationProbeAsync(string[] probeArguments)
{
    var quiet = probeArguments.Contains("--quiet", StringComparer.Ordinal);
    var readyEventName = ReadOption(probeArguments, "--ready-event");
    var bootstrapLog = new HookBootstrapLog();
    int? launcherProcessId = int.TryParse(ReadOption(probeArguments, "--launcher-pid"), out var parsedProcessId)
        ? parsedProcessId
        : null;

    try
    {
        bootstrapLog.TryAppend("HELPER_ENTERED");
        var input = await IntegrationHookInput.ReadAsync(Console.In);
        var result = await new IntegrationProbeRunner(bootstrapLog).RunAsync(input, launcherProcessId, readyEventName);
        if (!quiet)
        {
            Console.WriteLine($"Integration diagnostics written to {result.ReportPath}");
        }

        return 0;
    }
    catch (Exception exception) when (quiet)
    {
        bootstrapLog.AppendException("HELPER_FAILED", exception);
        return 1;
    }
    catch (Exception exception)
    {
        bootstrapLog.AppendException("HELPER_FAILED", exception);
        Console.Error.WriteLine("Integration probe failed before diagnostics could be written.");
        return 1;
    }
}

static int RunWin32TitleRoundTrip()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("The Win32 title round-trip command is available only on Windows.");
        return 1;
    }

    var result = TitleOwnershipWin32RoundTrip.Run();
    Console.WriteLine($"SentinelsSurvived={result.SentinelsSurvived} BaseTitleRecovered={result.RecoveredBaseTitle == result.BaseTitle}");
    return result.SentinelsSurvived && result.RecoveredBaseTitle == result.BaseTitle ? 0 : 1;
}

static async Task<int> RunTitleWriteTestAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("The title write test is available only on Windows.");
        return 1;
    }

    var profile = ProfileIdentity.FromEnvironment();
    if (!profile.IsCodexHomeExplicit)
    {
        Console.Error.WriteLine("The title write test requires an explicit CODEX_HOME.");
        return 1;
    }

    var runtimePaths = HelperRuntimePaths.ForProfile(profile.ProfileHash);
    var state = new HelperRuntimeStateStore(runtimePaths).TryRead();
    if (state is null ||
        !state.IsRunning ||
        !string.Equals(state.ProfileIdentity, profile.ProfileHash, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("No active helper runtime state matches the current CODEX_HOME.");
        return 1;
    }

    var request = new TitleWriteTestRequest(
        state.HostChatGptPid,
        state.WindowCorrelation,
        TitleWriteTestService.DefaultTemplate,
        HelperQuotaReadResultConverter.ToQuotaReadResult(state));
    var result = await new TitleWriteTestService(log: new TitleWriteTestLog(runtimePaths.DirectoryPath)).ExecuteAsync(request);
    if (!result.Applied || !result.RoundTripVerified || !result.CleanupCompleted)
    {
        Console.Error.WriteLine($"Title write test did not complete: {result.FailureReason ?? "ownership-lost"}.");
        return 1;
    }

    Console.WriteLine($"Title write test completed for HWND=0x{result.TargetHandle!.Value.ToInt64():X}.");
    return 0;
}

static string? ReadOption(IReadOnlyList<string> arguments, string name)
{
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.Ordinal))
        {
            return arguments[index + 1];
        }
    }

    return null;
}

static int PrintUsage()
{
    Console.Error.WriteLine("Usage: CodexQuotaTitle <rate-limits|windows|--win32-title-roundtrip|--title-write-test|--hook-launcher|--integration-probe>");
    return 1;
}
