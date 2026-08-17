using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class LessonStateTests
{
    private static readonly LessonStep[] ThreeSteps =
    [
        new("Open the File menu", "It holds the export commands", "The File menu is open", 100, 20, "File"),
        new("Choose Export", DoneWhen: "The export dialog is visible"),
        new("Pick WAV and confirm", DoneWhen: "A WAV file appears in the folder")
    ];

    [Fact]
    public void A_lesson_starts_on_its_first_step()
    {
        var lesson = new LessonState("Export a WAV", ThreeSteps);

        Assert.Equal(1, lesson.StepNumber);
        Assert.Equal("Open the File menu", lesson.Current!.Instruction);
        Assert.False(lesson.IsFinished);
    }

    [Fact]
    public void Advancing_walks_through_every_step_and_then_completes()
    {
        var lesson = new LessonState("Export a WAV", ThreeSteps);

        lesson = lesson.Advance().Advance();
        Assert.Equal(3, lesson.StepNumber);
        Assert.False(lesson.IsFinished);

        lesson = lesson.Advance();
        Assert.True(lesson.IsFinished);
        Assert.Equal(LessonStatus.Complete, lesson.Status);
        Assert.Null(lesson.Current);
    }

    [Fact]
    public void Retrying_counts_attempts_so_hints_can_escalate()
    {
        var lesson = new LessonState("Export a WAV", ThreeSteps).Retry().Retry();

        Assert.Equal(2, lesson.AttemptsOnCurrentStep);
        Assert.Equal(LessonStatus.Waiting, lesson.Status);
    }

    [Fact]
    public void Attempts_reset_when_the_learner_moves_on()
    {
        var lesson = new LessonState("Export a WAV", ThreeSteps).Retry().Retry().Advance();

        Assert.Equal(0, lesson.AttemptsOnCurrentStep);
        Assert.Equal(LessonStatus.Showing, lesson.Status);
    }

    [Fact]
    public void A_step_without_coordinates_still_teaches_but_marks_nothing()
    {
        Assert.False(ThreeSteps[1].HasTarget);
        Assert.True(ThreeSteps[0].HasTarget);
    }

    [Fact]
    public void An_empty_lesson_is_finished_immediately() =>
        Assert.True(new LessonState("Nothing to do", []).IsFinished);
}

public sealed class LessonStepParsingTests
{
    [Fact]
    public void Steps_are_read_from_the_model_response()
    {
        const string json = """
            {
              "screen_observed": true,
              "spoken_text": "Let's export your track.",
              "status": "continue",
              "goal": "Export a WAV",
              "actions": [],
              "steps": [
                { "instruction": "Open the File menu", "why": "It holds export", "done_when": "Menu is open",
                  "x": 120, "y": 35, "label": "File menu" },
                { "instruction": "Choose Export", "done_when": "Dialog appears" }
              ]
            }
            """;

        var plan = AssistantPlanParser.Parse(json, hasScreenshot: true, userRequest: "teach me to export");

        Assert.Equal(2, plan.LessonSteps.Count);
        Assert.Equal("Open the File menu", plan.LessonSteps[0].Instruction);
        Assert.Equal("Menu is open", plan.LessonSteps[0].DoneWhen);
        Assert.True(plan.LessonSteps[0].HasTarget);
        Assert.False(plan.LessonSteps[1].HasTarget);
    }

    [Fact]
    public void A_step_with_an_out_of_range_target_keeps_its_instruction()
    {
        const string json = """
            {
              "screen_observed": true, "spoken_text": "x", "actions": [],
              "steps": [ { "instruction": "Click somewhere", "x": 5000, "y": -20 } ]
            }
            """;

        var step = Assert.Single(
            AssistantPlanParser.Parse(json, hasScreenshot: true, userRequest: "teach me").LessonSteps);

        Assert.Equal("Click somewhere", step.Instruction);
        Assert.False(step.HasTarget);
    }

    [Fact]
    public void Coordinates_are_dropped_when_there_was_no_screenshot()
    {
        const string json = """
            {
              "spoken_text": "x", "actions": [],
              "steps": [ { "instruction": "Click Export", "x": 100, "y": 100 } ]
            }
            """;

        var step = Assert.Single(
            AssistantPlanParser.Parse(json, hasScreenshot: false, userRequest: "teach me").LessonSteps);

        Assert.False(step.HasTarget);
    }

    [Fact]
    public void A_response_without_steps_produces_no_lesson()
    {
        const string json = """{ "screen_observed": true, "spoken_text": "Just an answer.", "actions": [] }""";

        Assert.Empty(AssistantPlanParser.Parse(json, hasScreenshot: true, userRequest: "hello").LessonSteps);
    }

    [Fact]
    public void Steps_without_an_instruction_are_discarded()
    {
        const string json = """
            {
              "screen_observed": true, "spoken_text": "x", "actions": [],
              "steps": [ { "why": "no instruction here" }, { "instruction": "Do this" } ]
            }
            """;

        var step = Assert.Single(
            AssistantPlanParser.Parse(json, hasScreenshot: true, userRequest: "teach me").LessonSteps);

        Assert.Equal("Do this", step.Instruction);
    }
}
