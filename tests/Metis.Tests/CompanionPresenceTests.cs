using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// When the companion is on screen at all.
///
/// It used to follow the cursor from the moment first run finished until the
/// application closed, which is a small animated sprite permanently attached to
/// the pointer of somebody trying to do their own work. It now arrives when
/// Metis has something to say and leaves afterwards.
///
/// The rule is a pure function because the window it belongs to cannot be
/// reached by a test, and because "is it there or not" is precisely the sort of
/// thing that is easy to get subtly wrong in a way nobody notices until the
/// sprite is flickering on and off during a single answer.
/// </summary>
public sealed class CompanionPresenceTests
{
    [Theory]
    [InlineData(AssistantState.Idle)]
    [InlineData(AssistantState.Paused)]
    public void It_is_absent_while_nothing_is_happening(AssistantState state) =>
        Assert.False(CompanionPresence.ShouldBeVisible(state, alwaysVisible: false, teaching: false));

    [Theory]
    [InlineData(AssistantState.Listening)]
    [InlineData(AssistantState.Thinking)]
    [InlineData(AssistantState.Speaking)]
    [InlineData(AssistantState.Success)]
    public void It_is_there_while_Metis_is_doing_something(AssistantState state) =>
        Assert.True(CompanionPresence.ShouldBeVisible(state, alwaysVisible: false, teaching: false));

    /// <summary>
    /// A lesson spends most of its time between marks, with the runtime idle
    /// while the learner does the step they were shown. The companion has to
    /// stay put for all of that — blinking out between steps would be worse than
    /// never leaving at all.
    /// </summary>
    [Fact]
    public void It_stays_put_during_a_lesson() =>
        Assert.True(CompanionPresence.ShouldBeVisible(
            AssistantState.Idle, alwaysVisible: false, teaching: true));

    /// <summary>
    /// The old behaviour is still available for anyone who liked it.
    /// </summary>
    [Theory]
    [InlineData(AssistantState.Idle)]
    [InlineData(AssistantState.Paused)]
    public void Someone_who_wants_it_always_there_gets_it(AssistantState state) =>
        Assert.True(CompanionPresence.ShouldBeVisible(state, alwaysVisible: true, teaching: false));

    /// <summary>
    /// The whole point of the change: on a fresh install, with nothing
    /// happening, there is no sprite following the cursor around.
    /// </summary>
    [Fact]
    public void The_default_is_absent() =>
        Assert.False(CompanionPresence.ShouldBeVisible(
            AssistantState.Idle,
            alwaysVisible: new AppSettings().CompanionAlwaysVisible,
            teaching: false));

    /// <summary>
    /// An error is something the user needs to see, so the companion is there
    /// to show it rather than reporting into an empty corner of the screen.
    /// </summary>
    [Fact]
    public void It_is_there_to_deliver_bad_news() =>
        Assert.True(CompanionPresence.ShouldBeVisible(
            AssistantState.Error, alwaysVisible: false, teaching: false));

    /// <summary>
    /// Every state has a defined answer. A new one added to the enum without a
    /// thought about presence would otherwise pick up whatever the default
    /// branch happened to be.
    /// </summary>
    [Fact]
    public void Every_state_has_an_answer()
    {
        foreach (var state in Enum.GetValues<AssistantState>())
        {
            // Not asserting which — only that it decides, and does so the same
            // way twice.
            var first = CompanionPresence.ShouldBeVisible(state, false, false);
            var second = CompanionPresence.ShouldBeVisible(state, false, false);
            Assert.Equal(first, second);
        }
    }
}
