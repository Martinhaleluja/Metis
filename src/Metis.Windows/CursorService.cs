using System.ComponentModel;
using System.Runtime.InteropServices;
using Metis.Core.Contracts;

namespace Metis.Windows;

public sealed class CursorService : ICursorService
{
    private const uint MonitorDefaultToNearest = 2;

    private (int X, int Y) _lastKnownPosition = (960, 540);

    public (int X, int Y) GetPosition()
    {
        try
        {
            if (GetCursorPos(out var point))
            {
                _lastKnownPosition = (point.X, point.Y);
                return _lastKnownPosition;
            }
        }
        catch
        {
            // Ignore access errors on disconnected or background sessions
        }

        return _lastKnownPosition;
    }

    public (int Left, int Top, int Right, int Bottom) GetWorkingArea(int x, int y)
    {
        var info = GetMonitorInformation(x, y);
        if (info.HasValue)
        {
            return (info.Value.WorkArea.Left, info.Value.WorkArea.Top, info.Value.WorkArea.Right, info.Value.WorkArea.Bottom);
        }
        return (0, 0, 1920, 1080);
    }

    public (int Left, int Top, int Right, int Bottom) GetMonitorArea(int x, int y)
    {
        var info = GetMonitorInformation(x, y);
        if (info.HasValue)
        {
            return (info.Value.Monitor.Left, info.Value.Monitor.Top, info.Value.Monitor.Right, info.Value.Monitor.Bottom);
        }
        return (0, 0, 1920, 1080);
    }

    private static MonitorInfo? GetMonitorInformation(int x, int y)
    {
        try
        {
            var monitor = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != nint.Zero && GetMonitorInfo(monitor, ref info))
            {
                return info;
            }
        }
        catch
        {
            // Fallback to null
        }

        return null;
    }

#pragma warning disable CS0649 // user32 populates the monitor and cursor structures.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
#pragma warning restore CS0649

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
