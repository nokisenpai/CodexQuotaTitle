using System.Globalization;
using System.Text.RegularExpressions;
using CodexQuotaTitle.Quotas;

namespace CodexQuotaTitle.Titles;

public sealed record TitleTemplateParts(string BeforeBaseTitle, string AfterBaseTitle, bool IncludesBaseTitle)
{
    public string Render(string baseTitle) => IncludesBaseTitle
        ? string.Concat(BeforeBaseTitle, baseTitle, AfterBaseTitle)
        : BeforeBaseTitle;
}

public sealed record QuotaTemplateValidationResult(
    bool IsValid,
    IReadOnlyList<string> UnknownPlaceholders,
    string? Error);

public sealed class QuotaTemplateRenderer
{
    public const string BaseTitlePlaceholder = "%base_title%";
    public const string DefaultDateFormat = "dd/MM HH:mm";
    public const string MissingValue = "—";

    private static readonly Regex PlaceholderRegex = new("%[a-z0-9_]+%", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static IReadOnlyList<string> SupportedPlaceholders { get; } = Array.AsReadOnly(new[]
    {
        BaseTitlePlaceholder,
        "%primary_percent_used%",
        "%primary_percent_remaining%",
        "%primary_reset_date%",
        "%secondary_percent_used%",
        "%secondary_percent_remaining%",
        "%secondary_reset_date%",
        "%5h_percent_used%",
        "%5h_percent_remaining%",
        "%5h_reset_date%",
        "%5h_reset_relative%",
        "%week_percent_used%",
        "%week_percent_remaining%",
        "%week_reset_date%",
        "%week_reset_relative%",
    });

    private static readonly HashSet<string> SupportedPlaceholderSet = new(SupportedPlaceholders, StringComparer.Ordinal);

    public TitleTemplateParts RenderParts(
        string template,
        QuotaReadResult quotas,
        string dateFormat = DefaultDateFormat)
    {
        ArgumentException.ThrowIfNullOrEmpty(template);
        ArgumentNullException.ThrowIfNull(quotas);
        var validation = Validate(template);
        if (!validation.IsValid)
        {
            throw new FormatException(validation.Error);
        }

        var baseTitleIndex = template.IndexOf(BaseTitlePlaceholder, StringComparison.Ordinal);
        var includesBaseTitle = baseTitleIndex >= 0;
        var before = includesBaseTitle ? template[..baseTitleIndex] : template;
        var after = includesBaseTitle ? template[(baseTitleIndex + BaseTitlePlaceholder.Length)..] : string.Empty;
        var primary = quotas.RateLimits?.Primary;
        var secondary = quotas.RateLimits?.Secondary;
        var fiveHours = QuotaAliasResolver.FindFiveHours(quotas);
        var week = QuotaAliasResolver.FindWeek(quotas);

        return new TitleTemplateParts(
            RenderFragment(before, primary, secondary, fiveHours, week, dateFormat),
            RenderFragment(after, primary, secondary, fiveHours, week, dateFormat),
            includesBaseTitle);
    }

    public QuotaTemplateValidationResult Validate(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return new QuotaTemplateValidationResult(false, Array.Empty<string>(), "A title template cannot be empty.");
        }

        var baseTitleCount = PlaceholderRegex.Matches(template)
            .Count(match => string.Equals(match.Value, BaseTitlePlaceholder, StringComparison.Ordinal));
        if (baseTitleCount > 1)
        {
            return new QuotaTemplateValidationResult(
                false,
                Array.Empty<string>(),
                $"A title template cannot contain more than one {BaseTitlePlaceholder} placeholder.");
        }

        var unknown = PlaceholderRegex.Matches(template)
            .Select(match => match.Value)
            .Where(value => !SupportedPlaceholderSet.Contains(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new QuotaTemplateValidationResult(true, unknown, null);
    }

    private static string RenderFragment(
        string fragment,
        RateLimitWindow? primary,
        RateLimitWindow? secondary,
        RateLimitWindow? fiveHours,
        RateLimitWindow? week,
        string dateFormat) =>
        PlaceholderRegex.Replace(fragment, match => match.Value switch
        {
            "%primary_percent_used%" => FormatUsedPercent(primary),
            "%primary_percent_remaining%" => FormatRemainingPercent(primary),
            "%primary_reset_date%" => FormatResetDate(primary, dateFormat),
            "%secondary_percent_used%" => FormatUsedPercent(secondary),
            "%secondary_percent_remaining%" => FormatRemainingPercent(secondary),
            "%secondary_reset_date%" => FormatResetDate(secondary, dateFormat),
            "%5h_percent_used%" => FormatUsedPercent(fiveHours),
            "%5h_percent_remaining%" => FormatRemainingPercent(fiveHours),
            "%5h_reset_date%" => FormatResetDate(fiveHours, dateFormat),
            "%5h_reset_relative%" => FormatResetRelative(fiveHours),
            "%week_percent_used%" => FormatUsedPercent(week),
            "%week_percent_remaining%" => FormatRemainingPercent(week),
            "%week_reset_date%" => FormatResetDate(week, dateFormat),
            "%week_reset_relative%" => FormatResetRelative(week),
            _ => match.Value,
        });

    private static string FormatUsedPercent(RateLimitWindow? window) =>
        window is null
            ? MissingValue
            : string.Concat(window.UsedPercent.ToString("0.##", CultureInfo.CurrentCulture), "%");

    private static string FormatRemainingPercent(RateLimitWindow? window) =>
        window is null
            ? MissingValue
            : string.Concat(window.RemainingPercent.ToString("0.##", CultureInfo.CurrentCulture), "%");

    private static string FormatResetDate(RateLimitWindow? window, string dateFormat)
    {
        if (window?.ResetsAt is not { } resetAt)
        {
            return MissingValue;
        }

        var effectiveFormat = string.Equals(dateFormat, DefaultDateFormat, StringComparison.Ordinal)
            ? "dd'/'MM HH:mm"
            : dateFormat;
        try
        {
            return resetAt.ToLocalTime().ToString(effectiveFormat, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return MissingValue;
        }
    }

    private static string FormatResetRelative(RateLimitWindow? window)
    {
        if (window?.ResetsAt is not { } resetAt)
        {
            return MissingValue;
        }

        var remaining = resetAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        return remaining.TotalDays >= 1
            ? string.Concat("in ", Math.Ceiling(remaining.TotalDays).ToString(CultureInfo.InvariantCulture), "d")
            : remaining.TotalHours >= 1
                ? string.Concat("in ", Math.Ceiling(remaining.TotalHours).ToString(CultureInfo.InvariantCulture), "h")
                : string.Concat("in ", Math.Ceiling(remaining.TotalMinutes).ToString(CultureInfo.InvariantCulture), "m");
    }
}
