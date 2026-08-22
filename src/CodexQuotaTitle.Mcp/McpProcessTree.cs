using CodexQuotaTitle.Processes;

namespace CodexQuotaTitle.Mcp;

public sealed record McpProcessTree(int? ParentProcessId, IReadOnlyList<McpProcessNode> Nodes)
{
    public string ParentProcessIdMarker => ParentProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "UNKNOWN";

    public string FormatForDiagnostics() => Nodes.Count == 0
        ? "UNKNOWN"
        : string.Join(" -> ", Nodes.Select(node => $"{node.ProcessId}:{node.ProcessName}"));

    public int? FindProcessId(string processName) =>
        Nodes.FirstOrDefault(node => string.Equals(node.ProcessName, processName, StringComparison.OrdinalIgnoreCase))?.ProcessId;

    public static McpProcessTree Capture(int processId)
    {
        var inspectedProcesses = new ProcessInspectionService().GetProcessChain(processId, maximumDepth: 64);
        var nodes = inspectedProcesses
            .Select(process => new McpProcessNode(process.ProcessId, process.ProcessName))
            .ToList();
        return new McpProcessTree(inspectedProcesses.FirstOrDefault()?.ParentProcessId, nodes);
    }
}

public sealed record McpProcessNode(int ProcessId, string ProcessName);
