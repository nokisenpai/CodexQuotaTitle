using CodexQuotaTitle.Processes;

namespace CodexQuotaTitle.Windows;

public sealed record WindowCandidate(
    nint Handle,
    uint ProcessId,
    string ProcessName,
    string ClassName,
    string Title,
    bool IsVisible,
    IReadOnlyList<InspectedProcess> ProcessChain,
    IReadOnlyList<int> SharedProcessIds);
