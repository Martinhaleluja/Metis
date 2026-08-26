using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// This rule decides whether a walkthrough waits for the learner or marches on
/// without them, so both of its failure directions matter — and they are not
/// equally bad. Waiting forever on evidence that cannot arrive strands someone
/// mid-lesson; advancing early is exactly what Metis did before, which is
/// merely the old behaviour. So anything unreadable must come back Unknowable
/// rather than NotYet.
/// </summary>
public sealed class StepCompletionEvidenceTests
{
    private static LessonStep Step(string? doneWhen = null, string? element = null) =>
        new("Click the thing", DoneWhen: doneWhen, ElementName: element);

    private static StepProgress Read(
        LessonStep? step,
        LessonStep? next = null,
        bool nextVisibleNow = false,
        bool nextVisibleBefore = false,
        string? titleNow = "Notepad",
        string? titleBefore = "Notepad",
        string? screenText = null) =>
        StepCompletionEvidence.Read(
            step, next, nextVisibleNow, nextVisibleBefore, titleNow, titleBefore, screenText);

    [Fact]
    public void A_step_that_named_nothing_checkable_is_unknowable()
    {
        Assert.Equal(StepProgress.Unknowable, Read(Step()));
    }

    [Fact]
    public void A_null_step_is_unknowable_rather_than_a_crash()
    {
        Assert.Equal(StepProgress.Unknowable, Read(null));
    }

    [Fact]
    public void The_next_steps_target_appearing_is_proof_the_step_was_done()
    {
        // The dialog the next step acts on cannot be there unless this step
        // opened it. This is the strongest signal available locally.
        var progress = Read(
            Step(doneWhen: "The Save As dialog opens"),
            next: Step(element: "File name"),
            nextVisibleNow: true,
            nextVisibleBefore: false);

        Assert.Equal(StepProgress.Verified, progress);
    }

    [Fact]
    public void A_target_that_was_already_there_proves_nothing()
    {
        var progress = Read(
            Step(doneWhen: "The Save As dialog opens"),
            next: Step(element: "File name"),
            nextVisibleNow: true,
            nextVisibleBefore: true);

        Assert.NotEqual(StepProgress.Verified, progress);
    }

    [Fact]
    public void A_changed_window_counts_as_progress()
    {
        var progress = Read(
            Step(doneWhen: "The Settings window opens"),
            titleNow: "Settings",
            titleBefore: "Notepad");

        Assert.Equal(StepProgress.Verified, progress);
    }

    [Fact]
    public void Every_named_thing_must_be_on_screen_not_merely_one()
    {
        var half = Read(
            Step(doneWhen: "The Save As dialog opens"),
            screenText: """[{"Name":"Save"},{"Name":"Untitled"}]""");
        Assert.NotEqual(StepProgress.Verified, half);

        var whole = Read(
            Step(doneWhen: "The Save As dialog opens"),
            screenText: """[{"Name":"Save As"},{"Name":"File name"}]""");
        Assert.Equal(StepProgress.Verified, whole);
    }

    [Fact]
    public void A_readable_step_that_has_not_happened_is_not_yet()
    {
        var progress = Read(
            Step(doneWhen: "The Save As dialog opens"),
            screenText: """[{"Name":"Untitled - Notepad"}]""");

        Assert.Equal(StepProgress.NotYet, progress);
    }

    [Theory]
    [InlineData("The Save As dialog opens", new[] { "Save", "As" })]
    [InlineData("A new window appears", new string[0])]
    [InlineData("The Bold button is highlighted", new[] { "Bold" })]
    [InlineData("You see the file listed", new string[0])]
    public void Only_names_are_searched_for_never_ordinary_english(string doneWhen, string[] expected)
    {
        // Searching for "the", "window" or "opens" would match half of Windows
        // and verify a step nobody performed.
        Assert.Equal(expected, StepCompletionEvidence.SignificantWords(doneWhen));
    }

    [Fact]
    public void A_long_prose_phrase_is_treated_as_unsearchable()
    {
        // Requiring all of five names would essentially never match, and a
        // check that can only fail is worse than no check.
        var words = StepCompletionEvidence.SignificantWords(
            "The Save As Export Import Backup Restore screen appears");

        Assert.Empty(words);
    }

    [Fact]
    public void A_step_is_only_worth_waiting_on_when_something_was_named()
    {
        Assert.False(StepCompletionEvidence.CanBeChecked(Step(), null));
        Assert.True(StepCompletionEvidence.CanBeChecked(Step(doneWhen: "The Save As dialog opens"), null));
        Assert.True(StepCompletionEvidence.CanBeChecked(Step(), Step(element: "File name")));
    }

    [Fact]
    public void A_missing_window_title_does_not_read_as_a_change()
    {
        var progress = Read(
            Step(doneWhen: "The Save As dialog opens"),
            titleNow: null,
            titleBefore: "Notepad",
            screenText: """[{"Name":"Untitled"}]""");

        Assert.NotEqual(StepProgress.Verified, progress);
    }
}
