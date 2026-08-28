using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodexQuotaTitle.AppServer;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaTitle.Mcp;

public sealed record CodexAppServerExecutableResolution(string Executable, string Source, bool Available);

public interface IHostCodexExecutablePathProvider
{
    string? TryGetExecutablePathWithQuery(int processId);

    string? TryGetExecutablePathWithMainModule(int processId);
}

public interface ICodexAppServerExecutableResolver
{
    CodexAppServerExecutableResolution Resolve(int? hostCodexPid);
}

public sealed class HostCodexExecutablePathProvider : IHostCodexExecutablePathProvider
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaximumProcessImagePathLength = 32768;

    public string? TryGetExecutablePathWithQuery(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return null;
        }

        using var processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (processHandle.IsInvalid)
        {
            return null;
        }

        var path = new StringBuilder(MaximumProcessImagePathLength);
        var pathLength = (uint)path.Capacity;
        return QueryFullProcessImageName(processHandle, flags: 0, path, ref pathLength) && pathLength > 0
            ? path.ToString(0, checked((int)pathLength))
            : null;
    }

    public string? TryGetExecutablePathWithMainModule(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return null;
            }

            return process.MainModule?.FileName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle processHandle,
        uint flags,
        [Out] StringBuilder executablePath,
        ref uint executablePathLength);
}

public sealed class CodexAppServerExecutableResolver : ICodexAppServerExecutableResolver
{
    private readonly IHostCodexExecutablePathProvider _hostPathProvider;
    private readonly Func<string, string> _fallbackResolver;

    public CodexAppServerExecutableResolver(
        IHostCodexExecutablePathProvider? hostPathProvider = null,
        Func<string, string>? fallbackResolver = null)
    {
        _hostPathProvider = hostPathProvider ?? new HostCodexExecutablePathProvider();
        _fallbackResolver = fallbackResolver ?? CodexExecutableResolver.Resolve;
    }

    public CodexAppServerExecutableResolution Resolve(int? hostCodexPid)
    {
        if (hostCodexPid is > 0)
        {
            var queryExecutable = TryResolveHostExecutable(
                hostCodexPid.Value,
                _hostPathProvider.TryGetExecutablePathWithQuery);
            if (IsAvailableAbsolutePath(queryExecutable))
            {
                return new CodexAppServerExecutableResolution(queryExecutable!, "host-process-query", true);
            }

            var mainModuleExecutable = TryResolveHostExecutable(
                hostCodexPid.Value,
                _hostPathProvider.TryGetExecutablePathWithMainModule);
            if (IsAvailableAbsolutePath(mainModuleExecutable))
            {
                return new CodexAppServerExecutableResolution(mainModuleExecutable!, "host-process-mainmodule", true);
            }
        }

        var fallbackExecutable = _fallbackResolver("codex");
        var fallbackAvailable = IsAvailableAbsolutePath(fallbackExecutable);
        return new CodexAppServerExecutableResolution(
            fallbackExecutable,
            fallbackAvailable ? "path" : "fallback",
            fallbackAvailable);
    }

    private static string? TryResolveHostExecutable(
        int processId,
        Func<int, string?> resolve)
    {
        try
        {
            return resolve(processId);
        }
        catch (ArgumentException)
        {
            // The host process may have exited between process-tree inspection and resolution.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Access to process metadata can become invalid while the host is shutting down.
            return null;
        }
        catch (Win32Exception)
        {
            // Some Windows process handles do not allow the requested image-path inspection.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Restricted process inspection continues with the next compatible resolver.
            return null;
        }
    }

    private static bool IsAvailableAbsolutePath(string? executable) =>
        !string.IsNullOrWhiteSpace(executable) &&
        Path.IsPathFullyQualified(executable) &&
        File.Exists(executable);
}
