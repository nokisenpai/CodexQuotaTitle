namespace CodexQuotaTitle.Mcp;

public static class DiagnosticMessageSanitizer
{
    private static readonly string[] SensitiveMarkers =
    [
        "token",
        "cookie",
        "api_key",
        "api-key",
        "api key",
        "apikey",
        "secret",
        "authorization",
        "bearer",
        "password",
        "credential",
    ];

    public static string Sanitize(string? value)
    {
        var singleLine = value?.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(singleLine))
        {
            return "No diagnostic message was provided.";
        }

        return SensitiveMarkers.Any(marker => singleLine.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? "[REDACTED]"
            : singleLine;
    }
}
