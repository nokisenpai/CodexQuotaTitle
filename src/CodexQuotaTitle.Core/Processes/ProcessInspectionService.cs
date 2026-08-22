using System.Runtime.InteropServices;

namespace CodexQuotaTitle.Processes;

public sealed record InspectedProcess(int ProcessId, int? ParentProcessId, string ProcessName);

public sealed class ProcessInspectionService
{
    public IReadOnlyList<InspectedProcess> GetProcessChain(int processId, int maximumDepth = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1);

        var processes = GetProcessMap();
        var chain = new List<InspectedProcess>();
        var visited = new HashSet<int>();
        var currentProcessId = processId;

        while (chain.Count < maximumDepth && currentProcessId > 0 && visited.Add(currentProcessId))
        {
            if (!processes.TryGetValue(currentProcessId, out var process))
            {
                chain.Add(new InspectedProcess(currentProcessId, null, "(unavailable)"));
                break;
            }

            chain.Add(process);
            currentProcessId = process.ParentProcessId ?? 0;
        }

        return chain;
    }

    private static IReadOnlyDictionary<int, InspectedProcess> GetProcessMap()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Dictionary<int, InspectedProcess>();
        }

        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32CsSnapProcess, 0);
        if (snapshot == NativeMethods.InvalidHandleValue)
        {
            return new Dictionary<int, InspectedProcess>();
        }

        try
        {
            var entry = new NativeMethods.ProcessEntry32
            {
                DwSize = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>(),
            };
            var processes = new Dictionary<int, InspectedProcess>();
            if (!NativeMethods.Process32First(snapshot, ref entry))
            {
                return processes;
            }

            do
            {
                if (entry.ProcessId > int.MaxValue)
                {
                    continue;
                }

                var processId = (int)entry.ProcessId;
                int? parentProcessId = entry.ParentProcessId is 0 or > int.MaxValue
                    ? null
                    : (int)entry.ParentProcessId;
                processes[processId] = new InspectedProcess(processId, parentProcessId, entry.ExeFile);
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));

            return processes;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }

    private static class NativeMethods
    {
        internal const uint Th32CsSnapProcess = 0x00000002;
        internal static readonly nint InvalidHandleValue = new(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(nint handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry32
        {
            internal uint DwSize;
            private readonly uint _usage;
            internal uint ProcessId;
            private readonly nint _defaultHeapId;
            private readonly uint _moduleId;
            private readonly uint _threads;
            internal uint ParentProcessId;
            private readonly int _priorityClassBase;
            private readonly uint _flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string ExeFile;
        }
    }
}
