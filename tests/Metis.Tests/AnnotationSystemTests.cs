using Metis.AI;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The model says what it is explaining and Metis decides how that is drawn.
/// These are the cases that keep the second half of that promise.
/// </summary>
public sealed class AnnotationDirectorTests
{
    private const long Screen = 1920L * 1080;

    /// <summary>
    /// The case the whole scope idea exists for. Asked what the black window
    /// full of text is, Metis marks the window — not the nearest button inside
    /// it, which answers a question nobody asked.
    /// </summary>
    [Theory]
    [InlineData(900, 600)]
    [InlineData(1920, 1080)]
    [InlineData(400, 300)]
    public void A_window_is_always_bracketed(int width, int height) =>
        Assert.Equal(
            GuidanceMarkKind.Bracket,
            AnnotationDirector.Choose(AnnotationScope.Window, width, height, Screen));

    /// <summary>
    /// Text is underlined at any size. A box round a line of prose says "this
    /// area of the screen"; the sentence being spoken is about the words.
    /// </summary>
    [Theory]
    [InlineData(300, 18)]
    [InlineData(120, 120)]
    [InlineData(1400, 300)]
    public void Text_is_always_underlined(int width, int height) =>
        Assert.Equal(
            GuidanceMarkKind.Underline,
            AnnotationDirector.Choose(AnnotationScope.TextSpan, width, height, Screen));

    [Fact]
    public void A_movement_is_drawn_as_its_path() =>
        Assert.Equal(GuidanceMarkKind.Stroke, AnnotationDirector.Choose(AnnotationScope.Path, 0, 0, Screen));

    [Fact]
    public void Something_off_screen_gets_an_arrow() =>
        Assert.Equal(GuidanceMarkKind.Arrow, AnnotationDirector.Choose(AnnotationScope.Offscreen, 40, 40, Screen));

    [Fact]
    public void A_small_region_is_outlined() =>
        Assert.Equal(GuidanceMarkKind.Box, AnnotationDirector.Choose(AnnotationScope.Region, 400, 200, Screen));

    [Fact]
    public void A_dominating_region_is_bracketed_instead_of_outlined() =>
        Assert.Equal(GuidanceMarkKind.Bracket, AnnotationDirector.Choose(AnnotationScope.Region, 1600, 800, Screen));

    [Theory]
    [InlineData(48, 48, GuidanceMarkKind.FocusRing)]
    [InlineData(620, 44, GuidanceMarkKind.Capsule)]
    public void A_control_keeps_the_shape_rules_it_always_had(int width, int height, GuidanceMarkKind expected) =>
        Assert.Equal(expected, AnnotationDirector.Choose(AnnotationScope.Control, width, height, Screen));

    /// <summary>
    /// The old proportion-only rules had to guess whether a flat wide rectangle
    /// was text, and a toolbar button trips that test constantly. Underlining a
    /// button says "these words" when what was meant is "press this".
    /// </summary>
    [Theory]
    [InlineData(240, 40)]
    [InlineData(900, 30)]
    public void A_control_is_never_underlined_however_flat_it_is(int width, int height)
    {
        // The same proportions really would be underlined on the shape rules alone.
        Assert.Equal(GuidanceMarkKind.Underline, MarkGeometry.ForShape(width, height, Screen));
        Assert.Equal(GuidanceMarkKind.Capsule, AnnotationDirector.Choose(AnnotationScope.Control, width, height, Screen));
    }

    /// <summary>
    /// The rectangle is evidence and the claimed scope is only a claim. A
    /// "control" covering a third of the display is a panel, whatever it was
    /// called, and ringing it would draw a circle across half the screen.
    /// </summary>
    [Fact]
    public void A_control_that_turned_out_enormous_is_treated_as_a_region() =>
        Assert.Equal(
            AnnotationScope.Region,
            AnnotationDirector.Reconcile(AnnotationScope.Control, 1200, 700, Screen));

    [Fact]
    public void A_region_that_turned_out_tiny_is_treated_as_a_control() =>
        Assert.Equal(
            AnnotationScope.Control,
            AnnotationDirector.Reconcile(AnnotationScope.Region, 60, 24, Screen));

    [Fact]
    public void A_control_of_ordinary_size_is_left_alone() =>
        Assert.Equal(
            AnnotationScope.Control,
            AnnotationDirector.Reconcile(AnnotationScope.Control, 90, 32, Screen));

    /// <summary>
    /// A wrapped paragraph is still the words. Promoting it to a region would
    /// box prose that was meant to be underlined.
    /// </summary>
    [Fact]
    public void Text_stays_text_however_large_it_is() =>
        Assert.Equal(
            AnnotationScope.TextSpan,
            AnnotationDirector.Reconcile(AnnotationScope.TextSpan, 1500, 700, Screen));

    [Theory]
    [InlineData(AnnotationScope.Path)]
    [InlineData(AnnotationScope.Offscreen)]
    public void Scopes_with_no_rectangle_cannot_be_corrected_by_one(AnnotationScope scope) =>
        Assert.Equal(scope, AnnotationDirector.Reconcile(scope, 1900, 1000, Screen));

    [Fact]
    public void Nothing_is_reconciled_without_a_screen_to_compare_against() =>
        Assert.Equal(
            AnnotationScope.Control,
            AnnotationDirector.Reconcile(AnnotationScope.Control, 1200, 700, screenArea: 0));

    [Fact]
    public void Resolving_reconciles_and_then_chooses()
    {
        // Claimed as a control, but it is plainly a panel, so it must not be ringed.
        var resolved = AnnotationDirector.Resolve(
            AnnotationScope.Control, 900, 500, 1500, 800, "output panel", AnnotationSource.Element, Screen);

        Assert.Equal(AnnotationScope.Region, resolved.Scope);
        Assert.Equal(GuidanceMarkKind.Bracket, resolved.Mark);
        Assert.Equal(AnnotationSource.Element, resolved.Source);
    }

    [Fact]
    public void A_resolved_annotation_becomes_the_mark_the_overlay_draws()
    {
        var resolved = AnnotationDirector.Resolve(
            AnnotationScope.Window, 800, 400, 1000, 600, "Command Prompt", AnnotationSource.Window, Screen);

        var mark = resolved.ToMark(stepNumber: 3);

        Assert.Equal(GuidanceMarkKind.Bracket, mark.Kind);
        Assert.Equal(800, mark.ScreenX);
        Assert.Equal(1000, mark.Width);
        Assert.Equal("Command Prompt", mark.Label);
        Assert.Equal(3, mark.StepNumber);
        Assert.True(mark.HasShape);
    }
}


/// <summary>
/// Marks clear themselves now that Metis walks through a lesson rather than
/// waiting to be caught up with. A mark that outlives its sentence points at
/// something Metis has stopped talking about.
/// </summary>
public sealed class AnnotationDurationTests
{
    private const long Screen = 1920L * 1080;

    [Fact]
    public void A_button_sized_target_clears_sooner() =>
        Assert.Equal(
            AnnotationDuration.Small,
            AnnotationDuration.For(AnnotationScope.Control, 90, 32, Screen));

    /// <summary>
    /// A whole window is a lot to take in, so it keeps the full hold whatever
    /// its measured size works out to.
    /// </summary>
    [Theory]
    [InlineData(AnnotationScope.Window)]
    [InlineData(AnnotationScope.Region)]
    [InlineData(AnnotationScope.Offscreen)]
    public void Large_subjects_keep_the_standard_hold(AnnotationScope scope) =>
        Assert.Equal(AnnotationDuration.Standard, AnnotationDuration.For(scope, 40, 30, Screen));

    [Fact]
    public void A_large_control_keeps_the_standard_hold() =>
        Assert.Equal(
            AnnotationDuration.Standard,
            AnnotationDuration.For(AnnotationScope.Control, 900, 500, Screen));

    [Fact]
    public void The_two_holds_are_a_fourteen_and_nine_second_base_scaled_by_the_pace()
    {
        // The base holds are 14s and 9s; scaled by pace so markings stay visible comfortably.
        Assert.Equal(GuidanceTuning.Scale(TimeSpan.FromSeconds(14)), AnnotationDuration.Standard);
        Assert.Equal(GuidanceTuning.Scale(TimeSpan.FromSeconds(9)), AnnotationDuration.Small);
        Assert.True(AnnotationDuration.Standard > AnnotationDuration.Small);
    }

    [Fact]
    public void A_resolved_annotation_carries_its_own_hold()
    {
        var ring = AnnotationDirector.Resolve(
            AnnotationScope.Control, 500, 500, 48, 48, "save", AnnotationSource.Element, Screen);

        Assert.Equal(AnnotationDuration.Small, AnnotationDuration.For(ring, Screen));
    }

    /// <summary>
    /// Without a screen to compare against, the target is judged on its own
    /// rather than defaulting to the long hold for everything.
    /// </summary>
    [Fact]
    public void A_small_target_is_recognised_without_a_screen_to_compare_against() =>
        Assert.Equal(
            AnnotationDuration.Small,
            AnnotationDuration.For(AnnotationScope.Control, 80, 24, screenArea: 0));

    [Fact]
    public void An_unknown_size_keeps_the_standard_hold() =>
        Assert.Equal(
            AnnotationDuration.Standard,
            AnnotationDuration.For(AnnotationScope.Control, 0, 0, Screen));
}

/// <summary>
/// Coordinates arrive in a resolution-free space and have to land on real
/// pixels, on the right monitor.
/// </summary>
public sealed class CaptureProjectionTests
{
    private static readonly ScreenCapture Capture = new(
        [1], "Command Prompt", 1280, 720, ScreenLeft: 1920, ScreenTop: 0,
        SourceWidth: 2560, SourceHeight: 1440);

    [Fact]
    public void The_centre_of_the_space_is_the_centre_of_the_captured_surface() =>
        Assert.Equal((1920 + 1280, 720), CaptureProjection.ToScreenPoint(500, 500, Capture));

    [Fact]
    public void The_capture_offset_puts_a_mark_on_the_right_monitor() =>
        Assert.Equal((1920, 0), CaptureProjection.ToScreenPoint(0, 0, Capture));

    [Fact]
    public void An_extent_scales_with_the_surface() =>
        Assert.Equal((256, 144), CaptureProjection.ToScreenSize(100, 100, Capture));

    /// <summary>
    /// A control reported as a few pixels across is a rounding artefact, and a
    /// mark that size reads as dust on the screen.
    /// </summary>
    [Fact]
    public void A_vanishingly_small_extent_is_floored()
    {
        var (width, height) = CaptureProjection.ToScreenSize(1, 1, Capture);

        Assert.Equal(CaptureProjection.MinimumMarkWidth, width);
        Assert.Equal(CaptureProjection.MinimumMarkHeight, height);
    }

    [Fact]
    public void A_target_without_an_extent_still_gets_a_rectangle_to_shape()
    {
        var (_, _, width, height) = CaptureProjection.ToScreenRect(
            new AnnotationTarget(AnnotationScope.Control, 500, 500), Capture);

        Assert.True(width > 0 && height > 0);
    }

    [Fact]
    public void The_area_is_taken_from_the_source_rather_than_the_resized_image() =>
        Assert.Equal(2560L * 1440, CaptureProjection.Area(Capture));
}

/// <summary>
/// The annotation fields have to survive the parser, or the model's judgement
/// about what it is pointing at never reaches the screen.
/// </summary>
public sealed class AnnotationParsingTests
{
    [Fact]
    public void A_reply_carries_its_scope_element_and_words()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {
              "spoken_text": "that whole black window is where you type commands",
              "screen_observed": true,
              "annotation": { "scope": "window", "element": "Command Prompt", "text": "C:\\Users" }
            }
            """,
            hasScreenshot: true);

        Assert.Equal(AnnotationScope.Window, plan.Scope);
        Assert.Equal("Command Prompt", plan.ElementName);
        Assert.Equal("C:\\Users", plan.AnnotationText);
    }

    /// <summary>
    /// A bare "text" at the top level belongs to any number of other things.
    /// Reading it as the annotation would underline a phrase nobody pointed at.
    /// </summary>
    [Fact]
    public void A_top_level_text_field_is_not_mistaken_for_the_annotation()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"ok","screen_observed":true,"scope":"text","text":"unrelated"}""",
            hasScreenshot: true);

        Assert.Equal(AnnotationScope.TextSpan, plan.Scope);
        Assert.Null(plan.AnnotationText);
    }

    [Fact]
    public void An_annotation_carries_the_targets_extent()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {
              "spoken_text": "here",
              "screen_observed": true,
              "scope": "control",
              "x": 500, "y": 400, "w": 220, "h": 40, "label": "search box"
            }
            """,
            hasScreenshot: true);

        Assert.True(plan.HasAnnotation);
        Assert.Equal(220, plan.NormalizedWidth);
        Assert.Equal(40, plan.NormalizedHeight);
    }

    [Fact]
    public void A_lesson_step_carries_its_scope_and_target_name()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {
              "spoken_text": "start here",
              "screen_observed": true,
              "steps": [{
                "instruction": "click the address bar",
                "done_when": "the address bar is focused",
                "scope": "text",
                "x": 300, "y": 60, "w": 400, "h": 24,
                "element": "Address and search bar",
                "text": "chrome://newtab"
              }]
            }
            """,
            hasScreenshot: true);

        var step = Assert.Single(plan.LessonSteps);
        Assert.Equal(AnnotationScope.TextSpan, step.Scope);
        Assert.Equal("Address and search bar", step.ElementName);
        Assert.Equal("chrome://newtab", step.Text);

        var target = step.ToAnnotationTarget();
        Assert.Equal(AnnotationScope.TextSpan, target.Scope);
        Assert.True(target.HasExtent);
        Assert.Equal("Address and search bar", target.ElementName);
    }

    /// <summary>
    /// An older reply that named a shape still has to produce a usable mark
    /// rather than falling back to a ring for everything.
    /// </summary>
    [Fact]
    public void A_reply_using_the_old_highlight_wording_still_reads()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"there","screen_observed":true,"highlight":"region"}""",
            hasScreenshot: true);

        Assert.Equal(AnnotationScope.Region, plan.Scope);
    }

    [Fact]
    public void A_single_turn_plan_converts_to_an_annotation_target()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {
              "spoken_text": "The Save button is in the top left.",
              "screen_observed": true,
              "scope": "control",
              "x": 120,
              "y": 80,
              "w": 60,
              "h": 32,
              "label": "Save",
              "element": "Save File"
            }
            """,
            hasScreenshot: true);

        Assert.True(plan.HasAnnotation);
        Assert.Empty(plan.LessonSteps);

        var target = plan.ToAnnotationTarget();
        Assert.Equal(AnnotationScope.Control, target.Scope);
        Assert.Equal(120, target.NormalizedX);
        Assert.Equal(80, target.NormalizedY);
        Assert.Equal(60, target.NormalizedWidth);
        Assert.Equal(32, target.NormalizedHeight);
        Assert.Equal("Save", target.Label);
        Assert.Equal("Save File", target.ElementName);
        Assert.True(target.HasPoint);
        Assert.True(target.HasExtent);
    }
}

