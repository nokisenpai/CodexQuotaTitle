using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using CodexQuotaTitle.Quotas;
using CodexQuotaTitle.Titles;

namespace CodexQuotaTitle.Windows;

public sealed record TitleWriteTestRequest(
    int HostChatGptProcessId,
    string ExpectedCorrelation,
    string Template,
    QuotaReadResult Quotas);

public sealed record TitleWriteTestResult(
    bool Applied,
    bool RoundTripVerified,
    bool CleanupCompleted,
    bool OwnershipLost,
    nint? TargetHandle,
    string? BaseTitle,
    string? VisibleRenderedTitle,
    string? FailureReason);

public interface ITitleWriteTestWindowCatalog
{
    IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(uint processId);
}

public interface ITitleWriteTestWindowAccessor
{
    bool IsWindow(nint handle);

    uint GetProcessId(nint handle);

    bool IsVisible(nint handle);

    string GetClassName(nint handle);

    string GetWindowTitle(nint handle);

    bool SetWindowTitle(nint handle, string title, out int win32Error);
}

public interface ITitleWriteTestDelay
{
    Task WaitAsync(CancellationToken cancellationToken);
}

public interface ITitleWriteTestLog
{
    void Write(string marker);
}

public sealed class TitleWriteTestService
{
    public const string DefaultTemplate = "%base_title% | W %week_percent_remaining% • %week_reset_date%";
    public const string DefaultWindowClassName = "Chrome_WidgetWin_1";
    public const string UnambiguousCorrelation = "WINDOW_CORRELATION_UNAMBIGUOUS";

    private readonly ITitleWriteTestWindowCatalog _windowCatalog;
    private readonly ITitleWriteTestWindowAccessor _windowAccessor;
    private readonly ITitleWriteTestDelay _delay;
    private readonly ITitleWriteTestLog _log;
    private readonly TitleOwnership _ownership;
    private readonly QuotaTemplateRenderer _templateRenderer;
    private readonly string _requiredWindowClassName;

    public TitleWriteTestService(
        ITitleWriteTestWindowCatalog? windowCatalog = null,
        ITitleWriteTestWindowAccessor? windowAccessor = null,
        ITitleWriteTestDelay? delay = null,
        ITitleWriteTestLog? log = null,
        TitleOwnership? ownership = null,
        QuotaTemplateRenderer? templateRenderer = null,
        string requiredWindowClassName = DefaultWindowClassName)
    {
        _windowCatalog = windowCatalog ?? new Win32TitleWriteTestWindowCatalog();
        _windowAccessor = windowAccessor ?? new Win32TitleWriteTestWindowAccessor();
        _delay = delay ?? new FixedTitleWriteTestDelay(TimeSpan.FromSeconds(10));
        _log = log ?? new NullTitleWriteTestLog();
        _ownership = ownership ?? new TitleOwnership();
        _templateRenderer = templateRenderer ?? new QuotaTemplateRenderer();
        _requiredWindowClassName = requiredWindowClassName;
    }

    public async Task<TitleWriteTestResult> ExecuteAsync(
        TitleWriteTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OperatingSystem.IsWindows())
        {
            return Cancel("windows-only");
        }

        if (!string.Equals(request.ExpectedCorrelation, UnambiguousCorrelation, StringComparison.Ordinal))
        {
            return Cancel("correlation-not-unambiguous");
        }

        if (!IsProcessRunning(request.HostChatGptProcessId))
        {
            return Cancel("host-chatgpt-pid-not-running");
        }

        var target = SelectUnambiguousTarget(request.HostChatGptProcessId);
        if (target is null || !IsTargetValid(target, request.HostChatGptProcessId))
        {
            return Cancel("target-validation-failed");
        }

        // Re-enumerate immediately before the first write; the previous candidate is never trusted.
        var currentTarget = SelectUnambiguousTarget(request.HostChatGptProcessId);
        if (currentTarget is null || currentTarget.Handle != target.Handle || !IsTargetValid(currentTarget, request.HostChatGptProcessId))
        {
            return Cancel("target-changed-before-write");
        }

        _log.Write($"TITLE_TEST_TARGET_VALIDATED HWND=0x{target.Handle.ToInt64():X} PID={request.HostChatGptProcessId}");
        // The base title is deliberately read only after the final PID/class/visibility validation.
        // Do not reuse a title captured from a prior enumeration or a previous test run.
        var currentTitle = _windowAccessor.GetWindowTitle(target.Handle);
        var baseTitle = _ownership.GetBaseTitle(currentTitle);
        var templateParts = _templateRenderer.RenderParts(request.Template, request.Quotas);
        var visibleRenderedTitle = templateParts.Render(baseTitle);
        var titleWithOwnership = _ownership.Apply(currentTitle, templateParts.BeforeBaseTitle, templateParts.AfterBaseTitle);
        _log.Write($"TITLE_TEST_BASE_TITLE={baseTitle}");
        _log.Write($"TITLE_TEST_RENDERED={visibleRenderedTitle}");

        if (!_windowAccessor.SetWindowTitle(target.Handle, titleWithOwnership, out var setError))
        {
            _log.Write($"TITLE_TEST_SET_FAILED WIN32_ERROR={setError.ToString(CultureInfo.InvariantCulture)}");
            return new TitleWriteTestResult(false, false, false, false, target.Handle, baseTitle, visibleRenderedTitle, "set-window-text-failed");
        }

        _log.Write("TITLE_TEST_SET_OK");
        var readTitle = _windowAccessor.GetWindowTitle(target.Handle);
        var roundTripVerified = _ownership.HasCompleteOwnedRange(readTitle) &&
            string.Equals(_ownership.GetVisibleTitle(readTitle), visibleRenderedTitle, StringComparison.Ordinal) &&
            string.Equals(_ownership.GetBaseTitle(readTitle), baseTitle, StringComparison.Ordinal);
        if (!roundTripVerified)
        {
            _log.Write("TITLE_TEST_ROUNDTRIP_FAILED");
            return new TitleWriteTestResult(true, false, false, false, target.Handle, baseTitle, visibleRenderedTitle, "roundtrip-failed");
        }

        _log.Write("TITLE_TEST_ROUNDTRIP_OK");
        await _delay.WaitAsync(cancellationToken);
        return Cleanup(target.Handle, request.HostChatGptProcessId, baseTitle, visibleRenderedTitle);
    }

    private TitleWriteTestResult Cleanup(
        nint handle,
        int hostChatGptProcessId,
        string baseTitle,
        string visibleRenderedTitle)
    {
        _log.Write("TITLE_TEST_CLEANUP_STARTED");
        if (!IsHandleStillOwnedByHost(handle, hostChatGptProcessId))
        {
            _log.Write("TITLE_TEST_CLEANUP_TARGET_INVALID");
            return new TitleWriteTestResult(true, true, false, false, handle, baseTitle, visibleRenderedTitle, "cleanup-target-invalid");
        }

        var currentTitle = _windowAccessor.GetWindowTitle(handle);
        if (!_ownership.HasCompleteOwnedRange(currentTitle))
        {
            _log.Write("TITLE_TEST_OWNERSHIP_LOST");
            return new TitleWriteTestResult(true, true, false, true, handle, baseTitle, visibleRenderedTitle, null);
        }

        var cleanedTitle = _ownership.GetBaseTitle(currentTitle);
        if (!_windowAccessor.SetWindowTitle(handle, cleanedTitle, out var cleanupError))
        {
            _log.Write($"TITLE_TEST_CLEANUP_FAILED WIN32_ERROR={cleanupError.ToString(CultureInfo.InvariantCulture)}");
            return new TitleWriteTestResult(true, true, false, false, handle, baseTitle, visibleRenderedTitle, "cleanup-set-window-text-failed");
        }

        _log.Write("TITLE_TEST_CLEANUP_OK");
        return new TitleWriteTestResult(true, true, true, false, handle, baseTitle, visibleRenderedTitle, null);
    }

    private WindowCandidate? SelectUnambiguousTarget(int hostChatGptProcessId)
    {
        var candidates = _windowCatalog.ListTopLevelWindowsForProcess((uint)hostChatGptProcessId)
            .Where(candidate => candidate.IsVisible)
            .Where(candidate => string.Equals(candidate.ClassName, _requiredWindowClassName, StringComparison.Ordinal))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Title))
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private bool IsTargetValid(WindowCandidate target, int hostChatGptProcessId) =>
        target.ProcessId == hostChatGptProcessId &&
        target.IsVisible &&
        string.Equals(target.ClassName, _requiredWindowClassName, StringComparison.Ordinal) &&
        IsHandleStillOwnedByHost(target.Handle, hostChatGptProcessId) &&
        _windowAccessor.IsVisible(target.Handle) &&
        string.Equals(_windowAccessor.GetClassName(target.Handle), _requiredWindowClassName, StringComparison.Ordinal);

    private bool IsHandleStillOwnedByHost(nint handle, int hostChatGptProcessId) =>
        _windowAccessor.IsWindow(handle) &&
        _windowAccessor.GetProcessId(handle) == hostChatGptProcessId &&
        string.Equals(_windowAccessor.GetClassName(handle), _requiredWindowClassName, StringComparison.Ordinal);

    private TitleWriteTestResult Cancel(string reason)
    {
        _log.Write($"TITLE_TEST_TARGET_REJECTED REASON={reason}");
        return new TitleWriteTestResult(false, false, false, false, null, null, null, reason);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed class Win32TitleWriteTestWindowCatalog : ITitleWriteTestWindowCatalog
{
    public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(uint processId) =>
        OperatingSystem.IsWindows()
            ? ListWindowsOnWindows(processId)
            : Array.Empty<WindowCandidate>();

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<WindowCandidate> ListWindowsOnWindows(uint processId) =>
        new WindowTitleService().ListTopLevelWindowsForProcess(processId);
}

public sealed class Win32TitleWriteTestWindowAccessor : ITitleWriteTestWindowAccessor
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

public sealed class FixedTitleWriteTestDelay : ITitleWriteTestDelay
{
    private readonly TimeSpan _duration;

    public FixedTitleWriteTestDelay(TimeSpan duration)
    {
        _duration = duration;
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        Task.Delay(_duration, cancellationToken);
}

public sealed class TitleWriteTestLog : ITitleWriteTestLog
{
    private readonly string _filePath;

    public TitleWriteTestLog(string directoryPath)
    {
        _filePath = Path.Combine(directoryPath, "title-write-test.log");
    }

    public void Write(string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var line = $"{DateTimeOffset.UtcNow:O} PID={Environment.ProcessId} {Sanitize(marker)}{Environment.NewLine}";
        File.AppendAllText(_filePath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               singleLine.Contains("secret", StringComparison.OrdinalIgnoreCase)
            ? "[REDACTED]"
            : singleLine;
    }
}

public sealed class NullTitleWriteTestLog : ITitleWriteTestLog
{
    public void Write(string marker)
    {
    }
}

[SupportedOSPlatform("windows")]
public static class TitleWriteTestWin32Controlled
{
    public static async Task<TitleWriteTestResult> RunAsync()
    {
        const string baseTitle = "CodexQuotaTitle controlled title-write test";
        const uint wsVisible = 0x10000000;
        const int swShowNoActivate = 4;
        var handle = NativeWindowApi.CreateWindowExW(
            0,
            "STATIC",
            baseTitle,
            wsVisible,
            20,
            20,
            320,
            100,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("Unable to create the controlled title-write test window.");
        }

        try
        {
            NativeWindowApi.ShowWindow(handle, swShowNoActivate);
            var windowClassName = NativeWindowApi.GetClassName(handle);
            if (string.IsNullOrWhiteSpace(windowClassName))
            {
                throw new InvalidOperationException("Unable to read the controlled title-write test window class.");
            }

            var service = new TitleWriteTestService(
                new ControlledWindowCatalog(handle, Environment.ProcessId, baseTitle, windowClassName),
                new Win32TitleWriteTestWindowAccessor(),
                new FixedTitleWriteTestDelay(TimeSpan.Zero),
                new NullTitleWriteTestLog(),
                requiredWindowClassName: windowClassName);
            var quotas = new QuotaReadResult(
                new QuotaSnapshot(
                    "codex",
                    "Codex",
                    null,
                    new RateLimitWindow(0m, 10080, DateTimeOffset.UtcNow.AddDays(7))),
                new Dictionary<string, QuotaSnapshot>());
            return await service.ExecuteAsync(new TitleWriteTestRequest(
                Environment.ProcessId,
                TitleWriteTestService.UnambiguousCorrelation,
                TitleWriteTestService.DefaultTemplate,
                quotas));
        }
        finally
        {
            NativeWindowApi.DestroyWindow(handle);
        }
    }

    private sealed class ControlledWindowCatalog : ITitleWriteTestWindowCatalog
    {
        private readonly nint _handle;
        private readonly int _processId;
        private readonly string _title;
        private readonly string _className;

        public ControlledWindowCatalog(nint handle, int processId, string title, string className)
        {
            _handle = handle;
            _processId = processId;
            _title = title;
            _className = className;
        }

        public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(uint processId) =>
            processId == _processId
                ?
                [
                    new WindowCandidate(
                        _handle,
                        processId,
                        "CodexQuotaTitle.Tests",
                        _className,
                        _title,
                        true,
                        Array.Empty<CodexQuotaTitle.Processes.InspectedProcess>(),
                        Array.Empty<int>()),
                ]
                : Array.Empty<WindowCandidate>();
    }
}
