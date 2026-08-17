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
/// Whether Metis teaches or takes over is decided from the user's own words,
/// before any model sees them. These cases pin the orderings that decide it.
/// </summary>
public sealed class IntentDetectorTests
{
    [Theory]
    [InlineData("how do I open Chrome?")]
    [InlineData("what is this window?")]
    [InlineData("explain what this button does")]
    [InlineData("teach me to crop an image")]
    [InlineData("walk me through exporting")]
    public void A_question_is_answered_rather_than_performed(string request) =>
        Assert.Equal(AssistanceIntent.Teach, IntentDetector.Detect(request).Intent);

    /// <summary>
    /// The ordering that matters most: every request to be taught something
    /// also names the thing, so the action word is always present.
    /// </summary>
    [Theory]
    [InlineData("show me how to open Chrome")]
    [InlineData("how do i click the export button")]
    [InlineData("explain how to type a command in here")]
    public void Asking_to_be_shown_beats_the_action_word_inside_it(string request)
    {
        var decision = IntentDetector.Detect(request);

        Assert.Equal(AssistanceIntent.Teach, decision.Intent);
        Assert.True(decision.IsExplicit);
    }

    [Theory]
    [InlineData("open Chrome")]
    [InlineData("close this tab")]
    [InlineData("type my email address")]
    public void A_plain_instruction_is_carried_out(string request)
    {
        var decision = IntentDetector.Detect(request);

        Assert.Equal(AssistanceIntent.TakeControl, decision.Intent);
        Assert.True(decision.IsExplicit);
    }

    [Theory]
    [InlineData("just do it")]
    [InlineData("can you do it for me")]
    [InlineData("take care of it")]
    public void An_outright_handover_is_taken(string request)
    {
        var decision = IntentDetector.Detect(request);

        Assert.Equal(AssistanceIntent.TakeControl, decision.Intent);
        Assert.True(decision.IsExplicit);
    }

    [Theory]
    [InlineData("where is the save button")]
    [InlineData("show me the toolbar")]
    [InlineData("which button exports")]
    public void Asking_where_something_is_gets_a_mark_not_a_click(string request) =>
        Assert.Equal(AssistanceIntent.Teach, IntentDetector.Detect(request).Intent);

    /// <summary>
    /// With nothing to go on, the reading that changes nothing is the only safe
    /// one: not acting is always recoverable and acting is not.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    public void Anything_unclear_teaches(string request)
    {
        var decision = IntentDetector.Detect(request);

        Assert.Equal(AssistanceIntent.Teach, decision.Intent);
        Assert.False(decision.IsExplicit);
    }

    [Fact]
    public void The_reason_names_the_words_it_acted_on() =>
        Assert.Contains("just do it", IntentDetector.Detect("ok just do it").Reason, StringComparison.Ordinal);

    [Theory]
    [InlineData("next")]
    [InlineData("ok done")]
    [InlineData("what's next?")]
    [InlineData("keep going")]
    public void A_continuation_carries_no_intent_of_its_own(string request) =>
        Assert.True(IntentDetector.IsContinuation(request));

    [Theory]
    [InlineData("open Chrome")]
    [InlineData("how do I do this")]
    public void A_real_request_is_not_a_continuation(string request) =>
        Assert.False(IntentDetector.IsContinuation(request));
}

/// <summary>
/// Telling Metis to do something has to actually reach the mouse and keyboard.
/// The chain from the user's words to a permitted action runs through the
/// detector, the intent filter, and the safety engine, and a break anywhere in
/// it leaves Metis announcing that it is doing something and then doing
/// nothing — the worst outcome available, because the user believes it.
/// </summary>
public sealed class TakeControlTests
{
    private static readonly DesktopAction Click = new(DesktopActionKind.LeftClick, 500, 500, Label: "Save");
    private static readonly DesktopAction Typing = new(DesktopActionKind.TypeText, Text: "hello", HasCoordinates: false);
    private static readonly DesktopAction Launch = new(DesktopActionKind.OpenApp, Text: "Notepad", HasCoordinates: false);
    private static readonly DesktopAction Pointing = new(DesktopActionKind.MovePointer, 400, 300, Label: "Save");

    [Theory]
    [InlineData("open notepad")]
    [InlineData("just do it for me")]
    [InlineData("close this tab")]
    public void An_instruction_survives_all_the_way_to_a_permitted_action(string request)
    {
        var decision = IntentDetector.Detect(request);
        var handedOver = decision is { Intent: AssistanceIntent.TakeControl, IsExplicit: true };
        Assert.True(handedOver, $"'{request}' was not read as a handover");

        var kept = IntentPolicy.Filter(decision.Intent, [Click, Typing, Launch], handedOver, out var withheld);

        Assert.Equal(3, kept.Count);
        Assert.Equal(0, withheld);

        var safety = new SafetyPolicyEngine();
        foreach (var action in kept)
        {
            Assert.True(
                safety.IsPermitted(action, IntentPolicy.ToMode(decision.Intent), handedOver, out var reason),
                $"{action.Kind} was refused: {reason}");
        }
    }

    /// <summary>
    /// The other half of the promise: teaching never touches the computer, but
    /// it still points, or the user is told about a control and not shown it.
    /// </summary>
    [Fact]
    public void Teaching_drops_the_actions_but_keeps_the_pointing()
    {
        var decision = IntentDetector.Detect("how do I save this?");

        var kept = IntentPolicy.Filter(decision.Intent, [Click, Typing, Launch, Pointing], userHandedOver: false, out var withheld);

        Assert.Equal([DesktopActionKind.MovePointer], kept.Select(action => action.Kind));
        Assert.Equal(3, withheld);
    }

    /// <summary>
    /// Defence in depth. The detector only ever reports an explicit handover
    /// today, but the filter must not rely on that: an intent arriving without
    /// one still may not move the mouse.
    /// </summary>
    [Fact]
    public void An_intent_to_act_without_an_explicit_handover_still_cannot_act()
    {
        var kept = IntentPolicy.Filter(
            AssistanceIntent.TakeControl, [Click, Typing], userHandedOver: false, out var withheld);

        Assert.Empty(kept);
        Assert.Equal(2, withheld);
    }

    /// <summary>
    /// Metis batches its work, so a plan longer than the budget is trimmed
    /// rather than run in one burst the user cannot follow or interrupt.
    /// </summary>
    [Fact]
    public void A_long_plan_is_trimmed_to_the_batch_budget()
    {
        var many = Enumerable.Repeat(Click, 10).ToArray();

        var kept = IntentPolicy.Filter(AssistanceIntent.TakeControl, many, userHandedOver: true, out var withheld);

        Assert.Equal(IntentPolicy.For(AssistanceIntent.TakeControl).MaxActionsPerBatch, kept.Count);
        Assert.Equal(many.Length - kept.Count, withheld);
    }

    /// <summary>
    /// Pointing is not a computer action, so it is never withheld whatever the
    /// user asked for.
    /// </summary>
    [Theory]
    [InlineData(AssistanceIntent.Teach)]
    [InlineData(AssistanceIntent.TakeControl)]
    public void Pointing_is_always_allowed(AssistanceIntent intent) =>
        Assert.True(IntentPolicy.Allows(intent, DesktopActionKind.MovePointer, userHandedOver: false));

    /// <summary>
    /// While Metis works the computer the real pointer is already crossing the
    /// screen doing the job. Sending the companion after it, and ringing a
    /// control that is about to be clicked anyway, gives the user two things to
    /// follow at once. Doing the work is narrated; only showing is annotated.
    /// </summary>
    [Fact]
    public void Doing_the_work_is_spoken_rather_than_drawn()
    {
        Assert.False(IntentPolicy.For(AssistanceIntent.TakeControl).ShowsAnnotations);
        Assert.True(IntentPolicy.For(AssistanceIntent.Teach).ShowsAnnotations);
    }

    /// <summary>
    /// The runtime reads this through the legacy mode it still threads around,
    /// so the projection has to preserve it in both directions.
    /// </summary>
    [Theory]
    [InlineData(OperatingMode.Learn, true)]
    [InlineData(OperatingMode.Guide, true)]
    [InlineData(OperatingMode.Assist, false)]
    [InlineData(OperatingMode.Autopilot, false)]
    public void The_annotation_rule_survives_the_legacy_mode_projection(OperatingMode mode, bool expected) =>
        Assert.Equal(expected, IntentPolicy.For(IntentPolicy.FromMode(mode)).ShowsAnnotations);
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
    public void The_two_holds_are_seven_and_five_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(7), AnnotationDuration.Standard);
        Assert.Equal(TimeSpan.FromSeconds(5), AnnotationDuration.Small);
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
    public void A_pointer_move_carries_the_targets_extent()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {
              "spoken_text": "here",
              "screen_observed": true,
              "actions": [{ "type": "move_pointer", "x": 500, "y": 400, "w": 220, "h": 40, "label": "search box" }]
            }
            """,
            hasScreenshot: true);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(220, action.NormalizedWidth);
        Assert.Equal(40, action.NormalizedHeight);
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
}
