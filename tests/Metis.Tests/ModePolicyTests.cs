using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class ModePolicyTests
{
    [Theory]
    [InlineData(OperatingMode.Learn)]
    [InlineData(OperatingMode.Guide)]
    [InlineData(OperatingMode.Assist)]
    [InlineData(OperatingMode.Autopilot)]
    public void Pointing_at_a_control_is_allowed_in_every_mode(OperatingMode mode)
    {
        Assert.True(ModePolicy.Allows(mode, DesktopActionKind.MovePointer, userAskedForAction: false));
        Assert.True(ModePolicy.Allows(mode, DesktopActionKind.Observe, userAskedForAction: false));
    }

    [Fact]
    public void Learn_mode_never_performs_a_computer_action_even_when_asked()
    {
        Assert.False(ModePolicy.Allows(OperatingMode.Learn, DesktopActionKind.LeftClick, userAskedForAction: true));
        Assert.False(ModePolicy.Allows(OperatingMode.Learn, DesktopActionKind.TypeText, userAskedForAction: true));
    }

    [Fact]
    public void Guide_mode_acts_only_when_the_user_asked_in_this_request()
    {
        Assert.True(ModePolicy.Allows(OperatingMode.Guide, DesktopActionKind.LeftClick, userAskedForAction: true));
        Assert.False(ModePolicy.Allows(OperatingMode.Guide, DesktopActionKind.LeftClick, userAskedForAction: false));
    }

    [Theory]
    [InlineData(OperatingMode.Assist)]
    [InlineData(OperatingMode.Autopilot)]
    public void Assist_and_autopilot_may_act_without_an_explicit_request(OperatingMode mode)
    {
        Assert.True(ModePolicy.Allows(mode, DesktopActionKind.LeftClick, userAskedForAction: false));
    }

    [Fact]
    public void Filtering_keeps_pointer_steps_and_drops_the_clicks_learn_mode_forbids()
    {
        DesktopAction[] actions =
        [
            new(DesktopActionKind.MovePointer, 100, 100, Id: "a"),
            new(DesktopActionKind.LeftClick, 100, 100, Id: "b"),
            new(DesktopActionKind.TypeText, HasCoordinates: false, Text: "hello", Id: "c")
        ];

        var filtered = ModePolicy.Filter(OperatingMode.Learn, actions, userAskedForAction: true, out var withheld);

        Assert.Equal(2, withheld);
        Assert.Equal([DesktopActionKind.MovePointer], filtered.Select(action => action.Kind));
    }

    [Fact]
    public void Filtering_trims_a_batch_to_the_mode_step_budget()
    {
        var actions = Enumerable
            .Range(0, 6)
            .Select(index => new DesktopAction(DesktopActionKind.LeftClick, 10, 10, Id: $"step-{index}"))
            .ToArray();

        var guided = ModePolicy.Filter(OperatingMode.Guide, actions, userAskedForAction: true, out _);
        var autopilot = ModePolicy.Filter(OperatingMode.Autopilot, actions, userAskedForAction: true, out _);

        Assert.Equal(ModePolicy.For(OperatingMode.Guide).MaxActionsPerBatch, guided.Count);
        Assert.Equal(6, autopilot.Count);
    }

    [Theory]
    [InlineData("learn", OperatingMode.Learn)]
    [InlineData("ASSIST", OperatingMode.Assist)]
    [InlineData("autopilot", OperatingMode.Autopilot)]
    [InlineData("", OperatingMode.Guide)]
    [InlineData(null, OperatingMode.Guide)]
    [InlineData("nonsense", OperatingMode.Guide)]
    public void Unknown_mode_names_fall_back_to_guide(string? value, OperatingMode expected) =>
        Assert.Equal(expected, ModePolicy.Parse(value));

    [Fact]
    public void Every_mode_instruction_names_its_own_mode()
    {
        foreach (var capabilities in ModePolicy.All)
        {
            var instruction = ModePolicy.BuildInstruction(capabilities.Mode);
            Assert.Contains(
                capabilities.DisplayName.ToUpperInvariant(),
                instruction,
                StringComparison.Ordinal);
        }
    }
}
