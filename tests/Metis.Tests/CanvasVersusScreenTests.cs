using Metis.AI;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Metis can either mark the real screen or draw a subject on a blank canvas,
/// and the canvas mode tells the model to ignore the screen entirely. Choosing
/// it too eagerly is what made annotations disappear: a video about triangles
/// matched a geometry skill by its window title, so questions about what was on
/// screen were answered with shapes floating over the user's work instead of
/// marks on the thing they asked about.
/// </summary>
public sealed class CanvasVersusScreenTests
{
    private static LessonStep Shape() => new("Draw it", DiagramShapeKind: "circle");

    private static LessonStep ShapeAndTarget() =>
        new("Look here", TargetX: 400, TargetY: 300, DiagramShapeKind: "circle");

    private static LessonStep TargetOnly() => new("Click Save", TargetX: 400, TargetY: 300);

    [Fact]
    public void A_subject_question_is_illustrated() =>
        Assert.True(LessonStepRouting.ShouldIllustrateSubject(
            subjectMatchedFromRequest: true, requestAsksAboutScreen: false));

    /// <summary>
    /// The regression. Asking about the screen is answered on the screen, even
    /// when the words also name something Metis knows how to draw.
    /// </summary>
    [Fact]
    public void A_question_about_the_screen_is_never_swapped_for_a_diagram() =>
        Assert.False(LessonStepRouting.ShouldIllustrateSubject(
            subjectMatchedFromRequest: true, requestAsksAboutScreen: true));

    [Fact]
    public void An_ordinary_request_is_not_illustrated() =>
        Assert.False(LessonStepRouting.ShouldIllustrateSubject(
            subjectMatchedFromRequest: false, requestAsksAboutScreen: false));

    [Fact]
    public void A_shape_is_drawn_only_when_a_subject_was_asked_for()
    {
        Assert.True(LessonStepRouting.ShouldDrawOnCanvas(Shape(), illustratingASubject: true));
        Assert.False(LessonStepRouting.ShouldDrawOnCanvas(Shape(), illustratingASubject: false));
    }

    /// <summary>
    /// A step pointing at something real is answered on the screen, even if it
    /// also describes a shape — the real control is what was asked about.
    /// </summary>
    [Fact]
    public void A_real_target_beats_a_shape()
    {
        Assert.False(LessonStepRouting.ShouldDrawOnCanvas(ShapeAndTarget(), illustratingASubject: true));
        Assert.True(LessonStepRouting.RequiresRealScreenAnnotation(TargetOnly()));
    }

    [Fact]
    public void A_step_with_no_shape_is_never_drawn_on_the_canvas() =>
        Assert.False(LessonStepRouting.ShouldDrawOnCanvas(TargetOnly(), illustratingASubject: true));

    /// <summary>
    /// The screen-observation test is what the gate leans on, so the phrasings
    /// that broke are pinned here: each of these must keep Metis on the screen.
    /// </summary>
    [Theory]
    [InlineData("explain what is on the screen")]
    [InlineData("what is this")]
    [InlineData("where is the record button")]
    [InlineData("what does that menu do")]
    public void Screen_phrasings_are_recognised_as_being_about_the_screen(string request) =>
        Assert.True(RequestIntent.RequiresScreenObservation(request));

    [Theory]
    [InlineData("explain the concept of vectors")]
    [InlineData("teach me the pythagorean theorem")]
    public void Concept_phrasings_are_not_about_the_screen(string request) =>
        Assert.False(RequestIntent.RequiresScreenObservation(request));
}

/// <summary>
/// A reply often names the spot it is talking about once, at the top, and then
/// writes steps that read as prose without repeating the coordinates. The
/// lesson used to drop that annotation entirely, so a reply that knew exactly
/// where to point marked nothing at all — no highlight, no companion movement,
/// and nothing in the log to say why.
/// </summary>
public sealed class LessonAnnotationFallbackTests
{
    private static AssistantPlan PlanWithAnnotationAndOneStep() => AssistantPlanParser.Parse(
        """
        {"screen_observed":true,"spoken_text":"That is the bandwidth setting.",
         "scope":"control","x":640,"y":410,"element":"Download rate",
         "steps":[{"instruction":"Set the download limit here","why":"It caps the speed"}]}
        """,
        hasScreenshot: true);

    [Fact]
    public void A_reply_can_carry_both_an_annotation_and_a_step()
    {
        var plan = PlanWithAnnotationAndOneStep();

        Assert.True(plan.HasAnnotation);
        Assert.Single(plan.LessonSteps);
    }

    /// <summary>
    /// The step itself names no target — this is the shape of reply that drew
    /// nothing, so the annotation has to be reachable as a fallback.
    /// </summary>
    [Fact]
    public void The_step_names_no_target_of_its_own()
    {
        var step = Assert.Single(PlanWithAnnotationAndOneStep().LessonSteps);

        Assert.False(step.HasTarget);
        Assert.False(LessonStepRouting.RequiresRealScreenAnnotation(step));
    }

    /// <summary>
    /// The fallback is the plan's own annotation, and it points where the reply
    /// said it was talking about.
    /// </summary>
    [Fact]
    public void The_replys_annotation_is_a_usable_target()
    {
        var target = PlanWithAnnotationAndOneStep().ToAnnotationTarget();

        Assert.Equal(640, target.NormalizedX);
        Assert.Equal(410, target.NormalizedY);
        Assert.Equal("Download rate", target.ElementName);
    }

    /// <summary>
    /// A step with no target and no reply-level annotation genuinely has
    /// nowhere to point, and must be reported rather than silently skipped.
    /// </summary>
    [Fact]
    public void A_step_with_nothing_to_point_at_is_still_recognisable()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Have a look around.","steps":[{"instruction":"Explore the menus"}]}""",
            hasScreenshot: true);

        Assert.False(plan.HasAnnotation);
        Assert.False(Assert.Single(plan.LessonSteps).HasTarget);
    }
}
