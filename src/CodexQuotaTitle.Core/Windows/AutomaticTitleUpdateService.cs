using System.Globalization;
using System.Runtime.InteropServices;
using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Titles;

namespace CodexQuotaTitle.Windows;

public interface IWindowTitleAccessor
{
    bool IsWindow(nint handle);

    uint GetProcessId(nint handle);

    bool IsVisible(nint handle);

    string GetClassName(nint handle);

    string GetWindowTitle(nint handle);

    bool SetWindowTitle(nint handle, string title, out int win32Error);
}

public sealed class Win32WindowTitleAccessor : IWindowTitleAccessor
{
    public bool IsWindow(nint handle) => NativeWindowApi.IsWindow(handle);

    public uint GetProcessId(nint handle)
    {
        NativeWindowApi.GetWindowThreadProcessId(handle, out var processId);
        return processId;
    }

    public bool IsVisible(nint handle) => NativeWindowApi.IsWindowVisible(handle);

    public string GetClassName(nint handle) => NativeWindowApi.GetClassName(handle);

    public string GetWindowTitle(nint handle) => NativeWindowApi.GetWindowTitle(handle);

    public bool SetWindowTitle(nint handle, string title, out int win32Error)
    {
        var succeeded = NativeWindowApi.SetWindowTextW(handle, title);
        win32Error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }
}

public sealed record AutomaticTitleUpdateRequest(
    int HostChatGptProcessId,
    string ExpectedCorrelation,
    IReadOnlyList<WindowCandidate> Windows,
    bool Enabled,
    string Template,
    string DateFormat,
    QuotaReadResult Quotas);

public sealed record AutomaticTitleUpdateResult(
    bool Success,
    bool Changed,
    bool Cleaned,
    bool OwnershipLost,
    string Correlation,
    int CandidateCount,
    string? RenderedTitle,
    string? FailureReason);

public interface IAutomaticTitleUpdateService
{
    AutomaticTitleUpdateResult Apply(AutomaticTitleUpdateRequest request);
}

public sealed class AutomaticTitleUpdateService : IAutomaticTitleUpdateService
{
    public const string UnambiguousCorrelation = "WINDOW_CORRELATION_UNAMBIGUOUS";
    public const string ExpectedWindowClassName = "Chrome_WidgetWin_1";

    private readonly IWindowTitleAccessor _windowAccessor;
    private readonly TitleOwnership _ownership;
    private readonly QuotaTemplateRenderer _renderer;

    public AutomaticTitleUpdateService(
        IWindowTitleAccessor? windowAccessor = null,
        TitleOwnership? ownership = null,
        QuotaTemplateRenderer? renderer = null)
    {
        _windowAccessor = windowAccessor ?? new Win32WindowTitleAccessor();
        _ownership = ownership ?? new TitleOwnership();
        _renderer = renderer ?? new QuotaTemplateRenderer();
    }

    public AutomaticTitleUpdateResult Apply(AutomaticTitleUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows())
        {
            return Reject("windows-only", 0);
        }

        var targets = request.Windows
            .Where(window => window.IsVisible)
            .Where(window => !string.IsNullOrWhiteSpace(window.Title))
            .Where(window => string.Equals(window.ClassName, ExpectedWindowClassName, StringComparison.Ordinal))
            .ToList();
        if (!string.Equals(request.ExpectedCorrelation, UnambiguousCorrelation, StringComparison.Ordinal) || targets.Count != 1)
        {
            return Reject("window-correlation-ambiguous", targets.Count);
        }

        var target = targets[0];
        if (!IsTargetValid(target, request.HostChatGptProcessId))
        {
            return Reject("window-target-invalid", targets.Count);
        }

        // Read the title only after validating the live HWND; never retain a base title between refreshes.
        var currentTitle = _windowAccessor.GetWindowTitle(target.Handle);
        if (!request.Enabled)
        {
            return Cleanup(target, currentTitle, targets.Count);
        }

        TitleTemplateParts parts;
        try
        {
            parts = _renderer.RenderParts(request.Template, request.Quotas, request.DateFormat);
        }
        catch (FormatException)
        {
            return Reject("template-invalid", targets.Count);
        }

        var baseTitle = _ownership.GetBaseTitle(currentTitle);
        var visibleTitle = parts.Render(baseTitle);
        var ownedTitle = _ownership.Apply(currentTitle, parts.BeforeBaseTitle, parts.AfterBaseTitle);
        if (!_windowAccessor.SetWindowTitle(target.Handle, ownedTitle, out var error))
        {
            return Reject(string.Concat("set-window-text-failed-", error.ToString(CultureInfo.InvariantCulture)), targets.Count);
        }

        var readBack = _windowAccessor.GetWindowTitle(target.Handle);
        var verified = _ownership.HasCompleteOwnedRange(readBack) &&
            string.Equals(_ownership.GetVisibleTitle(readBack), visibleTitle, StringComparison.Ordinal) &&
            string.Equals(_ownership.GetBaseTitle(readBack), baseTitle, StringComparison.Ordinal);
        return verified
            ? new AutomaticTitleUpdateResult(true, true, false, false, UnambiguousCorrelation, targets.Count, visibleTitle, null)
            : Reject("set-window-text-roundtrip-failed", targets.Count);
    }

    private AutomaticTitleUpdateResult Cleanup(WindowCandidate target, string currentTitle, int candidateCount)
    {
        if (!_ownership.HasCompleteOwnedRange(currentTitle))
        {
            return new AutomaticTitleUpdateResult(true, false, false, false, UnambiguousCorrelation, candidateCount, null, "no-owned-title-content");
        }

        var cleanedTitle = _ownership.GetBaseTitle(currentTitle);
        if (!_windowAccessor.SetWindowTitle(target.Handle, cleanedTitle, out var error))
        {
            return Reject(string.Concat("cleanup-set-window-text-failed-", error.ToString(CultureInfo.InvariantCulture)), candidateCount);
        }

        var readBack = _windowAccessor.GetWindowTitle(target.Handle);
        return string.Equals(readBack, cleanedTitle, StringComparison.Ordinal)
            ? new AutomaticTitleUpdateResult(true, true, true, false, UnambiguousCorrelation, candidateCount, null, null)
            : Reject("cleanup-roundtrip-failed", candidateCount);
    }

    private bool IsTargetValid(WindowCandidate target, int expectedProcessId) =>
        target.ProcessId == (uint)expectedProcessId &&
        target.IsVisible &&
        string.Equals(target.ClassName, ExpectedWindowClassName, StringComparison.Ordinal) &&
        _windowAccessor.IsWindow(target.Handle) &&
        _windowAccessor.GetProcessId(target.Handle) == expectedProcessId &&
        _windowAccessor.IsVisible(target.Handle) &&
        string.Equals(_windowAccessor.GetClassName(target.Handle), ExpectedWindowClassName, StringComparison.Ordinal);

    private static AutomaticTitleUpdateResult Reject(string reason, int candidateCount) =>
        new(false, false, false, false, "WINDOW_CORRELATION_AMBIGUOUS", candidateCount, null, reason);
}
