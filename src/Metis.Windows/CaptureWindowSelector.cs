using System.Runtime.InteropServices;

namespace Metis.Windows;

/// <summary>
/// Selects the real application window the user is working with. When a typed
/// request gives focus to Metis, the next eligible window in Z order is used so
/// Metis does not send a screenshot of its own assistant or setup window.
/// </summary>
internal static class CaptureWindowSelector
{
    private const uint GwHwndNext = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmCloaked = 14;
    private const int MaximumWindowsToInspect = 256;

    internal static nint GetTargetWindow()
    {
        var foreground = GetForegroundWindow();
        if (IsEligible(foreground, skipToolWindows: false))
        {
            return foreground;
        }

        var candidate = foreground == nint.Zero ? GetTopWindow(nint.Zero) : GetWindow(foreground, GwHwndNext);
        for (var inspected = 0;
             candidate != nint.Zero && inspected < MaximumWindowsToInspect;
             inspected++, candidate = GetWindow(candidate, GwHwndNext))
        {
            if (IsEligible(candidate, skipToolWindows: true))
            {
                return candidate;
            }
        }

        return nint.Zero;
    }

    private static bool IsEligible(nint window, bool skipToolWindows)
    {
        if (window == nint.Zero || !IsWindowVisible(window) || IsIconic(window))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (skipToolWindows && (GetExtendedStyle(window).ToInt64() & WsExToolWindow) != 0)
        {
            return false;
        }

        if (DwmGetWindowAttribute(window, DwmCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        return GetWindowRect(window, out var bounds) &&
               bounds.Right - bounds.Left > 8 &&
               bounds.Bottom - bounds.Top > 8;
    }

    private static nint GetExtendedStyle(nint window) => IntPtr.Size == 8
        ? GetWindowLongPtr64(window, GwlExStyle)
        : new nint(GetWindowLong32(window, GwlExStyle));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int valueSize);
}
