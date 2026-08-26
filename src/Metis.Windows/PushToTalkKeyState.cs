namespace Metis.Windows;

public enum PushToTalkTransition
{
    None,
    Pressed,
    Released
}

public sealed class PushToTalkKeyState
{
    public const uint LeftControl = 0xA2;
    public const uint RightControl = 0xA3;
    public const uint LeftShift = 0xA0;
    public const uint RightShift = 0xA1;
    public const uint Digit1 = 0x31;

    private readonly HashSet<uint> _keysDown = [];

    public bool IsActive { get; private set; }

    public PushToTalkTransition Update(uint virtualKey, bool isDown)
    {
        if (!IsRelevant(virtualKey))
        {
            return PushToTalkTransition.None;
        }

        if (isDown)
        {
            _keysDown.Add(virtualKey);
        }
        else
        {
            _keysDown.Remove(virtualKey);
        }

        var control = _keysDown.Contains(LeftControl) || _keysDown.Contains(RightControl);
        var shift = _keysDown.Contains(LeftShift) || _keysDown.Contains(RightShift);
        var combination = control && shift && _keysDown.Contains(Digit1);

        if (combination && !IsActive)
        {
            IsActive = true;
            return PushToTalkTransition.Pressed;
        }

        if (!combination && IsActive)
        {
            IsActive = false;
            return PushToTalkTransition.Released;
        }

        return PushToTalkTransition.None;
    }

    public void Reset()
    {
        _keysDown.Clear();
        IsActive = false;
    }

    private static bool IsRelevant(uint virtualKey) =>
        virtualKey is LeftControl or RightControl or LeftShift or RightShift or Digit1;
}

/// <summary>
/// Tracks dedicated Direct Agent Voice shortcut chords: Ctrl+Shift+A and Ctrl+Alt+A.
/// Directly records voice and dispatches an autonomous agent goal to the AgentTaskManager.
/// </summary>
public sealed class DirectAgentVoiceKeyState
{
    public const uint LeftControl = 0xA2;
    public const uint RightControl = 0xA3;
    public const uint LeftShift = 0xA0;
    public const uint RightShift = 0xA1;
    public const uint LeftAlt = 0xA4;
    public const uint RightAlt = 0xA5;
    public const uint KeyA = 0x41;

    private readonly HashSet<uint> _keysDown = [];

    public bool IsActive { get; private set; }

    public PushToTalkTransition Update(uint virtualKey, bool isDown)
    {
        if (!IsRelevant(virtualKey))
        {
            return PushToTalkTransition.None;
        }

        if (isDown)
        {
            _keysDown.Add(virtualKey);
        }
        else
        {
            _keysDown.Remove(virtualKey);
        }

        var control = _keysDown.Contains(LeftControl) || _keysDown.Contains(RightControl);
        var shift = _keysDown.Contains(LeftShift) || _keysDown.Contains(RightShift);
        var alt = _keysDown.Contains(LeftAlt) || _keysDown.Contains(RightAlt);
        var hasA = _keysDown.Contains(KeyA);

        // Triggers on Ctrl+Shift+A or Ctrl+Alt+A
        var combination = (control && shift && hasA) || (control && alt && hasA);

        if (combination && !IsActive)
        {
            IsActive = true;
            return PushToTalkTransition.Pressed;
        }

        if (!combination && IsActive)
        {
            IsActive = false;
            return PushToTalkTransition.Released;
        }

        return PushToTalkTransition.None;
    }

    public void Reset()
    {
        _keysDown.Clear();
        IsActive = false;
    }

    private static bool IsRelevant(uint virtualKey) =>
        virtualKey is LeftControl or RightControl or LeftShift or RightShift or LeftAlt or RightAlt or KeyA;
}

/// <summary>
/// Tracks the Ctrl+Alt context chord and the Ctrl+Alt+Shift inspect chord.
/// Activation begins when Ctrl+Alt goes down without the hold-to-talk digit;
/// Shift at any point during the hold upgrades the activation to Inspect, so
/// the user can add precision after starting to speak.
/// </summary>
public enum ContextActivationTransition
{
    None,
    Pressed,

    /// <summary>
    /// Shift arrived after the hold had already started, turning a context
    /// activation into an inspect one. Without this the classification depended
    /// on whether Shift happened to land before or after Ctrl+Alt completed,
    /// which made the same three keys behave differently run to run.
    /// </summary>
    UpgradedToInspect,
    Released
}

public sealed class ContextActivationKeyState
{
    public const uint LeftControl = 0xA2;
    public const uint RightControl = 0xA3;
    public const uint LeftShift = 0xA0;
    public const uint RightShift = 0xA1;
    public const uint LeftAlt = 0xA4;
    public const uint RightAlt = 0xA5;

    private readonly HashSet<uint> _keysDown = [];

    public bool IsActive { get; private set; }

    public bool ShiftSeen { get; private set; }

    public ContextActivationTransition Update(uint virtualKey, bool isDown)
    {
        if (!IsRelevant(virtualKey))
        {
            return ContextActivationTransition.None;
        }

        if (isDown)
        {
            _keysDown.Add(virtualKey);
        }
        else
        {
            _keysDown.Remove(virtualKey);
        }

        var control = _keysDown.Contains(LeftControl) || _keysDown.Contains(RightControl);
        var alt = _keysDown.Contains(LeftAlt) || _keysDown.Contains(RightAlt);
        var shift = _keysDown.Contains(LeftShift) || _keysDown.Contains(RightShift);
        var combination = control && alt;

        if (combination && IsActive && shift && !ShiftSeen)
        {
            ShiftSeen = true;
            return ContextActivationTransition.UpgradedToInspect;
        }

        if (combination && !IsActive)
        {
            IsActive = true;
            ShiftSeen = shift;
            return ContextActivationTransition.Pressed;
        }

        if (!combination && IsActive)
        {
            IsActive = false;
            return ContextActivationTransition.Released;
        }

        return ContextActivationTransition.None;
    }

    public void Reset()
    {
        _keysDown.Clear();
        IsActive = false;
        ShiftSeen = false;
    }

    private static bool IsRelevant(uint virtualKey) =>
        virtualKey is LeftControl or RightControl or LeftShift or RightShift or LeftAlt or RightAlt;
}

/// <summary>
/// Ctrl+Space, which turns continuous listening on and off.
///
/// A toggle rather than a hold, because the whole point is to stop holding
/// something. It fires once when Space goes down with Ctrl already held, and
/// will not fire again until Space is released — otherwise the key repeat
/// Windows sends while a key is held would switch listening on and off dozens
/// of times a second.
/// </summary>
public sealed class ActiveListeningKeyState
{
    public const uint LeftControl = 0xA2;
    public const uint RightControl = 0xA3;
    public const uint Space = 0x20;

    private bool _firedForThisPress;

    /// <summary>
    /// True when the chord should toggle listening.
    ///
    /// <paramref name="controlHeld"/> is asked of Windows at the moment Space
    /// arrives rather than accumulated from earlier hook callbacks. Tracking it
    /// here looked equivalent and was not: a hook misses key-ups whenever
    /// another window takes focus mid-chord or input is injected, and a Ctrl
    /// that is believed held forever turns every subsequent space — every space
    /// in an ordinary sentence — into a toggle. In testing that switched
    /// listening on and off four times from one keypress.
    /// </summary>
    public bool Update(uint virtualKey, bool isDown, bool controlHeld)
    {
        if (virtualKey != Space)
        {
            return false;
        }

        if (!isDown)
        {
            _firedForThisPress = false;
            return false;
        }

        if (!controlHeld || _firedForThisPress)
        {
            return false;
        }

        _firedForThisPress = true;
        return true;
    }

    public void Reset() => _firedForThisPress = false;
}

public sealed class EmergencyStopKeyState
{
    public const uint F12 = 0x7B;
    private bool _isDown;

    public bool Update(uint virtualKey, bool isDown)
    {
        if (virtualKey != F12)
        {
            return false;
        }

        var pressed = isDown && !_isDown;
        _isDown = isDown;
        return pressed;
    }

    public void Reset() => _isDown = false;
}
