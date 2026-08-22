using CodexQuotaTitle.Quotas;

namespace CodexQuotaTitle.Mcp;

public static class HelperQuotaReadResultConverter
{
    public static QuotaReadResult ToQuotaReadResult(HelperRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return ToQuotaReadResult(state.Quotas);
    }

    public static QuotaReadResult ToQuotaReadResult(IReadOnlyList<HelperQuotaSnapshot> runtimeQuotas)
    {
        ArgumentNullException.ThrowIfNull(runtimeQuotas);
        var snapshots = runtimeQuotas
            .Select(ToQuotaSnapshot)
            .ToList();
        var primarySnapshot = snapshots.FirstOrDefault();
        var byLimitId = snapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.LimitId))
            .ToDictionary(snapshot => snapshot.LimitId!, StringComparer.Ordinal);
        return new QuotaReadResult(primarySnapshot, byLimitId);
    }

    private static QuotaSnapshot ToQuotaSnapshot(HelperQuotaSnapshot snapshot) =>
        new(
            snapshot.LimitId,
            snapshot.LimitName,
            ToRateLimitWindow(snapshot.Primary),
            ToRateLimitWindow(snapshot.Secondary));

    private static RateLimitWindow? ToRateLimitWindow(HelperQuotaWindow? window) =>
        window is null
            ? null
            : new RateLimitWindow(
                window.UsedPercent,
                window.WindowDurationMins,
                window.ResetsAtLocal);
}
