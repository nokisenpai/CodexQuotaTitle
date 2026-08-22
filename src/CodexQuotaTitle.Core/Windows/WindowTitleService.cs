using System.Runtime.Versioning;
using CodexQuotaTitle.Processes;

namespace CodexQuotaTitle.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowTitleService
{
    private readonly ProcessInspectionService _processInspection = new();

    public IReadOnlyList<WindowCandidate> ListCodexCandidates() =>
        ListRelevantWindows(Array.Empty<int>());

    public IReadOnlyList<WindowCandidate> ListTopLevelWindowsForProcess(uint processId)
    {
        var candidates = new List<WindowCandidate>();
        NativeWindowApi.EnumWindows((handle, _) =>
        {
            try
            {
                NativeWindowApi.GetWindowThreadProcessId(handle, out var windowProcessId);
                if (windowProcessId != processId)
                {
                    return true;
                }

                var processName = TryGetProcessName(windowProcessId);
                var processChain = windowProcessId <= int.MaxValue
                    ? _processInspection.GetProcessChain((int)windowProcessId)
                    : Array.Empty<InspectedProcess>();
                candidates.Add(new WindowCandidate(
                    handle,
                    windowProcessId,
                    processName,
                    NativeWindowApi.GetClassName(handle),
                    NativeWindowApi.GetWindowTitle(handle),
                    NativeWindowApi.IsWindowVisible(handle),
                    processChain,
                    Array.Empty<int>()));
            }
            catch
            {
                // One inaccessible window must not prevent correlation of the host process.
            }

            return true;
        }, nint.Zero);

        return candidates;
    }

    public IReadOnlyList<WindowCandidate> ListRelevantWindows(IEnumerable<int> relatedProcessIds)
    {
        ArgumentNullException.ThrowIfNull(relatedProcessIds);

        var relatedProcessIdSet = relatedProcessIds.Where(processId => processId > 0).ToHashSet();
        var candidates = new List<WindowCandidate>();
        NativeWindowApi.EnumWindows((handle, _) =>
        {
            try
            {
                if (!NativeWindowApi.IsWindowVisible(handle))
                {
                    return true;
                }

                var title = NativeWindowApi.GetWindowTitle(handle);
                NativeWindowApi.GetWindowThreadProcessId(handle, out var processId);
                var processName = TryGetProcessName(processId);
                var processChain = processId <= int.MaxValue
                    ? _processInspection.GetProcessChain((int)processId)
                    : Array.Empty<InspectedProcess>();
                var sharedProcessIds = processChain
                    .Select(process => process.ProcessId)
                    .Where(relatedProcessIdSet.Contains)
                    .ToList();

                if (IsCodexProcess(processName) || IsCodexTitle(title) || sharedProcessIds.Count > 0)
                {
                    candidates.Add(new WindowCandidate(
                        handle,
                        processId,
                        processName,
                        NativeWindowApi.GetClassName(handle),
                        title,
                        true,
                        processChain,
                        sharedProcessIds));
                }
            }
            catch
            {
                // One inaccessible window must not prevent correlation of the others.
            }

            return true;
        }, nint.Zero);

        return candidates;
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "(exited)";
        }
        catch (InvalidOperationException)
        {
            return "(unavailable)";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "(unavailable)";
        }
        catch (OverflowException)
        {
            return "(unavailable)";
        }
    }

    private static bool IsCodexProcess(string processName) =>
        processName.Contains("codex", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("chatgpt", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("openai", StringComparison.OrdinalIgnoreCase);

    private static bool IsCodexTitle(string title) =>
        title.Contains("codex", StringComparison.OrdinalIgnoreCase);
}
