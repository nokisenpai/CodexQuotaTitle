using System.Globalization;
using System.Text.Json;

namespace CodexQuotaTitle.Quotas;

public static class QuotaNormalizer
{
    private const long UnixMillisecondsThreshold = 10_000_000_000;

    public static QuotaReadResult ParseReadResult(JsonElement result)
    {
        var primarySnapshot = result.TryGetProperty("rateLimits", out var rateLimits)
            ? ParseSnapshot(rateLimits)
            : null;

        var snapshots = new Dictionary<string, QuotaSnapshot>(StringComparer.Ordinal);
        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) &&
            byLimitId.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in byLimitId.EnumerateObject())
            {
                var snapshot = ParseSnapshot(item.Value);
                if (snapshot is not null)
                {
                    snapshots[item.Name] = snapshot;
                }
            }
        }

        return new QuotaReadResult(primarySnapshot, snapshots);
    }

    public static QuotaSnapshot? ParseSnapshot(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new QuotaSnapshot(
            ReadString(value, "limitId"),
            ReadString(value, "limitName"),
            ReadWindow(value, "primary"),
            ReadWindow(value, "secondary"));
    }

    private static RateLimitWindow? ReadWindow(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("usedPercent", out var usedPercent) ||
            usedPercent.ValueKind != JsonValueKind.Number ||
            !usedPercent.TryGetDecimal(out var used))
        {
            return null;
        }

        return new RateLimitWindow(
            used,
            ReadInt32(value, "windowDurationMins"),
            ReadResetAt(value, "resetsAt"));
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static DateTimeOffset? ReadResetAt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var timestamp))
        {
            return null;
        }

        try
        {
            return timestamp > UnixMillisecondsThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
