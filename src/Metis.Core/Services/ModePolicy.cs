using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// The single place that decides what each operating mode means. Both the
/// prompt sent to the reasoning provider and the action filter applied to its
/// answer are derived from here, so the two can never disagree about what the
/// mode allows.
/// </summary>
public static class ModePolicy
{
    private static readonly ModeCapabilities LearnCapabilities = new(
        OperatingMode.Learn,
        "Learn",
        "You do the work. Metis teaches while you do it.",
        MayActUnprompted: false,
        MayActWhenAsked: false,
        MaxActionsPerBatch: 2,
        ExplainConcepts: true,
        ExplainWhy: true,
        TrackSkills: true,
        ShowVisualGuidance: true);

    private static readonly ModeCapabilities GuideCapabilities = new(
        OperatingMode.Guide,
        "Guide",
        "You do the work. Metis points to the next step.",
        MayActUnprompted: false,
        MayActWhenAsked: true,
        MaxActionsPerBatch: 3,
        ExplainConcepts: false,
        ExplainWhy: true,
        TrackSkills: true,
        ShowVisualGuidance: true);

    private static readonly ModeCapabilities AssistCapabilities = new(
        OperatingMode.Assist,
        "Assist",
        "Metis shares the work and leaves the important choices to you.",
        MayActUnprompted: true,
        MayActWhenAsked: true,
        MaxActionsPerBatch: 4,
        ExplainConcepts: false,
        ExplainWhy: true,
        TrackSkills: true,
        ShowVisualGuidance: true);

    private static readonly ModeCapabilities AutopilotCapabilities = new(
        OperatingMode.Autopilot,
        "Autopilot",
        "Metis performs the task and can explain it afterwards.",
        MayActUnprompted: true,
        MayActWhenAsked: true,
        MaxActionsPerBatch: 6,
        ExplainConcepts: false,
        ExplainWhy: false,
        TrackSkills: false,
        ShowVisualGuidance: true);

    public static ModeCapabilities For(OperatingMode mode) => mode switch
    {
        OperatingMode.Learn => LearnCapabilities,
        OperatingMode.Guide => GuideCapabilities,
        OperatingMode.Assist => AssistCapabilities,
        _ => AutopilotCapabilities
    };

    public static IReadOnlyList<ModeCapabilities> All =>
        [LearnCapabilities, GuideCapabilities, AssistCapabilities, AutopilotCapabilities];

    public static OperatingMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "learn" or "teach" or "teacher" => OperatingMode.Learn,
        "assist" or "collaborate" => OperatingMode.Assist,
        "autopilot" or "auto" or "agent" => OperatingMode.Autopilot,
        _ => OperatingMode.Guide
    };

    /// <summary>
    /// True when an action changes the user's computer rather than only
    /// pointing at it. Non-mutating actions stay available in every mode
    /// because showing the user where to go is never a computer action.
    /// </summary>
    public static bool IsMutating(DesktopActionKind kind) => kind is
        DesktopActionKind.LeftClick or DesktopActionKind.DoubleClick or DesktopActionKind.RightClick or
        DesktopActionKind.TypeText or DesktopActionKind.KeyPress or DesktopActionKind.OpenApp or
        DesktopActionKind.OpenUrl;

    /// <summary>
    /// Decides whether one action survives the current mode.
    /// <paramref name="userAskedForAction"/> is the user's own request for this
    /// turn, never the model's claim about it.
    /// </summary>
    public static bool Allows(OperatingMode mode, DesktopActionKind kind, bool userAskedForAction)
    {
        if (!IsMutating(kind))
        {
            return true;
        }

        var capabilities = For(mode);
        return capabilities.MayActUnprompted ||
               (capabilities.MayActWhenAsked && userAskedForAction);
    }

    /// <summary>
    /// Applies the mode to a whole plan: drops actions the mode forbids and
    /// trims the batch to the mode's step budget. Learn and Guide keep the
    /// pointer moves so the user still sees where to go.
    /// </summary>
    public static IReadOnlyList<DesktopAction> Filter(
        OperatingMode mode,
        IReadOnlyList<DesktopAction> actions,
        bool userAskedForAction,
        out int withheldCount)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var capabilities = For(mode);
        var permitted = actions
            .Where(action => Allows(mode, action.Kind, userAskedForAction))
            .Take(capabilities.MaxActionsPerBatch)
            .ToArray();
        withheldCount = actions.Count - permitted.Length;
        return permitted;
    }

    /// <summary>
    /// The mode-specific block appended to the system instruction. It tells the
    /// provider how to answer; the filter above still enforces the same rules
    /// on whatever comes back.
    /// </summary>
    /// <summary>
    /// Shared guidance on choosing a mark. Metis picks the shape that suits
    /// what it is pointing at, so the mark itself carries meaning rather than
    /// every target looking identical.
    /// </summary>
    public const string HighlightInstruction = """
        Choose how to mark what you point at with a "highlight" field, on the reply and on each step:
          ring       a single control such as a button, icon, checkbox, or menu item. Use this by default.
          box        a whole panel, dialog, toolbar, or region of the window.
          underline  a line of text, a row, a field value, or a span you are reading out.
          arrow      something the user is not looking at, such as a control at the far edge of the screen.
        Pick the one that fits the target; do not use ring for everything.
        Give w and h whenever you can see the control's extent, so the mark takes its shape rather than ringing a fixed circle.

        When the user asks where something is — "show me", "where is", "which button", "where do I type" — you must
        return a move_pointer action carrying x, y and, where visible, w and h for that control. Describing the location
        in words is not an answer to that question: the user asked to be shown, so the reply has to put a mark on the
        screen. Say the sentence as well, but never send the sentence on its own.
        """;

    public static string BuildInstruction(OperatingMode mode) => mode switch
    {
        OperatingMode.Learn => """
            operating_mode: LEARN. The user performs every step; you teach while they work.
            Explain what to do and why it matters in plain language, using the concepts and tool names visible on their screen.
            Point at controls with move_pointer and a short bubble_cue, and then ask the user to perform the step themselves.
            Do not click, type, press keys, or open apps or URLs even if the user asks; instead explain how to do it and offer to switch to Assist or Autopilot mode.
            When the screen shows a mistake, explain the cause before the correction.
            Adapt the depth of the explanation to the skills listed in user_skills; skip explanations for anything already advanced or mastered.

            Teach as a sequence the user works through, not as one block of prose. Return a "steps" array, in order, each with:
              instruction  what the user should do, one action, in plain words
              why          why this step matters, one short sentence
              done_when    the visible evidence on screen that proves the user finished it, so the lesson can wait and check
              x, y         normalized 0-1000 coordinates of the centre of the control for this step, omitted when not visible
              w, h         the control's width and height in the same 0-1000 space, so the highlight takes its shape
              to_x, to_y   only for a movement such as a drag: where the gesture ends, so the motion can be demonstrated
              label        two or three words naming the control
            Keep each step to a single action the user can do without further explanation, and prefer more small steps over fewer large ones.
            Put only the first step's explanation in spoken_text; the rest are read out as the user reaches them.
            """,
        OperatingMode.Guide => """
            operating_mode: GUIDE. The user performs the work; you direct them efficiently.
            Give the exact next step, point at the exact control with move_pointer and a short bubble_cue, and keep theory to one sentence unless asked.
            Perform a click, keystroke, or launch only when the user explicitly asked you to in this request. Otherwise point and let the user act.
            Correct obvious mistakes directly rather than lecturing about them.
            """,
        OperatingMode.Assist => """
            operating_mode: ASSIST. You and the user share the work.
            Handle routine, repetitive, and structural steps yourself, and leave creative, destructive, and consequential choices to the user.
            Say briefly what you are doing while you do it, and stop to ask when a choice is genuinely the user's to make.
            """,
        _ => """
            operating_mode: AUTOPILOT. You perform the task.
            Work in small verified batches: act, observe the fresh screen, then continue. Keep spoken_text to a short progress note.
            Track what you did so you can answer "show me what you did" afterwards.
            Stop and ask before anything destructive, irreversible, financial, or security-sensitive; speed never overrides that.
            """
    };
}
