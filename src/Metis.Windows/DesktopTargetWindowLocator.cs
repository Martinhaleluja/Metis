using System.Runtime.InteropServices;

namespace Metis.Windows;

/// <summary>Finds the topmost non-Metis application at a desktop coordinate.</summary>
internal static class DesktopTargetWindowLocator
{
    private const uint GwHwndNext = 2;
    private const int DwmCloaked = 14;
    private const int MaximumWindowsToInspect = 512;

    internal static nint FindAt(int screenX, int screenY)
    {
        var candidate = GetTopWindow(nint.Zero);
        for (var inspected = 0;
             candidate != nint.Zero && inspected < MaximumWindowsToInspect;
             inspected++, candidate = GetWindow(candidate, GwHwndNext))
        {
            if (!IsWindowVisible(candidate) || IsIconic(candidate))
            {
                continue;
            }

            _ = GetWindowThreadProcessId(candidate, out var processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                continue;
            }

            if (DwmGetWindowAttribute(candidate, DwmCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                continue;
            }

            if (GetWindowRect(candidate, out var bounds) &&
                screenX >= bounds.Left && screenX < bounds.Right &&
                screenY >= bounds.Top && screenY < bounds.Bottom)
            {
                return candidate;
            }
        }

        return nint.Zero;
    }

    /// <summary>
    /// The window the user is actually working in.
    ///
    /// Needed because a lesson's coordinates age: they were estimated from the
    /// screen as it looked when the walkthrough was planned, so once a step has
    /// opened something the point no longer lands in the right window. Where
    /// the target is named, the window in front is a far better place to look
    /// for it than wherever a stale coordinate happens to fall. Metis's own
    /// windows are skipped for the same reason they are skipped above — the
    /// overlay and the companion sit on top of everything.
    /// </summary>
    internal static nint Foreground()
    {
        var window = GetForegroundWindow();
        if (window == nint.Zero || !IsWindowVisible(window) || IsIconic(window))
        {
            return nint.Zero;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == 0 || processId == (uint)Environment.ProcessId ? nint.Zero : window;
    }

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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int valueSize);
}
