using System.ComponentModel;
using System.Runtime.InteropServices;
using Lulu.Core.Contracts;

namespace Lulu.Windows;

public sealed class CursorService : ICursorService
{
    private const uint MonitorDefaultToNearest = 2;

    public (int X, int Y) GetPosition()
    {
        if (!GetCursorPos(out var point))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the cursor position.");
        }

        return (point.X, point.Y);
    }

    public (int Left, int Top, int Right, int Bottom) GetWorkingArea(int x, int y)
    {
        var info = GetMonitorInformation(x, y);
        return (info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom);
    }

    public (int Left, int Top, int Right, int Bottom) GetMonitorArea(int x, int y)
    {
        var info = GetMonitorInformation(x, y);
        return (info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }

    private static MonitorInfo GetMonitorInformation(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the monitor work area.");
        }

        return info;
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
