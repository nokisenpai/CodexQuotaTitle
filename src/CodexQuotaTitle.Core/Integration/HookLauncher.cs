using System.Reflection;
using System.Diagnostics;

namespace CodexQuotaTitle.Integration;

public sealed class HookLauncher
{
    private readonly HookBootstrapLog _bootstrapLog;

    public HookLauncher(HookBootstrapLog? bootstrapLog = null)
    {
        _bootstrapLog = bootstrapLog ?? new HookBootstrapLog();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _bootstrapLog.TryAppend("LAUNCHER_ENTERED");

        try
        {
            var hookInput = await IntegrationHookInput.ReadAsync(Console.In, cancellationToken);
            var readyEventName = $"Local\\CodexQuotaTitle-LauncherReady-{Guid.NewGuid():N}";
            using var readyEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                readyEventName);
            _bootstrapLog.TryAppend("HELPER_START_REQUESTED");
            using var helper = await StartHelperAsync(readyEventName, hookInput, cancellationToken);
            readyEvent.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            _bootstrapLog.AppendException("LAUNCHER_FAILED", exception);
            throw;
        }
    }

    private async Task<Process> StartHelperAsync(
        string readyEventName,
        IntegrationHookInput hookInput,
        CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to resolve the current executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException("Unable to resolve the entry assembly for the probe helper.");
            }

            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add("--integration-probe");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--ready-event");
        startInfo.ArgumentList.Add(readyEventName);
        startInfo.ArgumentList.Add("--launcher-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var helper = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the integration probe helper.");
        _bootstrapLog.TryAppend($"HELPER_PROCESS_STARTED PID={helper.Id}");
        await helper.StandardInput.WriteLineAsync(hookInput.SerializeForHelper());
        helper.StandardInput.Close();
        cancellationToken.ThrowIfCancellationRequested();
        return helper;
    }
}
