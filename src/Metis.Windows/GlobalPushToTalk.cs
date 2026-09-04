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
    private const uint VkEscape = 0x1B;
    private readonly object _gate = new();
    private readonly HookProcedure _hookProcedure;
    private readonly ControlKeyTracker _controlKeyTracker = new();
    private readonly DirectAgentVoiceKeyState _directAgentVoiceKeyState = new();
    private readonly ContextActivationKeyState _contextKeyState = new();
    private readonly EmergencyStopKeyState _emergencyStopKeyState = new();
    private readonly ActiveListeningKeyState _activeListeningKeyState = new();
    private CancellationTokenSource? _holdCheckCts;
    private bool _isDictating;
    private nint _hookHandle;
    private bool _disposed;

    public GlobalPushToTalk()
    {
        _hookProcedure = HookCallback;
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? LiveListeningToggled;
    public event EventHandler? DictationPressed;
    public event EventHandler? DictationReleased;
    public event EventHandler? DirectAgentVoicePressed;
    public event EventHandler? DirectAgentVoiceReleased;
    public event EventHandler? EmergencyStopPressed;
    public event EventHandler<ActivationKind>? ContextActivationPressed;
    public event EventHandler<ActivationKind>? ContextActivationReleased;

    /// <summary>
    /// Raised when Shift joins a hold that had already started, so a context
    /// activation becomes an inspect one part-way through.
    /// </summary>
    public event EventHandler? ContextActivationUpgraded;

    /// <summary>Raised when Ctrl+Space turns continuous listening on or off.</summary>
    public event EventHandler? ActiveListeningToggled;

    /// <summary>
    /// Raised when Escape is pressed while <see cref="CancelKeyEnabled"/> is
    /// set. The trace surface cannot take keyboard input itself — it is
    /// deliberately never activated, so it never has focus — which meant the
    /// Escape its own hint offered did nothing at all.
    /// </summary>
    public event EventHandler? CancelPressed;

    /// <summary>
    /// Whether Escape currently belongs to Metis. Only set while a trace is on
    /// screen: swallowing Escape globally would break every other application.
    /// </summary>
    public bool CancelKeyEnabled { get; set; }

    public bool ContextShortcutsEnabled { get; set; } = true;

    public bool DirectAgentShortcutsEnabled { get; set; } = true;

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
                    "Windows could not register Metis's shortcut hook.");
            }
        }
    }

    public void Stop()
    {
        nint handle;
        var fireDictationReleased = false;
        var fireAgentReleased = false;
        lock (_gate)
        {
            handle = _hookHandle;
            _hookHandle = nint.Zero;
            _holdCheckCts?.Cancel();
            _holdCheckCts = null;
            fireDictationReleased = _isDictating;
            _isDictating = false;
            fireAgentReleased = _directAgentVoiceKeyState.IsActive;
            _controlKeyTracker.Reset();
            _directAgentVoiceKeyState.Reset();
            _contextKeyState.Reset();
            _emergencyStopKeyState.Reset();
            _activeListeningKeyState.Reset();
        }

        if (handle != nint.Zero && !UnhookWindowsHookEx(handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not release Metis's shortcut hook.");
        }

        if (fireDictationReleased)
        {
            DictationReleased?.Invoke(this, EventArgs.Empty);
            Released?.Invoke(this, EventArgs.Empty);
        }

        if (fireAgentReleased)
        {
            DirectAgentVoiceReleased?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether either Ctrl key is physically down right now, asked of Windows
    /// rather than remembered. A hook loses key-ups when focus moves mid-chord
    /// or input is injected, and a remembered modifier that never comes back up
    /// silently rewrites what every later keystroke means.
    /// </summary>
    private static bool IsControlHeldNow() =>
        (GetAsyncKeyState(ActiveListeningKeyState.LeftControl) & 0x8000) != 0 ||
        (GetAsyncKeyState(ActiveListeningKeyState.RightControl) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(uint virtualKey);

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0 && data != nint.Zero)
        {
            try
            {
                var keyboard = Marshal.PtrToStructure<LowLevelKeyboardInput>(data);
                var isDown = message == WmKeyDown || message == WmSysKeyDown;
                var isUp = message == WmKeyUp || message == WmSysKeyUp;
                if (isDown || isUp)
                {
                    EventHandler? dictationHandler = null;
                    EventHandler? liveToggleHandler = null;
                    EventHandler? agentHandler = null;
                    var emergencyStop = false;
                    var toggleListening = false;
                    var contextTransition = ContextActivationTransition.None;
                    var contextKind = ActivationKind.Context;

                    var isCtrl = ControlKeyTracker.IsControlKey(keyboard.VirtualKey);

                    lock (_gate)
                    {
                        emergencyStop = _emergencyStopKeyState.Update(keyboard.VirtualKey, isDown);
                        toggleListening = _activeListeningKeyState.Update(
                            keyboard.VirtualKey,
                            isDown,
                            IsControlHeldNow());

                        if (DirectAgentShortcutsEnabled)
                        {
                            var agentTransition = _directAgentVoiceKeyState.Update(keyboard.VirtualKey, isDown);
                            agentHandler = agentTransition switch
                            {
                                PushToTalkTransition.Pressed => DirectAgentVoicePressed,
                                PushToTalkTransition.Released => DirectAgentVoiceReleased,
                                _ => null
                            };
                        }
                        else
                        {
                            _directAgentVoiceKeyState.Reset();
                        }

                        // ControlKeyTracker for Live Listening (Triple-tap Ctrl) and Dictation (Tap-then-hold Ctrl)
                        var ctrlTransition = _controlKeyTracker.Update(keyboard.VirtualKey, isDown);
                        if (ctrlTransition == ControlKeyTransition.TripleTap)
                        {
                            liveToggleHandler = LiveListeningToggled;
                        }
                        else if (ctrlTransition == ControlKeyTransition.HoldEnded)
                        {
                            if (_isDictating)
                            {
                                _isDictating = false;
                                dictationHandler = DictationReleased;
                            }
                        }

                        if (isDown && isCtrl)
                        {
                            _holdCheckCts?.Cancel();
                            // Only initiate the hold timer if this is the second press (tap-then-hold)
                            if (_controlKeyTracker.IsSecondPressCandidate)
                            {
                                _holdCheckCts = new CancellationTokenSource();
                                var token = _holdCheckCts.Token;
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await Task.Delay((int)ControlKeyTracker.HoldThresholdMs, token).ConfigureAwait(false);
                                        if (token.IsCancellationRequested) return;

                                        if (_controlKeyTracker.CheckHold())
                                        {
                                            if (EditableInputDetector.IsFocusedElementEditable())
                                            {
                                                lock (_gate)
                                                {
                                                    _isDictating = true;
                                                }
                                                DictationPressed?.Invoke(this, EventArgs.Empty);
                                                Pressed?.Invoke(this, EventArgs.Empty);
                                            }
                                            else
                                            {
                                                _controlKeyTracker.CancelHold();
                                            }
                                        }
                                    }
                                    catch (OperationCanceledException)
                                    {
                                    }
                                }, token);
                            }
                        }
                        else if ((isDown && !isCtrl) || (isUp && isCtrl))
                        {
                            // Any intervening key or Ctrl release cancels pending hold check
                            _holdCheckCts?.Cancel();
                        }

                        // Direct Agent and Dictation take precedence over screen context
                        if (ContextShortcutsEnabled && !_isDictating && !_directAgentVoiceKeyState.IsActive)
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
                        Task.Run(() => EmergencyStopPressed?.Invoke(this, EventArgs.Empty));
                    }

                    if (toggleListening)
                    {
                        Task.Run(() => ActiveListeningToggled?.Invoke(this, EventArgs.Empty));
                    }

                    if (liveToggleHandler is not null)
                    {
                        var lh = liveToggleHandler;
                        Task.Run(() => lh.Invoke(this, EventArgs.Empty));
                    }

                    if (dictationHandler is not null)
                    {
                        var dh = dictationHandler;
                        Task.Run(() =>
                        {
                            dh.Invoke(this, EventArgs.Empty);
                            Released?.Invoke(this, EventArgs.Empty);
                        });
                    }

                    switch (contextTransition)
                    {
                        case ContextActivationTransition.Pressed:
                            var kindPressed = contextKind;
                            Task.Run(() => ContextActivationPressed?.Invoke(this, kindPressed));
                            break;
                        case ContextActivationTransition.UpgradedToInspect:
                            Task.Run(() => ContextActivationUpgraded?.Invoke(this, EventArgs.Empty));
                            break;
                        case ContextActivationTransition.Released:
                            var kindReleased = contextKind;
                            Task.Run(() => ContextActivationReleased?.Invoke(this, kindReleased));
                            break;
                    }

                    if (agentHandler is not null)
                    {
                        var ah = agentHandler;
                        Task.Run(() => ah.Invoke(this, EventArgs.Empty));
                    }

                    if (CancelKeyEnabled && isDown && keyboard.VirtualKey == VkEscape)
                    {
                        // Swallowed, but only while a trace is up: for that moment
                        // Escape means "put the pen away", and letting it through as
                        // well would also dismiss whatever is underneath.
                        Task.Run(() => CancelPressed?.Invoke(this, EventArgs.Empty));
                        return 1;
                    }

                    if (keyboard.VirtualKey == EmergencyStopKeyState.F12)
                    {
                        return 1;
                    }
                }
            }
            catch
            {
                // Protect low-level Windows hook callback from bubbling exceptions to unmanaged caller
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
