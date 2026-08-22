using System.Runtime.InteropServices;
using System.Text;

namespace CodexQuotaTitle.Windows;

internal static class NativeWindowApi
{
    internal delegate bool EnumWindowsProc(nint handle, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint handle, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint handle, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowTextW(nint handle, string title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint handle, int command);

    internal static string GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLengthW(handle);
        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        return GetWindowTextW(handle, buffer, buffer.Capacity) == 0 ? string.Empty : buffer.ToString();
    }

    internal static string GetClassName(nint handle)
    {
        var buffer = new StringBuilder(256);
        return GetClassNameW(handle, buffer, buffer.Capacity) == 0 ? string.Empty : buffer.ToString();
    }
}
