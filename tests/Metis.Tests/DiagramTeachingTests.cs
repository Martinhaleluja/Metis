using Metis.AI;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Metis draws a diagram on a canvas of its own when it is explaining an idea
/// rather than pointing at software. The rules worth pinning down are the ones
/// that keep the two apart, and the ones that keep a drawn shape the shape it
/// was asked for.
/// </summary>
public sealed class DiagramRoutingTests
{
    /// <summary>
    /// The guarantee the whole feature rests on. The annotation resolver finds
    /// real controls, and it would succeed at that even for an invented
    /// triangle — marking whatever the user has open underneath it. A step that
    /// draws must never reach it, whatever else that step happens to carry.
    /// </summary>
    [Fact]
    public void A_drawn_step_never_goes_to_the_screen_resolver()
    {
        var step = new LessonStep(
            "Draw a triangle",
            TargetX: 400,
            TargetY: 400,
            DiagramShapeKind: "polygon");

        Assert.True(step.HasTarget);
        Assert.True(step.HasDiagram);
        Assert.False(LessonStepRouting.RequiresRealScreenAnnotation(step));
    }

    [Fact]
    public void A_pointing_step_still_goes_to_the_screen_resolver()
    {
        var step = new LessonStep("Click Save", TargetX: 400, TargetY: 400);

        Assert.True(LessonStepRouting.RequiresRealScreenAnnotation(step));
    }

    [Fact]
    public void A_step_with_neither_annotates_nothing()
    {
        var step = new LessonStep("Think about it");

        Assert.False(step.HasDiagram);
        Assert.False(LessonStepRouting.RequiresRealScreenAnnotation(step));
    }

    [Theory]
    [InlineData("polygon", DiagramShapeKind.Polygon)]
    [InlineData("triangle", DiagramShapeKind.Polygon)]
    [InlineData("circle", DiagramShapeKind.Circle)]
    [InlineData("arrow", DiagramShapeKind.Arrow)]
    [InlineData("vector", DiagramShapeKind.Arrow)]
    [InlineData("wave", DiagramShapeKind.Wave)]
    [InlineData("label", DiagramShapeKind.Label)]
    [InlineData("nonsense", DiagramShapeKind.None)]
    [InlineData(null, DiagramShapeKind.None)]
    public void Shape_names_are_read_generously(string? name, DiagramShapeKind expected) =>
        Assert.Equal(expected, DiagramShapeKinds.Parse(name));
}

/// <summary>
/// A diagram is projected onto a square canvas with one scale for both axes.
/// The alternative — the independent scaling a real annotation uses — turns
/// every circle into an ellipse, which is visibly wrong on any screen that is
/// not square.
/// </summary>
public sealed class DiagramProjectionTests
{
    [Fact]
    public void The_canvas_is_square_and_centred_on_the_screen()
    {
        var canvas = DiagramCanvas.Centred(0, 0, 1920, 1080, 0.6);

        Assert.Equal((int)Math.Round(1080 * 0.6), canvas.Side);
        Assert.Equal((1920 - canvas.Side) / 2, canvas.Left);
        Assert.Equal((1080 - canvas.Side) / 2, canvas.Top);
    }

    /// <summary>
    /// The same normalized distance has to travel the same number of pixels
    /// across as it does down, or shapes come out squashed.
    /// </summary>
    [Fact]
    public void One_scale_serves_both_axes()
    {
        var canvas = DiagramCanvas.Centred(0, 0, 1920, 1080, 0.6);

        var (originX, originY) = DiagramProjection.ToScreenPoint(0, 0, canvas);
        var (acrossX, _) = DiagramProjection.ToScreenPoint(500, 0, canvas);
        var (_, downY) = DiagramProjection.ToScreenPoint(0, 500, canvas);

        Assert.Equal(acrossX - originX, downY - originY);
    }

    [Fact]
    public void The_centre_of_the_space_is_the_centre_of_the_canvas()
    {
        var canvas = DiagramCanvas.Centred(0, 0, 1920, 1080, 0.6);
        var (x, y) = DiagramProjection.ToScreenPoint(500, 500, canvas);

        Assert.Equal(canvas.Left + (canvas.Side / 2), x);
        Assert.Equal(canvas.Top + (canvas.Side / 2), y);
    }

    /// <summary>
    /// A second monitor sitting to the left gives negative screen coordinates.
    /// The canvas has to follow the offset rather than assume the origin.
    /// </summary>
    [Fact]
    public void A_screen_offset_moves_the_canvas_with_it()
    {
        var canvas = DiagramCanvas.Centred(-1920, 0, 1920, 1080, 0.6);
        var (x, _) = DiagramProjection.ToScreenPoint(500, 500, canvas);

        Assert.True(x < 0);
    }
}

public sealed class DiagramGeometryTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void A_polygon_has_the_corners_it_was_asked_for(int sides) =>
        Assert.Equal(sides, DiagramGeometry.RegularPolygonPoints(500, 500, 300, sides).Count);

    /// <summary>
    /// Three sides is the fewest that encloses anything; a request for fewer is
    /// a mistake to absorb rather than a shape to attempt.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void An_impossible_side_count_still_draws_something(int sides)
    {
        var points = DiagramGeometry.RegularPolygonPoints(500, 500, 300, sides);

        Assert.InRange(points.Count, 3, 12);
    }

    /// <summary>
    /// A triangle drawn point-up is the one people recognise. Starting at zero
    /// degrees, as the maths would, prints every shape rotated from how a
    /// textbook draws it.
    /// </summary>
    [Fact]
    public void A_polygon_starts_at_the_top()
    {
        var points = DiagramGeometry.RegularPolygonPoints(500, 500, 300, 3);

        Assert.Equal(500, points[0].X);
        Assert.Equal(200, points[0].Y);
    }

    [Fact]
    public void Every_corner_sits_at_the_radius()
    {
        foreach (var (x, y) in DiagramGeometry.RegularPolygonPoints(500, 500, 300, 6))
        {
            var distance = Math.Sqrt(Math.Pow(x - 500, 2) + Math.Pow(y - 500, 2));
            Assert.InRange(distance, 299, 301);
        }
    }

    [Fact]
    public void A_circle_is_round_enough_to_read_as_round()
    {
        var points = DiagramGeometry.CirclePoints(500, 500, 250);

        Assert.Equal(DiagramGeometry.CircleSegments, points.Count);
        foreach (var (x, y) in points)
        {
            var distance = Math.Sqrt(Math.Pow(x - 500, 2) + Math.Pow(y - 500, 2));
            Assert.InRange(distance, 249, 251);
        }
    }

    /// <summary>
    /// A wave rides along the line between its ends and swings across it, so it
    /// stays readable whichever way that line points.
    /// </summary>
    [Fact]
    public void A_wave_starts_and_ends_on_its_axis()
    {
        var points = DiagramGeometry.WavePoints(100, 500, 900, 500, 3, 100);

        Assert.Equal((100, 500), points[0]);
        Assert.Equal((900, 500), points[^1]);
    }

    [Fact]
    public void A_wave_swings_by_its_amplitude()
    {
        var points = DiagramGeometry.WavePoints(100, 500, 900, 500, 3, 100);
        var highest = points.Min(point => point.Y);
        var lowest = points.Max(point => point.Y);

        Assert.InRange(500 - highest, 95, 100);
        Assert.InRange(lowest - 500, 95, 100);
    }

    [Fact]
    public void A_wave_with_no_length_does_not_divide_by_zero()
    {
        var points = DiagramGeometry.WavePoints(500, 500, 500, 500, 3, 100);

        Assert.NotEmpty(points);
    }
}

public sealed class DiagramMarkBuilderTests
{
    private static readonly DiagramCanvas Canvas = DiagramCanvas.Centred(0, 0, 1920, 1080, 0.6);

    [Fact]
    public void A_triangle_is_drawn_with_corners_not_curves()
    {
        var mark = DiagramMarkBuilder.Build(
            new LessonStep("A triangle", DiagramShapeKind: "polygon", DiagramSides: 3, DiagramSize: 300),
            Canvas);

        Assert.NotNull(mark);
        Assert.Equal(GuidanceMarkKind.Polygon, mark.Kind);
        Assert.Equal(3, mark.Points!.Count);
        Assert.True(mark.StraightEdges);
        Assert.True(mark.Persistent);
    }

    [Fact]
    public void A_circle_is_drawn_smooth()
    {
        var mark = DiagramMarkBuilder.Build(
            new LessonStep("A cell", DiagramShapeKind: "circle", DiagramSize: 300),
            Canvas);

        Assert.NotNull(mark);
        Assert.Equal(GuidanceMarkKind.Polygon, mark.Kind);
        Assert.False(mark.StraightEdges);
        Assert.Equal(DiagramGeometry.CircleSegments, mark.Points!.Count);
    }

    /// <summary>
    /// A wave is an open curve. Closing it would run a line back from the last
    /// crest to the first and tint the inside.
    /// </summary>
    [Fact]
    public void A_wave_is_an_open_stroke()
    {
        var mark = DiagramMarkBuilder.Build(
            new LessonStep("Light", DiagramShapeKind: "wave", DiagramCenterX: 100, DiagramCenterY: 500),
            Canvas);

        Assert.NotNull(mark);
        Assert.Equal(GuidanceMarkKind.Stroke, mark.Kind);
        Assert.False(mark.StraightEdges);
    }

    /// <summary>
    /// An arrow in a diagram runs from where it acts to where it points, so it
    /// has to carry both ends rather than let the overlay pick an approach.
    /// </summary>
    [Fact]
    public void An_arrow_carries_both_of_its_ends()
    {
        var mark = DiagramMarkBuilder.Build(
            new LessonStep(
                "Gravity",
                DiagramShapeKind: "arrow",
                DiagramCenterX: 500,
                DiagramCenterY: 400,
                DiagramEndX: 500,
                DiagramEndY: 800),
            Canvas);

        Assert.NotNull(mark);
        Assert.Equal(GuidanceMarkKind.Arrow, mark.Kind);
        Assert.Equal(2, mark.Points!.Count);
        Assert.True(mark.Points[1].ScreenY > mark.Points[0].ScreenY);
    }

    [Fact]
    public void A_label_needs_no_points_at_all()
    {
        var mark = DiagramMarkBuilder.Build(
            new LessonStep("The nucleus", TargetLabel: "Nucleus", DiagramShapeKind: "label"),
            Canvas);

        Assert.NotNull(mark);
        Assert.Equal(GuidanceMarkKind.Label, mark.Kind);
        Assert.Equal("Nucleus", mark.Label);
    }

    /// <summary>
    /// A model that names a shape and forgets its numbers should still get a
    /// shape. The narration carries the lesson either way, and a sensible
    /// default beats an empty canvas.
    /// </summary>
    [Fact]
    public void A_shape_with_no_numbers_still_draws()
    {
        var mark = DiagramMarkBuilder.Build(new LessonStep("A shape", DiagramShapeKind: "polygon"), Canvas);

        Assert.NotNull(mark);
        Assert.NotEmpty(mark.Points!);
    }

    [Fact]
    public void Nothing_is_built_for_a_step_that_draws_nothing() =>
        Assert.Null(DiagramMarkBuilder.Build(new LessonStep("Click Save", TargetX: 10, TargetY: 10), Canvas));

    [Fact]
    public void A_shape_lands_inside_the_canvas()
    {
        var mark = DiagramMarkBuilder.Build(
            new LessonStep("A triangle", DiagramShapeKind: "polygon", DiagramSides: 3, DiagramSize: 300),
            Canvas);

        foreach (var point in mark!.Points!)
        {
            Assert.InRange(point.ScreenX, Canvas.Left, Canvas.Left + Canvas.Side);
            Assert.InRange(point.ScreenY, Canvas.Top, Canvas.Top + Canvas.Side);
        }
    }
}

/// <summary>
/// A drawn stage stays up for as long as its sentence takes to say. Sizing it
/// the way a real annotation is sized would be meaningless: an invented shape
/// has no real size to compare against the screen.
/// </summary>
public sealed class DiagramStepDurationTests
{
    [Fact]
    public void A_short_stage_still_outlasts_the_drawing_animation() =>
        Assert.Equal(DiagramStepDuration.Minimum, DiagramStepDuration.For("Here."));

    [Fact]
    public void A_long_stage_is_capped_so_it_cannot_stall_the_lesson() =>
        Assert.Equal(
            DiagramStepDuration.Maximum,
            DiagramStepDuration.For(string.Join(' ', Enumerable.Repeat("word", 400))));

    [Fact]
    public void A_longer_sentence_holds_longer()
    {
        var shorter = DiagramStepDuration.For(string.Join(' ', Enumerable.Repeat("word", 15)));
        var longer = DiagramStepDuration.For(string.Join(' ', Enumerable.Repeat("word", 30)));

        Assert.True(longer > shorter);
    }

    [Fact]
    public void Nothing_to_say_still_leaves_the_shape_up() =>
        Assert.Equal(DiagramStepDuration.Minimum, DiagramStepDuration.For((string?)null));

    /// <summary>The reason and the instruction are both spoken, so both count.</summary>
    [Fact]
    public void The_reason_counts_toward_the_hold()
    {
        var withReason = new LessonStep(
            string.Join(' ', Enumerable.Repeat("word", 10)),
            string.Join(' ', Enumerable.Repeat("word", 20)));

        Assert.True(DiagramStepDuration.For(withReason) > DiagramStepDuration.Minimum);
    }
}

/// <summary>
/// The shape fields travel from the model's answer into a lesson step. They are
/// deliberately not gated on a screenshot, unlike the coordinates that describe
/// something inside one.
/// </summary>
public sealed class DiagramPlanParsingTests
{
    [Fact]
    public void A_drawn_step_is_read_from_the_answer()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"screen_observed":false,"spoken_text":"Here is a triangle.","steps":[
              {"instruction":"Start with a triangle","why":"Three sides is the simplest shape",
               "diagram_shape":"polygon","diagram_cx":500,"diagram_cy":500,
               "diagram_size":300,"diagram_sides":3,"label":"Triangle"}]}
            """,
            hasScreenshot: false);

        var step = Assert.Single(plan.LessonSteps);
        Assert.True(step.HasDiagram);
        Assert.Equal(DiagramShapeKind.Polygon, step.Diagram);
        Assert.Equal(500, step.DiagramCenterX);
        Assert.Equal(300, step.DiagramSize);
        Assert.Equal(3, step.DiagramSides);
    }

    /// <summary>
    /// The regression that matters most here: x and y are wiped without a
    /// screenshot because they describe something in it. A diagram describes
    /// nothing on screen, so gating it the same way would switch the feature
    /// off for anyone with screen capture turned off.
    /// </summary>
    [Fact]
    public void A_diagram_survives_having_no_screenshot()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"spoken_text":"A cell.","steps":[
              {"instruction":"Draw the membrane","diagram_shape":"circle",
               "diagram_cx":500,"diagram_cy":500,"diagram_size":350,"x":400,"y":400}]}
            """,
            hasScreenshot: false);

        var step = Assert.Single(plan.LessonSteps);

        Assert.True(step.HasDiagram);
        Assert.Equal(500, step.DiagramCenterX);

        // The screen coordinates are still discarded, so this step cannot reach
        // the resolver even though the model supplied them.
        Assert.False(step.HasTarget);
        Assert.False(LessonStepRouting.RequiresRealScreenAnnotation(step));
    }

    [Fact]
    public void A_software_step_carries_no_diagram()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"screen_observed":true,"spoken_text":"Click Save.","steps":[
              {"instruction":"Click Save","x":400,"y":300,"element":"Save"}]}
            """,
            hasScreenshot: true);

        var step = Assert.Single(plan.LessonSteps);

        Assert.False(step.HasDiagram);
        Assert.True(LessonStepRouting.RequiresRealScreenAnnotation(step));
    }
}

/// <summary>
/// Which kind of lesson a request gets is decided by the skill that matched,
/// so a new subject is a new file rather than another branch in a keyword list.
/// </summary>
public sealed class SkillDomainTests
{
    [Fact]
    public void A_subject_skill_declares_itself_academic()
    {
        var skill = SkillLibrary.Parse("Biology.md", """
            # Biology
            description: How to draw biology
            domain: academic
            applies-to: cell, photosynthesis

            Draw the membrane first.
            """);

        Assert.NotNull(skill);
        Assert.Equal(SkillDomain.Academic, skill.Domain);
    }

    /// <summary>
    /// Every skill written before domains existed has to keep behaving exactly
    /// as it did, which means defaulting to software rather than to the new one.
    /// </summary>
    [Fact]
    public void A_skill_without_a_domain_is_still_about_software()
    {
        var skill = SkillLibrary.Parse("Windows.md", """
            # Windows
            description: How to get around Windows
            applies-to: windows, taskbar

            Press the Windows key and type.
            """);

        Assert.NotNull(skill);
        Assert.Equal(SkillDomain.Software, skill.Domain);
    }

    [Fact]
    public void An_unrecognised_domain_falls_back_to_software()
    {
        var skill = SkillLibrary.Parse("Odd.md", """
            # Odd
            domain: nonsense
            applies-to: odd

            Something.
            """);

        Assert.NotNull(skill);
        Assert.Equal(SkillDomain.Software, skill.Domain);
    }

    /// <summary>
    /// The wrapper around injected skills is read by the model for both kinds
    /// of knowledge, so it must not claim they are all about software.
    /// </summary>
    [Fact]
    public void The_wrapper_does_not_claim_every_skill_is_about_software()
    {
        var described = SkillLibrary.Describe([
            new SkillPack("Biology", "Drawing biology", "Draw the membrane first.", ["cell"], Domain: SkillDomain.Academic)
        ]);

        Assert.NotNull(described);
        Assert.DoesNotContain("about this software", described, StringComparison.OrdinalIgnoreCase);
    }
}
