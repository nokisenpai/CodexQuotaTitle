using System.ComponentModel;
using System.Diagnostics;

namespace CodexQuotaTitle.Mcp;

public interface ISettingsFileLauncher
{
    void Open(string settingsPath);
}

public sealed class WindowsSettingsFileLauncher : ISettingsFileLauncher
{
    public void Open(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = settingsPath,
                UseShellExecute = true,
            });
            if (process is null)
            {
                throw new SettingsFileOpenException("Windows did not start the associated settings application.");
            }
        }
        catch (SettingsFileOpenException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException or FileNotFoundException)
        {
            throw new SettingsFileOpenException("Windows could not open the settings file with its associated application.", exception);
        }
    }
}

public sealed class SettingsFileOpenException : Exception
{
    public SettingsFileOpenException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
