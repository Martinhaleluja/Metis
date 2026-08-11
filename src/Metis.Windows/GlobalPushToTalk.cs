using System.ComponentModel;
using System.Runtime.InteropServices;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Windows;

public sealed class GlobalPushToTalk : IGlobalPushToTalk
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly object _gate = new();
    private readonly HookProcedure _hookProcedure;
    private readonly PushToTalkKeyState _keyState = new();
    private readonly ContextActivationKeyState _contextKeyState = new();
    private readonly EmergencyStopKeyState _emergencyStopKeyState = new();
    private nint _hookHandle;
    private bool _disposed;

    public GlobalPushToTalk()
    {
        _hookProcedure = HookCallback;
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? EmergencyStopPressed;
    public event EventHandler<ActivationKind>? ContextActivationPressed;
    public event EventHandler<ActivationKind>? ContextActivationReleased;

    public bool ContextShortcutsEnabled { get; set; } = true;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _hookHandle != nint.Zero;
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_hookHandle != nint.Zero)
            {
                return;
            }

            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProcedure, GetModuleHandle(null), 0);
            if (_hookHandle == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows could not register Ctrl+Shift+1 as Metis's hold-to-talk shortcut.");
            }
        }
    }

    public void Stop()
    {
        nint handle;
        var fireReleased = false;
        lock (_gate)
        {
            handle = _hookHandle;
            _hookHandle = nint.Zero;
            fireReleased = _keyState.IsActive;
            _keyState.Reset();
            _contextKeyState.Reset();
            _emergencyStopKeyState.Reset();
        }

        if (handle != nint.Zero && !UnhookWindowsHookEx(handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not release Metis's shortcut hook.");
        }

        if (fireReleased)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var keyboard = Marshal.PtrToStructure<LowLevelKeyboardInput>(data);
            var isDown = message == WmKeyDown || message == WmSysKeyDown;
            var isUp = message == WmKeyUp || message == WmSysKeyUp;
            if (isDown || isUp)
            {
                EventHandler? handler = null;
                var emergencyStop = false;
                var contextTransition = ContextActivationTransition.None;
                var contextKind = ActivationKind.Context;
                lock (_gate)
                {
                    emergencyStop = _emergencyStopKeyState.Update(keyboard.VirtualKey, isDown);
                    var pushToTalk = _keyState.Update(keyboard.VirtualKey, isDown);
                    handler = pushToTalk switch
                    {
                        PushToTalkTransition.Pressed => Pressed,
                        PushToTalkTransition.Released => Released,
                        _ => null
                    };

                    // Hold-to-talk owns the Ctrl+Shift+1 chord. Suppressing the
                    // context state while it is active keeps the two shortcuts
                    // from firing a second overlapping request.
                    if (ContextShortcutsEnabled && !_keyState.IsActive)
                    {
                        contextTransition = _contextKeyState.Update(keyboard.VirtualKey, isDown);
                        contextKind = _contextKeyState.ShiftSeen ? ActivationKind.Inspect : ActivationKind.Context;
                    }
                    else
                    {
                        _contextKeyState.Reset();
                    }
                }

                if (emergencyStop)
                {
                    // This callback runs independently of Metis's inference/action
                    // loop, so F12 can cancel automation even when that loop stalls.
                    EmergencyStopPressed?.Invoke(this, EventArgs.Empty);
                }

                switch (contextTransition)
                {
                    case ContextActivationTransition.Pressed:
                        ContextActivationPressed?.Invoke(this, contextKind);
                        break;
                    case ContextActivationTransition.Released:
                        ContextActivationReleased?.Invoke(this, contextKind);
                        break;
                }

                handler?.Invoke(this, EventArgs.Empty);
                if (keyboard.VirtualKey == EmergencyStopKeyState.F12)
                {
                    return 1;
                }
            }
        }

        return CallNextHookEx(nint.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _disposed = true;
        }
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

#pragma warning disable CS0649 // user32 populates the hook payload.
    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
#pragma warning restore CS0649

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProcedure callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
