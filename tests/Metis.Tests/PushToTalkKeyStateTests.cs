using Metis.Windows;

namespace Metis.Tests;

public sealed class PushToTalkKeyStateTests
{
    [Fact]
    public void Triple_tap_ctrl_triggers_live_listening()
    {
        var tracker = new ControlKeyTracker();

        // Tap 1
        Assert.Equal(ControlKeyTransition.None, tracker.Update(ControlKeyTracker.LeftControl, true, 100));
        Assert.Equal(ControlKeyTransition.None, tracker.Update(ControlKeyTracker.LeftControl, false, 150));
        Assert.Equal(1, tracker.CurrentTapCount);

        // Tap 2
        Assert.Equal(ControlKeyTransition.None, tracker.Update(ControlKeyTracker.LeftControl, true, 250));
        Assert.Equal(ControlKeyTransition.None, tracker.Update(ControlKeyTracker.LeftControl, false, 300));
        Assert.Equal(2, tracker.CurrentTapCount);

        // Tap 3
        Assert.Equal(ControlKeyTransition.None, tracker.Update(ControlKeyTracker.LeftControl, true, 400));
        Assert.Equal(ControlKeyTransition.TripleTap, tracker.Update(ControlKeyTracker.LeftControl, false, 450));
        Assert.Equal(0, tracker.CurrentTapCount);
    }

    [Fact]
    public void Intervening_key_cancels_triple_tap()
    {
        var tracker = new ControlKeyTracker();

        // Tap 1
        tracker.Update(ControlKeyTracker.LeftControl, true, 100);
        tracker.Update(ControlKeyTracker.LeftControl, false, 150);
        Assert.Equal(1, tracker.CurrentTapCount);

        // Intervening key 'C' (0x43) pressed
        tracker.Update(0x43, true, 200);
        Assert.Equal(0, tracker.CurrentTapCount);

        // Tap 2
        tracker.Update(ControlKeyTracker.LeftControl, true, 250);
        tracker.Update(ControlKeyTracker.LeftControl, false, 300);
        Assert.Equal(1, tracker.CurrentTapCount); // Restarted at 1

        // Tap 3
        var transition = tracker.Update(ControlKeyTracker.LeftControl, true, 400);
        var transitionUp = tracker.Update(ControlKeyTracker.LeftControl, false, 450);
        Assert.Equal(ControlKeyTransition.None, transition);
        Assert.Equal(ControlKeyTransition.None, transitionUp);
        Assert.Equal(2, tracker.CurrentTapCount);
    }

    [Fact]
    public void Single_hold_ctrl_does_not_trigger_dictation()
    {
        var tracker = new ControlKeyTracker();

        // User presses and holds Ctrl once (e.g. while selecting text or preparing to copy/paste)
        tracker.Update(ControlKeyTracker.LeftControl, true, 100);
        Assert.True(tracker.IsControlDown);
        Assert.False(tracker.IsHolding);
        Assert.False(tracker.IsSecondPressCandidate);

        // Even after holding well beyond hold threshold, it does NOT trigger dictation
        Assert.False(tracker.CheckHold(500));
        Assert.False(tracker.IsHolding);

        // Releasing Ctrl ends without firing HoldEnded
        var transition = tracker.Update(ControlKeyTracker.LeftControl, false, 700);
        Assert.Equal(ControlKeyTransition.None, transition);
        Assert.False(tracker.IsHolding);
        Assert.False(tracker.IsControlDown);
    }

    [Fact]
    public void Tap_then_hold_ctrl_triggers_dictation_and_ends_on_release()
    {
        var tracker = new ControlKeyTracker();

        // Tap 1: Press and release Ctrl quickly
        tracker.Update(ControlKeyTracker.LeftControl, true, 100);
        tracker.Update(ControlKeyTracker.LeftControl, false, 150);
        Assert.Equal(1, tracker.CurrentTapCount);

        // Second press: Press Ctrl again within gap window and hold
        tracker.Update(ControlKeyTracker.LeftControl, true, 250);
        Assert.True(tracker.IsControlDown);
        Assert.True(tracker.IsSecondPressCandidate);
        Assert.False(tracker.IsHolding);

        // At 350ms (100ms hold) - not yet threshold (300ms)
        Assert.False(tracker.CheckHold(350));
        Assert.False(tracker.IsHolding);

        // At 600ms (350ms hold >= 300ms threshold) - triggers dictation
        Assert.True(tracker.CheckHold(600));
        Assert.True(tracker.IsHolding);

        // Releasing Ctrl ends dictation hold
        var transition = tracker.Update(ControlKeyTracker.LeftControl, false, 800);
        Assert.Equal(ControlKeyTransition.HoldEnded, transition);
        Assert.False(tracker.IsHolding);
        Assert.False(tracker.IsControlDown);
    }

    [Fact]
    public void Ctrl_c_shortcut_cancels_hold_and_does_not_leave_stray_tap()
    {
        var tracker = new ControlKeyTracker();

        // Ctrl+C shortcut sequence:
        tracker.Update(ControlKeyTracker.LeftControl, true, 100);
        tracker.Update(0x43, true, 150); // 'C' key pressed

        // At 500ms, CheckHold should return false
        Assert.False(tracker.CheckHold(500));
        Assert.False(tracker.IsHolding);

        // Release 'C' then release Ctrl
        tracker.Update(0x43, false, 200);
        var transition = tracker.Update(ControlKeyTracker.LeftControl, false, 250);
        Assert.Equal(ControlKeyTransition.None, transition);

        // Crucial: Releasing Ctrl from Ctrl+C must NOT count as a tap
        Assert.Equal(0, tracker.CurrentTapCount);
        Assert.False(tracker.IsSecondPressCandidate);
    }

    [Fact]
    public void Intervening_key_cancels_tap_sequence_before_hold()
    {
        var tracker = new ControlKeyTracker();

        // Tap 1
        tracker.Update(ControlKeyTracker.LeftControl, true, 100);
        tracker.Update(ControlKeyTracker.LeftControl, false, 150);
        Assert.Equal(1, tracker.CurrentTapCount);

        // User types something before pressing Ctrl again
        tracker.Update(0x41, true, 200); // 'A' pressed
        Assert.Equal(0, tracker.CurrentTapCount);

        // Now user presses Ctrl and holds
        tracker.Update(ControlKeyTracker.LeftControl, true, 300);
        Assert.False(tracker.IsSecondPressCandidate);

        // CheckHold at 700ms (400ms hold) must NOT fire because previous tap was invalidated
        Assert.False(tracker.CheckHold(700));
        Assert.False(tracker.IsHolding);
    }

    [Fact]
    public void Slow_second_press_resets_tap_count_and_does_not_trigger_dictation()
    {
        var tracker = new ControlKeyTracker();

        // Tap 1
        tracker.Update(ControlKeyTracker.LeftControl, true, 100);
        tracker.Update(ControlKeyTracker.LeftControl, false, 150);
        Assert.Equal(1, tracker.CurrentTapCount);

        // More than MaxInterTapGapMs (500ms) passes before second press: 150 + 550 = 700ms
        tracker.Update(ControlKeyTracker.LeftControl, true, 700);
        Assert.False(tracker.IsSecondPressCandidate);
        Assert.Equal(0, tracker.CurrentTapCount);

        // Even if held for 500ms, does not trigger dictation
        Assert.False(tracker.CheckHold(1200));
        Assert.False(tracker.IsHolding);
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

    [Fact]
    public void Direct_agent_voice_fires_on_ctrl_shift_a()
    {
        var state = new DirectAgentVoiceKeyState();

        Assert.Equal(PushToTalkTransition.None, state.Update(DirectAgentVoiceKeyState.LeftControl, true));
        Assert.Equal(PushToTalkTransition.None, state.Update(DirectAgentVoiceKeyState.LeftShift, true));
        Assert.Equal(PushToTalkTransition.Pressed, state.Update(DirectAgentVoiceKeyState.KeyA, true));
        Assert.True(state.IsActive);
        Assert.Equal(PushToTalkTransition.None, state.Update(DirectAgentVoiceKeyState.KeyA, true));
        Assert.Equal(PushToTalkTransition.Released, state.Update(DirectAgentVoiceKeyState.KeyA, false));
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Direct_agent_voice_fires_on_ctrl_alt_a()
    {
        var state = new DirectAgentVoiceKeyState();

        Assert.Equal(PushToTalkTransition.None, state.Update(DirectAgentVoiceKeyState.RightControl, true));
        Assert.Equal(PushToTalkTransition.None, state.Update(DirectAgentVoiceKeyState.LeftAlt, true));
        Assert.Equal(PushToTalkTransition.Pressed, state.Update(DirectAgentVoiceKeyState.KeyA, true));
        Assert.True(state.IsActive);
        Assert.Equal(PushToTalkTransition.None, state.Update(DirectAgentVoiceKeyState.KeyA, true));
        Assert.Equal(PushToTalkTransition.Released, state.Update(DirectAgentVoiceKeyState.LeftAlt, false));
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Direct_agent_voice_resets_cleanly()
    {
        var state = new DirectAgentVoiceKeyState();
        state.Update(DirectAgentVoiceKeyState.LeftControl, true);
        state.Update(DirectAgentVoiceKeyState.LeftShift, true);
        state.Update(DirectAgentVoiceKeyState.KeyA, true);
        Assert.True(state.IsActive);

        state.Reset();
        Assert.False(state.IsActive);
    }
}
