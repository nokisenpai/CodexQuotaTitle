using System.Text.Json;

namespace CodexQuotaTitle.Integration;

public sealed record IntegrationHookInput(
    string? SessionId,
    string? CurrentWorkingDirectory,
    string? Source,
    string? Model,
    string? PermissionMode,
    string? TranscriptPath = null)
{
    public static async Task<IntegrationHookInput> ReadAsync(TextReader input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rawInput = await input.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new IntegrationHookInput(null, null, null, null, null);
        }

        using var document = JsonDocument.Parse(rawInput);
        var root = document.RootElement;
        return new IntegrationHookInput(
            ReadString(root, "session_id"),
            ReadString(root, "cwd"),
            ReadString(root, "source"),
            ReadString(root, "model"),
            ReadString(root, "permission_mode"),
            ReadString(root, "transcript_path"));
    }

    public string SerializeForHelper() =>
        JsonSerializer.Serialize(new
        {
            session_id = SessionId,
            cwd = CurrentWorkingDirectory,
            source = Source,
            model = Model,
            permission_mode = PermissionMode,
            transcript_path = TranscriptPath,
        });

    private static string? ReadString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
