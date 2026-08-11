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
/// Tracks the Ctrl+Alt context chord and the Ctrl+Alt+Shift inspect chord.
/// Activation begins when Ctrl+Alt goes down without the hold-to-talk digit;
/// Shift at any point during the hold upgrades the activation to Inspect, so
/// the user can add precision after starting to speak.
/// </summary>
public enum ContextActivationTransition
{
    None,
    Pressed,
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

        if (combination && IsActive && shift)
        {
            ShiftSeen = true;
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
