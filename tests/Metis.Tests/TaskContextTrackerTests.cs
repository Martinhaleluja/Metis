using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class TaskContextTrackerTests
{
    [Fact]
    public void A_new_request_starts_a_new_task()
    {
        var tracker = new TaskContextTracker();

        var first = tracker.BeginTurn("Help me export a WAV", "FL Studio", OperatingMode.Guide);
        var second = tracker.BeginTurn("Open my email", "Outlook", OperatingMode.Guide);

        Assert.NotEqual(first.TaskId, second.TaskId);
        Assert.Equal("Open my email", second.OriginalUserGoal);
    }

    [Fact]
    public void A_continuation_keeps_the_original_goal_across_applications()
    {
        var tracker = new TaskContextTracker();
        var first = tracker.BeginTurn("Help me make a YouTube video", "Chrome", OperatingMode.Guide);

        var second = tracker.BeginTurn("now open the editor", "DaVinci Resolve", OperatingMode.Guide);

        Assert.Equal(first.TaskId, second.TaskId);
        Assert.Equal("Help me make a YouTube video", second.OriginalUserGoal);
        Assert.Equal("DaVinci Resolve", second.CurrentApplication);
        Assert.Equal(1, second.CurrentStep);
    }

    [Fact]
    public void A_stale_task_does_not_absorb_a_later_continuation()
    {
        var tracker = new TaskContextTracker(TimeSpan.Zero);
        var first = tracker.BeginTurn("Help me make a YouTube video", "Chrome", OperatingMode.Guide);

        var second = tracker.BeginTurn("continue", "Chrome", OperatingMode.Guide);

        Assert.NotEqual(first.TaskId, second.TaskId);
    }

    [Fact]
    public void Progress_is_summarised_for_the_next_request()
    {
        var tracker = new TaskContextTracker();
        tracker.BeginTurn("Export a WAV", "FL Studio", OperatingMode.Guide);
        tracker.RecordProgress("opened the File menu", "the export dialog is visible");

        var description = tracker.Describe();

        Assert.NotNull(description);
        Assert.Contains("Export a WAV", description, StringComparison.Ordinal);
        Assert.Contains("opened the File menu", description, StringComparison.Ordinal);
        Assert.Contains("the export dialog is visible", description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_first_request_carries_no_prior_context() =>
        Assert.Null(new TaskContextTracker().Describe());

    [Fact]
    public void Completing_a_task_clears_it()
    {
        var tracker = new TaskContextTracker();
        tracker.BeginTurn("Export a WAV", "FL Studio", OperatingMode.Guide);
        tracker.RecordProgress("clicked Export", null);

        tracker.Complete();

        Assert.Null(tracker.Current);
        Assert.Null(tracker.Describe());
    }

    [Fact]
    public void Only_the_most_recent_actions_are_retained()
    {
        var tracker = new TaskContextTracker();
        tracker.BeginTurn("Long task", "Explorer", OperatingMode.Guide);
        for (var step = 1; step <= 15; step++)
        {
            tracker.RecordProgress($"step {step}", null);
        }

        var actions = tracker.Current!.PreviousActions;

        Assert.Equal(10, actions.Count);
        Assert.Equal("step 15", actions[^1]);
    }
}
