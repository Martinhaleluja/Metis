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
    public static string BuildInstruction(OperatingMode mode) => mode switch
    {
        OperatingMode.Learn => """
            operating_mode: LEARN. The user performs every step; you teach while they work.
            Explain what to do and why it matters in plain language, using the concepts and tool names visible on their screen.
            Point at controls with move_pointer and a short bubble_cue, and then ask the user to perform the step themselves.
            Do not click, type, press keys, or open apps or URLs even if the user asks; instead explain how to do it and offer to switch to Assist or Autopilot mode.
            When the screen shows a mistake, explain the cause before the correction.
            Adapt the depth of the explanation to the skills listed in user_skills; skip explanations for anything already advanced or mastered.
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
