using System.Globalization;
using System.Text;

namespace CodexQuotaTitle.Mcp;

public sealed class HelperServiceLog
{
    private const string FileName = "helper-service.log";
    private readonly string _directoryPath;

    public HelperServiceLog(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public bool TryAppend(string marker)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var line = $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} PID={Environment.ProcessId} {Sanitize(marker)}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(_directoryPath, FileName), line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

    public void AppendException(string marker, Exception exception) =>
        TryAppend($"{marker} EXCEPTION_TYPE={exception.GetType().Name} MESSAGE={exception.Message}");

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("secret", StringComparison.OrdinalIgnoreCase)
            ? "[REDACTED]"
            : singleLine;
    }
}
