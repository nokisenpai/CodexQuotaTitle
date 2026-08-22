namespace CodexQuotaTitle.Quotas;

public static class QuotaAliasResolver
{
    public const int FiveHoursDurationMins = 5 * 60;
    public const int WeekDurationMins = 7 * 24 * 60;

    public static RateLimitWindow? FindFiveHours(QuotaReadResult quotas) =>
        FindByDuration(quotas, FiveHoursDurationMins);

    public static RateLimitWindow? FindWeek(QuotaReadResult quotas) =>
        FindByDuration(quotas, WeekDurationMins);

    public static RateLimitWindow? FindByDuration(QuotaReadResult quotas, int durationMins)
    {
        ArgumentNullException.ThrowIfNull(quotas);

        return quotas.AllSnapshots()
            .SelectMany(snapshot => snapshot.Windows())
            .FirstOrDefault(window => window.WindowDurationMins == durationMins);
    }
}
