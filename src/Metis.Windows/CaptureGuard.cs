using System.Runtime.InteropServices;
using System.Text;
using Metis.Core.Models;

namespace Metis.Windows;

/// <summary>
/// Finds the parts of the screen Metis is not allowed to photograph.
///
/// Metis sends a picture of the whole desktop to a cloud model on every turn,
/// and the desktop is not all the user's to share. Some of what is on it
/// belongs to other people — a message someone sent in confidence, a client's
/// invoice — and some of it is a secret that stops being one the moment it is
/// uploaded.
///
/// Two things mark a window as off limits here, and the accessibility scan adds
/// a third for password fields.
///
/// The application said so. Windows lets a program set a display affinity on
/// its own windows, and WhatsApp and Signal set it on view-once media, as
/// banking apps, password managers and video players do on themselves. That is
/// the author of the program saying "do not record this", and it is a better
/// signal than any list of application names Metis could keep, because it is
/// maintained by the people who know what is sensitive in their own app.
///
/// The user said so. An explicit list of applications Metis must never look at.
/// </summary>
public static class CaptureGuard
{
    private const uint WdaNone = 0x00;
    private const int MaximumWindowsToInspect = 400;

    /// <summary>
    /// Every visible top-level window that must not appear in a capture, in
    /// screen coordinates.
    ///
    /// Failure is deliberately not reported as an empty list. A caller that
    /// could not tell "nothing is protected" from "the check did not run" would
    /// upload a protected window believing it had looked, so
    /// <paramref name="checkFailed"/> says which happened.
    /// </summary>
    public static IReadOnlyList<ProtectedRegion> FindProtectedRegions(
        IReadOnlyCollection<string>? excludedApplications,
        out bool checkFailed)
    {
        checkFailed = false;
        var found = new List<ProtectedRegion>();
        var processNames = new Dictionary<uint, string>();

        try
        {
            var inspected = 0;
            EnumWindows(
                (window, _) =>
                {
                    if (++inspected > MaximumWindowsToInspect)
                    {
                        return false;
                    }

                    try
                    {
                        Inspect(window, excludedApplications, processNames, found);
                    }
                    catch
                    {
                        // One unreadable window must not cost the whole sweep. A
                        // window that cannot be inspected cannot be sized
                        // either, so there is no rectangle to withhold.
                    }

                    return true;
                },
                nint.Zero);
        }
        catch (Exception)
        {
            checkFailed = true;
        }

        return found;
    }

    private static void Inspect(
        nint window,
        IReadOnlyCollection<string>? excludedApplications,
        Dictionary<uint, string> processNames,
        List<ProtectedRegion> found)
    {
        if (!IsWindowVisible(window) || IsIconic(window))
        {
            return;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
        {
            // Metis's own windows are kept out of captures at the source rather
            // than painted over here.
            return;
        }

        if (!GetWindowRect(window, out var bounds))
        {
            return;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (GetWindowDisplayAffinity(window, out var affinity) && affinity != WdaNone)
        {
            found.Add(new ProtectedRegion(
                bounds.Left,
                bounds.Top,
                width,
                height,
                ProtectedRegionReason.ApplicationProtected));
            return;
        }

        if (excludedApplications is not { Count: > 0 })
        {
            return;
        }

        if (!processNames.TryGetValue(processId, out var processName))
        {
            processName = ReadProcessName(processId);
            processNames[processId] = processName;
        }

        var title = ReadWindowTitle(window);
        foreach (var excluded in excludedApplications)
        {
            if (string.IsNullOrWhiteSpace(excluded))
            {
                continue;
            }

            var needle = excluded.Trim();
            if (processName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                title.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new ProtectedRegion(
                    bounds.Left,
                    bounds.Top,
                    width,
                    height,
                    ProtectedRegionReason.UserExcluded));
                return;
            }
        }
    }

    private static string ReadProcessName(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            // The process may have exited between the enumeration and this
            // lookup, or belong to a session this one cannot open.
            return string.Empty;
        }
    }

    private static string ReadWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        return GetWindowText(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);
}
