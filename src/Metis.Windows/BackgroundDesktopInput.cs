using System.Runtime.InteropServices;
using Metis.Core.Models;

namespace Metis.Windows;

internal interface IBackgroundDesktopInput
{
    bool TryHoverAt(int screenX, int screenY, out int error);
    bool TryClickAt(DesktopActionKind kind, int screenX, int screenY, out int error);

    /// <summary>
    /// Posts text character by character to the control that has keyboard
    /// focus, without synthesising keystrokes.
    ///
    /// This is the route for the surfaces the accessibility tree cannot write
    /// to — a document body, a plain edit control, a console — which is most of
    /// the places people actually type. It posts to a specific window rather
    /// than to whatever has focus system-wide, so text cannot land in the
    /// user's own window if they click away mid-sentence.
    /// </summary>
    bool TryTypeText(string text, out int error);
}

/// <summary>
/// Sends mouse messages directly to the application underneath Metis without
/// moving the system pointer. UI Automation remains the preferred click path.
/// </summary>
internal sealed class NativeBackgroundDesktopInput : IBackgroundDesktopInput
{
    private const uint CwpSkipInvisible = 0x0001;
    private const uint CwpSkipDisabled = 0x0002;
    private const uint CwpSkipTransparent = 0x0004;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const nuint MkLButton = 0x0001;
    private const nuint MkRButton = 0x0002;

    public bool TryHoverAt(int screenX, int screenY, out int error)
    {
        if (!TryResolveTarget(screenX, screenY, out _, out var target, out var clientPoint, out error))
        {
            return false;
        }

        return TryPost(target, WmMouseMove, 0, Pack(clientPoint), out error);
    }

    public bool TryClickAt(DesktopActionKind kind, int screenX, int screenY, out int error)
    {
        if (kind is not (DesktopActionKind.LeftClick or DesktopActionKind.DoubleClick or DesktopActionKind.RightClick))
        {
            error = 87;
            return false;
        }

        if (!TryResolveTarget(screenX, screenY, out var root, out var target, out var clientPoint, out error))
        {
            return false;
        }

        _ = SetForegroundWindow(root);
        var packedPoint = Pack(clientPoint);
        if (!TryPost(target, WmMouseMove, 0, packedPoint, out error))
        {
            return false;
        }

        (uint Down, uint Up, nuint Button) sequence = kind == DesktopActionKind.RightClick
            ? (WmRButtonDown, WmRButtonUp, MkRButton)
            : (WmLButtonDown, WmLButtonUp, MkLButton);
        if (!TryPost(target, sequence.Down, sequence.Button, packedPoint, out error) ||
            !TryPost(target, sequence.Up, 0, packedPoint, out error))
        {
            return false;
        }

        return kind != DesktopActionKind.DoubleClick ||
               (TryPost(target, WmLButtonDoubleClick, MkLButton, packedPoint, out error) &&
                TryPost(target, WmLButtonUp, 0, packedPoint, out error));
    }

    private const uint WmChar = 0x0102;

    public bool TryTypeText(string text, out int error)
    {
        error = 0;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var focused = FocusedWindow();
        if (focused == nint.Zero)
        {
            error = 1400;
            return false;
        }

        foreach (var character in text)
        {
            // Newlines arrive as a carriage return; posting the line feed as
            // well gives most editors a blank second line.
            var code = character == '\n' ? '\r' : character;
            if (!TryPost(focused, WmChar, code, 1, out error))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The control with keyboard focus in the foreground window's own thread.
    /// Asking for the thread's focus rather than the desktop's is what keeps
    /// the text going to the window Metis is working in.
    /// </summary>
    private static nint FocusedWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return nint.Zero;
        }

        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        var thread = GetWindowThreadProcessId(foreground, out _);
        if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.Focus != nint.Zero)
        {
            return info.Focus;
        }

        return foreground;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    private static bool TryResolveTarget(
        int screenX,
        int screenY,
        out nint root,
        out nint target,
        out NativePoint clientPoint,
        out int error)
    {
        root = DesktopTargetWindowLocator.FindAt(screenX, screenY);
        target = root;
        clientPoint = new NativePoint(screenX, screenY);
        if (root == nint.Zero)
        {
            error = 1400;
            return false;
        }

        for (var depth = 0; depth < 16; depth++)
        {
            var pointForParent = new NativePoint(screenX, screenY);
            if (!ScreenToClient(target, ref pointForParent))
            {
                break;
            }

            var child = ChildWindowFromPointEx(
                target,
                pointForParent,
                CwpSkipInvisible | CwpSkipDisabled | CwpSkipTransparent);
            if (child == nint.Zero || child == target)
            {
                break;
            }

            target = child;
        }

        clientPoint = new NativePoint(screenX, screenY);
        if (!ScreenToClient(target, ref clientPoint))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        error = 0;
        return true;
    }

    private static bool TryPost(nint window, uint message, nuint wParam, nint lParam, out int error)
    {
        if (PostMessage(window, message, wParam, lParam))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static nint Pack(NativePoint point) =>
        new((point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll")]
    private static extern nint ChildWindowFromPointEx(nint parent, NativePoint point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
