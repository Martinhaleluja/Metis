using Lulu.Core.Models;
using Lulu.Core.State;

namespace Lulu.Tests;

public sealed class AssistantStateMachineTests
{
    [Fact]
    public void Voice_turn_moves_through_expected_states()
    {
        var machine = new AssistantStateMachine();

        Assert.True(machine.TryTransition(AssistantState.Listening));
        Assert.True(machine.TryTransition(AssistantState.Thinking));
        Assert.True(machine.TryTransition(AssistantState.Speaking));
        Assert.True(machine.TryTransition(AssistantState.Success));
        Assert.True(machine.TryTransition(AssistantState.Idle));
        Assert.Equal(AssistantState.Idle, machine.Current);
    }

    [Fact]
    public void Invalid_transition_is_rejected()
    {
        var machine = new AssistantStateMachine();

        Assert.False(machine.TryTransition(AssistantState.Speaking));
        Assert.Equal(AssistantState.Idle, machine.Current);
    }

    [Fact]
    public void Error_can_be_entered_from_any_state_and_recovered()
    {
        var machine = new AssistantStateMachine();
        machine.TryTransition(AssistantState.Listening);

        Assert.True(machine.TryTransition(AssistantState.Error));
        Assert.True(machine.TryTransition(AssistantState.Idle));
    }
}
