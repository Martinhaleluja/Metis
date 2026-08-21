using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// The parser now reads a teaching reply, not a plan of actions: what to say,
/// where on screen to mark, and the steps for the learner to work through.
/// Metis no longer operates the computer, so there is nothing here about
/// clicking, typing, or opening — only guidance.
/// </summary>
public sealed class AssistantPlanParserTests
{
    [Fact]
    public void Ordinary_text_falls_back_to_speech_only()
    {
        var plan = AssistantPlanParser.Parse("Just a sentence, no JSON.", hasScreenshot: false);

        Assert.Equal("Just a sentence, no JSON.", plan.SpokenText);
        Assert.False(plan.HasAnnotation);
        Assert.Empty(plan.LessonSteps);
    }

    [Fact]
    public void Spoken_text_is_read_from_the_reply()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Here is the toolbar.","bubble_cue":null}""",
            hasScreenshot: true);

        Assert.Equal("Here is the toolbar.", plan.SpokenText);
    }

    /// <summary>
    /// Screen grounding is never taken on trust: a reply cannot claim to have
    /// read a screen that was never attached.
    /// </summary>
    [Fact]
    public void Screen_observed_is_never_trusted_without_an_attached_screenshot()
    {
        var plan = AssistantPlanParser.Parse(
            """{"screen_observed":true,"spoken_text":"I can see it."}""",
            hasScreenshot: false);

        Assert.False(plan.ScreenObserved);
    }

    [Fact]
    public void Screen_observed_holds_when_a_screenshot_was_attached()
    {
        var plan = AssistantPlanParser.Parse(
            """{"screen_observed":true,"spoken_text":"I can see it."}""",
            hasScreenshot: true);

        Assert.True(plan.ScreenObserved);
    }

    /// <summary>
    /// An annotation's coordinates describe something in the screenshot, so
    /// without one they mean nothing and are discarded.
    /// </summary>
    [Fact]
    public void An_annotation_needs_a_screenshot_to_land_on()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Over here.","scope":"control","x":500,"y":250,"element":"Save"}""",
            hasScreenshot: false);

        Assert.False(plan.HasAnnotation);
    }

    [Fact]
    public void An_annotation_is_read_when_a_screenshot_is_present()
    {
        var plan = AssistantPlanParser.Parse(
            """{"screen_observed":true,"spoken_text":"The Save button.","scope":"control","x":500,"y":250,"element":"Save"}""",
            hasScreenshot: true);

        Assert.True(plan.HasAnnotation);
        Assert.Equal(AnnotationScope.Control, plan.Scope);
        Assert.Equal("Save", plan.ElementName);
        Assert.Equal(500, plan.NormalizedX);
        Assert.Equal(250, plan.NormalizedY);
    }

    [Fact]
    public void Out_of_range_coordinates_are_treated_as_no_annotation()
    {
        var plan = AssistantPlanParser.Parse(
            """{"screen_observed":true,"spoken_text":"Somewhere.","scope":"control","x":5000,"y":9000}""",
            hasScreenshot: true);

        Assert.False(plan.HasAnnotation);
    }

    [Fact]
    public void Fenced_json_is_still_parsed()
    {
        var plan = AssistantPlanParser.Parse(
            """
            Here you go:
            ```json
            {"screen_observed":true,"spoken_text":"The menu.","scope":"region","x":100,"y":100,"w":200,"h":80}
            ```
            """,
            hasScreenshot: true);

        Assert.Equal("The menu.", plan.SpokenText);
        Assert.Equal(AnnotationScope.Region, plan.Scope);
        Assert.True(plan.HasAnnotation);
    }

    [Fact]
    public void Lesson_steps_are_read_in_order()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"screen_observed":true,"spoken_text":"Let's export.","goal":"Export a PDF","steps":[
              {"instruction":"Open the File menu","why":"Export lives there","x":40,"y":20,"element":"File"},
              {"instruction":"Choose Export","why":"That starts it","x":60,"y":120,"element":"Export"}]}
            """,
            hasScreenshot: true);

        Assert.Equal("Export a PDF", plan.Goal);
        Assert.Equal(2, plan.LessonSteps.Count);
        Assert.Equal("Open the File menu", plan.LessonSteps[0].Instruction);
        Assert.Equal("Choose Export", plan.LessonSteps[1].Instruction);
    }

    /// <summary>
    /// A garbled reply must never take the whole turn down; the worst case is a
    /// spoken fallback rather than an exception.
    /// </summary>
    [Fact]
    public void Malformed_json_degrades_to_speech_rather_than_throwing()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"half a repl""",
            hasScreenshot: false);

        Assert.False(string.IsNullOrWhiteSpace(plan.SpokenText));
        Assert.Empty(plan.LessonSteps);
    }
}
