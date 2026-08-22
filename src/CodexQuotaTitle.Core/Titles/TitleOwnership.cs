using System.Text;

namespace CodexQuotaTitle.Titles;

public interface ITitleSentinelStrategy
{
    string StartMarker { get; }

    string EndMarker { get; }
}

/// <summary>
/// Uses only Unicode format characters. The marker values are intentionally isolated behind
/// <see cref="ITitleSentinelStrategy"/> so they can be replaced after Windows title-bar testing.
/// </summary>
public sealed class InvisibleUnicodeTitleSentinelStrategy : ITitleSentinelStrategy
{
    public const string DefaultStartMarker = "\u2063\u2064\u2063\u2064";
    public const string DefaultEndMarker = "\u2064\u2063\u2064\u2063";

    public string StartMarker => DefaultStartMarker;

    public string EndMarker => DefaultEndMarker;
}

public sealed class TitleOwnership
{
    private readonly ITitleSentinelStrategy _sentinels;

    public TitleOwnership(ITitleSentinelStrategy? sentinels = null)
    {
        _sentinels = sentinels ?? new InvisibleUnicodeTitleSentinelStrategy();
        if (string.IsNullOrEmpty(_sentinels.StartMarker) || string.IsNullOrEmpty(_sentinels.EndMarker))
        {
            throw new ArgumentException("Title ownership markers cannot be empty.", nameof(sentinels));
        }
    }

    /// <summary>
    /// Removes only complete CodexQuotaTitle-owned ranges. An incomplete range is retained rather
    /// than guessed at, so unrelated title text is never discarded.
    /// </summary>
    public string GetBaseTitle(string currentTitle)
    {
        ArgumentNullException.ThrowIfNull(currentTitle);

        var result = new StringBuilder(currentTitle.Length);
        var cursor = 0;

        while (cursor < currentTitle.Length)
        {
            var start = currentTitle.IndexOf(_sentinels.StartMarker, cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(currentTitle, cursor, currentTitle.Length - cursor);
                break;
            }

            result.Append(currentTitle, cursor, start - cursor);
            var contentStart = start + _sentinels.StartMarker.Length;
            var end = currentTitle.IndexOf(_sentinels.EndMarker, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                result.Append(currentTitle, start, currentTitle.Length - start);
                break;
            }

            cursor = end + _sentinels.EndMarker.Length;
        }

        return result.ToString();
    }

    public bool HasCompleteOwnedRange(string currentTitle)
    {
        ArgumentNullException.ThrowIfNull(currentTitle);

        var start = currentTitle.IndexOf(_sentinels.StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var contentStart = start + _sentinels.StartMarker.Length;
        return currentTitle.IndexOf(_sentinels.EndMarker, contentStart, StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Returns true only when one complete owned range is the entire title. This represents a
    /// fully custom template that intentionally has no Codex base-title content.
    /// </summary>
    public bool IsFullyOwnedTitle(string currentTitle)
    {
        ArgumentNullException.ThrowIfNull(currentTitle);

        if (!currentTitle.StartsWith(_sentinels.StartMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var contentStart = _sentinels.StartMarker.Length;
        var end = currentTitle.IndexOf(_sentinels.EndMarker, contentStart, StringComparison.Ordinal);
        return end >= contentStart && end + _sentinels.EndMarker.Length == currentTitle.Length;
    }

    public string GetVisibleTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title
            .Replace(_sentinels.StartMarker, string.Empty, StringComparison.Ordinal)
            .Replace(_sentinels.EndMarker, string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a new title from the current title. Existing owned ranges are removed first, while
    /// all remaining content belongs to Codex or other software and is preserved as the base title.
    /// </summary>
    public string Apply(string currentTitle, string contentBeforeBaseTitle, string contentAfterBaseTitle)
    {
        ArgumentNullException.ThrowIfNull(currentTitle);
        ArgumentNullException.ThrowIfNull(contentBeforeBaseTitle);
        ArgumentNullException.ThrowIfNull(contentAfterBaseTitle);

        return ApplyToBaseTitle(GetBaseTitle(currentTitle), contentBeforeBaseTitle, contentAfterBaseTitle);
    }

    /// <summary>
    /// Applies ownership around an explicit base title. The caller supplies it when transitioning
    /// back from a fully custom title whose original base is retained by the helper runtime.
    /// </summary>
    public string ApplyToBaseTitle(string baseTitle, string contentBeforeBaseTitle, string contentAfterBaseTitle)
    {
        ArgumentNullException.ThrowIfNull(baseTitle);
        ArgumentNullException.ThrowIfNull(contentBeforeBaseTitle);
        ArgumentNullException.ThrowIfNull(contentAfterBaseTitle);

        return string.Concat(
            WrapOwnedContent(contentBeforeBaseTitle),
            baseTitle,
            WrapOwnedContent(contentAfterBaseTitle));
    }

    /// <summary>
    /// Wraps a complete, user-visible title in one owned range. There is intentionally no visible
    /// base-title content in this form.
    /// </summary>
    public string ApplyFullyOwnedTitle(string visibleTitle)
    {
        ArgumentException.ThrowIfNullOrEmpty(visibleTitle);
        return WrapOwnedContent(visibleTitle);
    }

    private string WrapOwnedContent(string content) =>
        content.Length == 0
            ? string.Empty
            : string.Concat(_sentinels.StartMarker, content, _sentinels.EndMarker);
}
