using System.Text;

namespace CodexQuotaTitle.Mcp;

public sealed class McpBootstrapLog
{
    private const string DirectoryName = "CodexQuotaTitle";
    private const string FileName = "mcp-bootstrap.log";
    private readonly string _directoryPath;

    public McpBootstrapLog(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public static McpBootstrapLog CreateDefault() =>
        new(Path.Combine(Path.GetTempPath(), DirectoryName));

    public void WriteProcessEntered(McpStartupContext startupContext, McpProcessTree processTree)
    {
        var processId = Environment.ProcessId;

        TryAppend($"MCP_PROCESS_ENTERED PID={processId} PARENT_PID={processTree.ParentProcessIdMarker} CURRENT_DIRECTORY={Environment.CurrentDirectory}");
        TryAppend($"MCP_PROCESS_CHAIN={processTree.FormatForDiagnostics()}");
        AppendEnvironmentVariable("CODEX_HOME", startupContext.ExplicitCodexHome);
        if (startupContext.CodexHomeSource is { } source)
        {
            TryAppend($"CODEX_HOME_SOURCE={source}");
        }

        if (startupContext.CodexHomeAvailable && startupContext.CodexHome is { } codexHome)
        {
            TryAppend($"CODEX_HOME_RESOLVED_VALUE={codexHome}");
        }
        else
        {
            TryAppend("CODEX_HOME_UNAVAILABLE");
        }

        TryAppend(startupContext.ProfileIdentity is { } profileIdentity
            ? $"PROFILE_IDENTITY={profileIdentity.ProfileHash}"
            : "PROFILE_IDENTITY_UNAVAILABLE");
        AppendEnvironmentVariable("PLUGIN_ROOT", Environment.GetEnvironmentVariable("PLUGIN_ROOT"));
        AppendEnvironmentVariable("PLUGIN_DATA", Environment.GetEnvironmentVariable("PLUGIN_DATA"));
    }

    public bool TryAppend(string marker)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var line = $"{DateTimeOffset.UtcNow:O} PID={Environment.ProcessId} {Sanitize(marker)}{Environment.NewLine}";
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

    public void AppendEnvironmentVariable(string name, string? value)
    {
        var marker = string.IsNullOrEmpty(value)
            ? $"{name}_PRESENT=FALSE"
            : $"{name}_PRESENT=TRUE {name}_VALUE={value}";
        TryAppend(marker);
    }

    private static string Sanitize(string value)
    {
        var oneLineValue = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLineValue.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               oneLineValue.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               oneLineValue.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               oneLineValue.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               oneLineValue.Contains("secret", StringComparison.OrdinalIgnoreCase)
            ? "[REDACTED]"
            : oneLineValue;
    }
}
