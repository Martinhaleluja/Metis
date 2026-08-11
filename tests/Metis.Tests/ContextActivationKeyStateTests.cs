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

        state.Update(Shift, true);

        Assert.True(state.ShiftSeen);
    }

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
