using System.Globalization;
using System.Text;

namespace CodexQuotaTitle.Quotas;

public static class QuotaConsoleFormatter
{
    public static string Format(QuotaReadResult quotas)
    {
        ArgumentNullException.ThrowIfNull(quotas);

        var builder = new StringBuilder();
        var snapshots = quotas.AllSnapshots().Distinct().ToList();
        if (snapshots.Count == 0)
        {
            return "No rate-limit windows are currently available from codex app-server.";
        }

        foreach (var snapshot in snapshots)
        {
            builder.Append("Limit: ")
                .Append(snapshot.LimitId ?? "(default)")
                .AppendLine();
            AppendWindow(builder, "  primary", snapshot.Primary);
            AppendWindow(builder, "  secondary", snapshot.Secondary);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendWindow(StringBuilder builder, string label, RateLimitWindow? window)
    {
        if (window is null)
        {
            return;
        }

        builder.Append(label)
            .Append(": duration=")
            .Append(window.WindowDurationMins?.ToString(CultureInfo.InvariantCulture) ?? "unknown")
            .Append(" min, used=")
            .Append(window.UsedPercent.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("%, remaining=")
            .Append(window.RemainingPercent.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("%, reset local=")
            .AppendLine(window.ResetsAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "unknown");
    }
}
