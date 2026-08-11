using Metis.Windows;

namespace Metis.Tests;

public sealed class PushToTalkKeyStateTests
{
    [Fact]
    public void Combination_fires_once_and_releases_when_any_required_key_is_released()
    {
        var state = new PushToTalkKeyState();

        Assert.Equal(PushToTalkTransition.None, state.Update(PushToTalkKeyState.LeftControl, true));
        Assert.Equal(PushToTalkTransition.None, state.Update(PushToTalkKeyState.LeftShift, true));
        Assert.Equal(PushToTalkTransition.Pressed, state.Update(PushToTalkKeyState.Digit1, true));
        Assert.Equal(PushToTalkTransition.None, state.Update(PushToTalkKeyState.Digit1, true));
        Assert.Equal(PushToTalkTransition.Released, state.Update(PushToTalkKeyState.LeftShift, false));
    }

    [Fact]
    public void Releasing_one_control_keeps_combination_active_when_other_control_is_still_down()
    {
        var state = new PushToTalkKeyState();
        state.Update(PushToTalkKeyState.LeftControl, true);
        state.Update(PushToTalkKeyState.RightControl, true);
        state.Update(PushToTalkKeyState.LeftShift, true);
        state.Update(PushToTalkKeyState.Digit1, true);

        Assert.Equal(PushToTalkTransition.None, state.Update(PushToTalkKeyState.LeftControl, false));
        Assert.True(state.IsActive);
        Assert.Equal(PushToTalkTransition.Released, state.Update(PushToTalkKeyState.RightControl, false));
    }

    [Fact]
    public void F12_emergency_stop_fires_once_until_key_is_released()
    {
        var state = new EmergencyStopKeyState();

        Assert.True(state.Update(EmergencyStopKeyState.F12, true));
        Assert.False(state.Update(EmergencyStopKeyState.F12, true));
        Assert.False(state.Update(EmergencyStopKeyState.F12, false));
        Assert.True(state.Update(EmergencyStopKeyState.F12, true));
    }
}
