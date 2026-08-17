using Metis.Windows;

namespace Metis.Tests;

public sealed class ContextActivationKeyStateTests
{
    private const uint Control = ContextActivationKeyState.LeftControl;
    private const uint Alt = ContextActivationKeyState.LeftAlt;
    private const uint Shift = ContextActivationKeyState.LeftShift;

    [Fact]
    public void Ctrl_then_alt_starts_a_context_activation()
    {
        var state = new ContextActivationKeyState();

        Assert.Equal(ContextActivationTransition.None, state.Update(Control, true));
        Assert.Equal(ContextActivationTransition.Pressed, state.Update(Alt, true));
        Assert.True(state.IsActive);
        Assert.False(state.ShiftSeen);
    }

    [Fact]
    public void Releasing_either_modifier_ends_the_activation()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Alt, true);

        Assert.Equal(ContextActivationTransition.Released, state.Update(Alt, false));
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Holding_shift_from_the_start_selects_inspect()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Shift, true);

        Assert.Equal(ContextActivationTransition.Pressed, state.Update(Alt, true));
        Assert.True(state.ShiftSeen);
    }

    [Fact]
    public void Adding_shift_during_the_hold_upgrades_the_activation_to_inspect()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Alt, true);
        Assert.False(state.ShiftSeen);

        // The upgrade must be announced, not just recorded: whether the chord
        // arms tracing or the microphone otherwise depends on which of three
        // keys the user happened to press a few milliseconds first.
        Assert.Equal(ContextActivationTransition.UpgradedToInspect, state.Update(Shift, true));
        Assert.True(state.ShiftSeen);
    }

    [Fact]
    public void Every_order_of_the_three_keys_ends_as_an_inspect()
    {
        uint[][] orders =
        [
            [Control, Alt, Shift],
            [Control, Shift, Alt],
            [Shift, Control, Alt],
            [Alt, Control, Shift],
            [Shift, Alt, Control],
            [Alt, Shift, Control]
        ];

        foreach (var order in orders)
        {
            var state = new ContextActivationKeyState();
            var upgraded = false;

            foreach (var key in order)
            {
                if (state.Update(key, true) == ContextActivationTransition.UpgradedToInspect)
                {
                    upgraded = true;
                }
            }

            Assert.True(
                state.ShiftSeen,
                $"pressing {string.Join(", ", order.Select(NameOf))} did not register as inspect");

            // Shift landing last is the case that used to arm the microphone
            // instead of the pen, and it is the only order that needs the
            // upgrade signal to reach that conclusion.
            if (order[^1] == Shift)
            {
                Assert.True(upgraded, "a late Shift must announce the upgrade");
            }
        }
    }

    [Fact]
    public void The_upgrade_is_announced_only_once_per_hold()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Alt, true);

        Assert.Equal(ContextActivationTransition.UpgradedToInspect, state.Update(Shift, true));
        Assert.Equal(ContextActivationTransition.None, state.Update(Shift, true));
    }

    private static string NameOf(uint key) => key switch
    {
        Control => "Ctrl",
        Alt => "Alt",
        Shift => "Shift",
        _ => key.ToString()
    };

    [Fact]
    public void Releasing_shift_before_the_chord_keeps_the_inspect_choice()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Alt, true);
        state.Update(Shift, true);
        state.Update(Shift, false);

        Assert.Equal(ContextActivationTransition.Released, state.Update(Control, false));
        Assert.True(state.ShiftSeen);
    }

    [Fact]
    public void A_new_activation_starts_without_the_previous_shift_choice()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Alt, true);
        state.Update(Shift, true);
        state.Update(Shift, false);
        state.Update(Alt, false);
        state.Update(Control, false);

        state.Update(Control, true);
        Assert.Equal(ContextActivationTransition.Pressed, state.Update(Alt, true));
        Assert.False(state.ShiftSeen);
    }

    [Fact]
    public void Unrelated_keys_never_start_an_activation()
    {
        var state = new ContextActivationKeyState();

        Assert.Equal(ContextActivationTransition.None, state.Update(0x41, true));
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Reset_clears_an_in_flight_activation()
    {
        var state = new ContextActivationKeyState();
        state.Update(Control, true);
        state.Update(Alt, true);

        state.Reset();

        Assert.False(state.IsActive);
        Assert.False(state.ShiftSeen);
    }
}
