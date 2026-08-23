using System.ComponentModel;
using System.Diagnostics;
using CodexQuotaTitle.AppServer;

namespace CodexQuotaTitle.Mcp;

public sealed record CodexAppServerExecutableResolution(string Executable, string Source, bool Available);

public interface IHostCodexExecutablePathProvider
{
    string? TryGetExecutablePath(int processId);
}

public interface ICodexAppServerExecutableResolver
{
    CodexAppServerExecutableResolution Resolve(int? hostCodexPid);
}

public sealed class HostCodexExecutablePathProvider : IHostCodexExecutablePathProvider
{
    public string? TryGetExecutablePath(int processId)
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
            try
            {
                var hostExecutable = _hostPathProvider.TryGetExecutablePath(hostCodexPid.Value);
                if (IsAvailableAbsolutePath(hostExecutable))
                {
                    return new CodexAppServerExecutableResolution(hostExecutable!, "host-process", true);
                }
            }
            catch (ArgumentException)
            {
                // The host process may have exited between process-tree inspection and resolution.
            }
            catch (InvalidOperationException)
            {
                // Access to process metadata can become invalid while the host is shutting down.
            }
            catch (Win32Exception)
            {
                // Some Windows process handles do not allow MainModule inspection.
            }
            catch (UnauthorizedAccessException)
            {
                // Restricted process inspection falls back to the normal Codex resolver.
            }
        }

        var fallbackExecutable = _fallbackResolver("codex");
        var fallbackAvailable = IsAvailableAbsolutePath(fallbackExecutable);
        return new CodexAppServerExecutableResolution(
            fallbackExecutable,
            fallbackAvailable ? "path" : "fallback",
            fallbackAvailable);
    }

    private static bool IsAvailableAbsolutePath(string? executable) =>
        !string.IsNullOrWhiteSpace(executable) &&
        Path.IsPathFullyQualified(executable) &&
        File.Exists(executable);
}
