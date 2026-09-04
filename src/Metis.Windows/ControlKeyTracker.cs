namespace Metis.Windows;

public enum ControlKeyTransition
{
    None,
    TripleTap,
    HoldStarted,
    HoldEnded
}

/// <summary>
/// Tracks the Control key to detect:
/// 1. Triple-tap Ctrl: 3 consecutive rapid presses and releases within 1000ms -> toggles Live Listening.
/// 2. Tap-then-hold Ctrl: press once (tap), then press again and hold for >300ms without intervening keys -> triggers Dictation.
/// 3. Normal shortcut non-interference: any intervening key (e.g. Ctrl+C, Ctrl+V, Ctrl+Alt, Ctrl+Shift)
///    or single continuous Ctrl hold immediately cancels tap and hold tracking so standard OS shortcuts work untouched.
/// </summary>
public sealed class ControlKeyTracker
{
    public const uint VirtualKeyControl = 0x11;
    public const uint LeftControl = 0xA2;
    public const uint RightControl = 0xA3;

    public const long HoldThresholdMs = 300;
    public const long TapMaxDurationMs = 350;
    public const long MaxInterTapGapMs = 500;
    public const long MaxTripleTapTotalWindowMs = 1000;

    private readonly object _gate = new();
    private bool _controlDown;
    private bool _isHolding;
    private bool _interveningKeyPressed;
    private bool _isSecondPressCandidate;
    private long _ctrlDownTime;
    private long _firstTapDownTime;
    private long _lastTapUpTime;
    private int _tapCount;

    public bool IsControlDown
    {
        get
        {
            lock (_gate)
            {
                return _controlDown;
            }
        }
    }

    public bool IsHolding
    {
        get
        {
            lock (_gate)
            {
                return _isHolding;
            }
        }
    }

    public bool IsSecondPressCandidate
    {
        get
        {
            lock (_gate)
            {
                return _isSecondPressCandidate;
            }
        }
    }

    public int CurrentTapCount
    {
        get
        {
            lock (_gate)
            {
                return _tapCount;
            }
        }
    }

    public static bool IsControlKey(uint virtualKey) =>
        virtualKey is VirtualKeyControl or LeftControl or RightControl;

    /// <summary>
    /// Updates state upon keyboard event.
    /// </summary>
    public ControlKeyTransition Update(uint virtualKey, bool isDown, long? timestampMs = null)
    {
        var now = timestampMs ?? Environment.TickCount64;
        lock (_gate)
        {
            if (!IsControlKey(virtualKey))
            {
                if (isDown)
                {
                    // Intervening key pressed while Ctrl is down or between taps (e.g. Ctrl+C, Ctrl+V)
                    _tapCount = 0;
                    _isSecondPressCandidate = false;
                    _interveningKeyPressed = true;
                    if (_isHolding)
                    {
                        _isHolding = false;
                        return ControlKeyTransition.HoldEnded;
                    }
                }
                return ControlKeyTransition.None;
            }

            if (isDown)
            {
                if (_controlDown)
                {
                    // Key repeat event from Windows; ignore
                    return ControlKeyTransition.None;
                }

                _controlDown = true;
                _interveningKeyPressed = false;
                _ctrlDownTime = now;

                // Check if this press follows a previous tap within the allowed gap
                if (_tapCount == 1 && (now - _lastTapUpTime <= MaxInterTapGapMs))
                {
                    _isSecondPressCandidate = true;
                }
                else
                {
                    _isSecondPressCandidate = false;
                    if (_tapCount > 0 && (now - _lastTapUpTime > MaxInterTapGapMs))
                    {
                        _tapCount = 0;
                    }
                }

                return ControlKeyTransition.None;
            }
            else
            {
                if (!_controlDown)
                {
                    return ControlKeyTransition.None;
                }

                _controlDown = false;
                var downDuration = now - _ctrlDownTime;
                _isSecondPressCandidate = false;

                if (_isHolding)
                {
                    _isHolding = false;
                    _tapCount = 0;
                    return ControlKeyTransition.HoldEnded;
                }

                // If an intervening key was pressed while Ctrl was held, this release
                // does not count as a tap (it was part of a shortcut like Ctrl+C or Ctrl+V).
                if (_interveningKeyPressed)
                {
                    _tapCount = 0;
                    return ControlKeyTransition.None;
                }

                // If held longer than tap threshold without entering hold mode, it does not count as a tap
                if (downDuration > TapMaxDurationMs)
                {
                    _tapCount = 0;
                    return ControlKeyTransition.None;
                }

                // Tap released within tap duration
                if (_tapCount == 0)
                {
                    _tapCount = 1;
                    _firstTapDownTime = _ctrlDownTime;
                    _lastTapUpTime = now;
                }
                else
                {
                    var interTapGap = now - _lastTapUpTime;
                    var totalWindow = now - _firstTapDownTime;

                    if (interTapGap <= MaxInterTapGapMs && totalWindow <= MaxTripleTapTotalWindowMs)
                    {
                        _tapCount++;
                        _lastTapUpTime = now;

                        if (_tapCount == 3)
                        {
                            _tapCount = 0;
                            return ControlKeyTransition.TripleTap;
                        }
                    }
                    else
                    {
                        // Too slow, restart tap sequence
                        _tapCount = 1;
                        _firstTapDownTime = _ctrlDownTime;
                        _lastTapUpTime = now;
                    }
                }

                return ControlKeyTransition.None;
            }
        }
    }

    /// <summary>
    /// Checks if a continuous hold threshold has elapsed on the second press (tap-then-hold).
    /// Returns true if dictation hold just started.
    /// </summary>
    public bool CheckHold(long? timestampMs = null)
    {
        var now = timestampMs ?? Environment.TickCount64;
        lock (_gate)
        {
            if (_controlDown
                && _isSecondPressCandidate
                && !_interveningKeyPressed
                && !_isHolding
                && (now - _ctrlDownTime >= HoldThresholdMs))
            {
                _isHolding = true;
                _tapCount = 0;
                _isSecondPressCandidate = false;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Cancels hold state without firing a release transition (e.g. if the focused control is not editable).
    /// </summary>
    public void CancelHold()
    {
        lock (_gate)
        {
            _isHolding = false;
            _tapCount = 0;
            _isSecondPressCandidate = false;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _controlDown = false;
            _interveningKeyPressed = false;
            _isHolding = false;
            _isSecondPressCandidate = false;
            _tapCount = 0;
            _ctrlDownTime = 0;
            _firstTapDownTime = 0;
            _lastTapUpTime = 0;
        }
    }
}
