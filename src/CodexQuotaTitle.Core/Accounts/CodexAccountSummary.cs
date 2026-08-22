using System.Text.Json;

namespace CodexQuotaTitle.Accounts;

public sealed record CodexAccountSummary(
    string? Type,
    string? Email,
    string? PlanType,
    bool RequiresOpenAiAuth)
{
    public static CodexAccountSummary Parse(JsonElement result)
    {
        var requiresOpenAiAuth = result.TryGetProperty("requiresOpenaiAuth", out var requiresAuthProperty) &&
            requiresAuthProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            requiresAuthProperty.GetBoolean();

        if (!result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
        {
            return new CodexAccountSummary(null, null, null, requiresOpenAiAuth);
        }

        return new CodexAccountSummary(
            ReadString(account, "type"),
            ReadString(account, "email"),
            ReadString(account, "planType"),
            requiresOpenAiAuth);
    }

    private static string? ReadString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
