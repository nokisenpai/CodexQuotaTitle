namespace CodexQuotaTitle.Quotas;

public sealed record RateLimitWindow(
    decimal UsedPercent,
    int? WindowDurationMins,
    DateTimeOffset? ResetsAt)
{
    public decimal RemainingPercent => decimal.Clamp(100m - UsedPercent, 0m, 100m);
}

public sealed record QuotaSnapshot(
    string? LimitId,
    string? LimitName,
    RateLimitWindow? Primary,
    RateLimitWindow? Secondary)
{
    public IEnumerable<RateLimitWindow> Windows()
    {
        if (Primary is not null)
        {
            yield return Primary;
        }

        if (Secondary is not null)
        {
            yield return Secondary;
        }
    }
}

public sealed record QuotaReadResult(
    QuotaSnapshot? RateLimits,
    IReadOnlyDictionary<string, QuotaSnapshot> RateLimitsByLimitId)
{
    public IEnumerable<QuotaSnapshot> AllSnapshots()
    {
        if (RateLimits is not null)
        {
            yield return RateLimits;
        }

        foreach (var snapshot in RateLimitsByLimitId.Values)
        {
            yield return snapshot;
        }
    }
}
