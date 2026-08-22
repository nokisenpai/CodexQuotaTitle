using System.Diagnostics;

namespace CodexQuotaTitle.AppServer;

internal static class CodexExecutableResolver
{
    public static string Resolve(string executable)
    {
        if (!OperatingSystem.IsWindows() || Path.IsPathFullyQualified(executable))
        {
            return executable;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(executable);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return executable;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var resolvedPath = output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return executable;
            }

            return Path.HasExtension(resolvedPath) ? resolvedPath : string.Concat(resolvedPath, ".exe");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return executable;
        }
    }
}
