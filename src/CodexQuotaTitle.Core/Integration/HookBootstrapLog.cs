using System.Globalization;

namespace CodexQuotaTitle.Integration;

public sealed class HookBootstrapLog
{
    private const string DirectoryName = "CodexQuotaTitle";
    private const string FileName = "hook-bootstrap.log";
    private readonly string _directoryPath;

    public HookBootstrapLog(string? directoryPath = null)
    {
        _directoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? Path.Combine(Path.GetTempPath(), DirectoryName)
            : Path.GetFullPath(directoryPath);
    }

    public string FilePath => Path.Combine(_directoryPath, FileName);

    public bool TryAppend(string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        try
        {
            Directory.CreateDirectory(_directoryPath);
            using var writer = new StreamWriter(
                new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read));
            writer.WriteLine($"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} PID={Environment.ProcessId} {Sanitize(marker)}");
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void AppendEnvironmentVariable(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var isPresent = !string.IsNullOrWhiteSpace(value);
        var marker = isPresent
            ? $"{name}_PRESENT=TRUE {name}_VALUE={Sanitize(value!)}"
            : $"{name}_PRESENT=FALSE";
        TryAppend(marker);
    }

    public void AppendException(string marker, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        ArgumentNullException.ThrowIfNull(exception);

        TryAppend($"{marker} EXCEPTION_TYPE={exception.GetType().Name} MESSAGE={Sanitize(exception.Message)}");
    }

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            singleLine.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            singleLine.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            singleLine.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
            singleLine.Contains("secret", StringComparison.OrdinalIgnoreCase))
        {
            return "[REDACTED]";
        }

        return singleLine.Length <= 1_024 ? singleLine : string.Concat(singleLine[..1_024], "…");
    }
}
