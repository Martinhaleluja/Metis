using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// The single place that decides what each intent means. Both the instruction
/// sent to the reasoning provider and the filter applied to its answer come
/// from here, so a persuasive reply can never widen what Metis is allowed to
/// do — the filter re-checks whatever comes back against the same rules the
/// prompt described.
/// </summary>
public static class IntentPolicy
{
    private static readonly IntentCapabilities TeachCapabilities = new(
        AssistanceIntent.Teach,
        "Showing you",
        "You do the work. Metis marks the screen and waits.",
        MayActWhenAsked: false,
        MaxActionsPerBatch: 2,
        ExplainConcepts: true,
        TrackSkills: true,
        WatchesTheUser: true,
        ShowsAnnotations: true);

    private static readonly IntentCapabilities TakeControlCapabilities = new(
        AssistanceIntent.TakeControl,
        "Doing it",
        "Metis performs the task and says what it is doing.",
        MayActWhenAsked: true,
        MaxActionsPerBatch: 4,
        ExplainConcepts: false,
        TrackSkills: false,
        WatchesTheUser: false,
        ShowsAnnotations: false);

    public static IntentCapabilities For(AssistanceIntent intent) => intent switch
    {
        AssistanceIntent.TakeControl => TakeControlCapabilities,
        _ => TeachCapabilities
    };

    public static IReadOnlyList<IntentCapabilities> All => [TeachCapabilities, TakeControlCapabilities];

    /// <summary>
    /// Applies the mode's ceiling to what was read from the request.
    ///
    /// The two layers answer different questions. The request says what the
    /// user wants this time; the mode says what Metis is permitted to do at
    /// all. So "just do it for me" in Learn is not a mistake to be corrected or
    /// an instruction to be obeyed — it is a request Metis cannot grant, and it
    /// says so and shows them instead. Clamping here rather than at each call
    /// site is what makes that guarantee hold everywhere, including in paths
    /// written later by someone who has forgotten the mode exists.
    /// </summary>
    public static IntentDecision Clamp(AssistanceMode mode, IntentDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (mode == AssistanceMode.Autopilot || decision.Intent == AssistanceIntent.Teach)
        {
            return decision;
        }

        return new IntentDecision(
            AssistanceIntent.Teach,
            $"{decision.Reason} Metis is in Learn mode, so it will show you instead.",
            IsExplicit: decision.IsExplicit);
    }

    /// <summary>
    /// True when the user asked for something the mode forbids, so Metis can
    /// say why rather than silently teaching at someone who asked it to act.
    /// A refusal nobody notices reads as Metis ignoring them.
    /// </summary>
    public static bool WasClampedByMode(AssistanceMode mode, IntentDecision detected) =>
        mode == AssistanceMode.Learn && detected.Intent == AssistanceIntent.TakeControl;

    /// <summary>
    /// Projects an intent onto the older four-mode vocabulary that the safety
    /// engine, the task tracker, and the provider requests still speak.
    ///
    /// The four modes are gone as a product: nothing asks the user to pick one
    /// and nothing displays one. What remains is a transport type threaded
    /// through a dozen records, and translating at this one boundary retires
    /// the concept without a rename touching every file that mentions it.
    /// Teaching maps to Learn and taking control to Assist, which are the two
    /// the enforcement rules were already written around.
    /// </summary>
    public static OperatingMode ToMode(AssistanceIntent intent) =>
        intent == AssistanceIntent.TakeControl ? OperatingMode.Assist : OperatingMode.Learn;

    /// <summary>
    /// Reads an intent back out of the legacy vocabulary, for the settings of
    /// users who chose a mode before Metis started deciding for itself.
    /// </summary>
    public static AssistanceIntent FromMode(OperatingMode mode) =>
        mode is OperatingMode.Assist or OperatingMode.Autopilot
            ? AssistanceIntent.TakeControl
            : AssistanceIntent.Teach;

    /// <summary>
    /// True when an action changes the user's computer rather than only
    /// pointing at it. Non-mutating actions stay available whatever the intent,
    /// because showing the user where to go is never a computer action.
    /// </summary>
    public static bool IsMutating(DesktopActionKind kind) => kind is
        DesktopActionKind.LeftClick or DesktopActionKind.DoubleClick or DesktopActionKind.RightClick or
        DesktopActionKind.TypeText or DesktopActionKind.KeyPress or DesktopActionKind.OpenApp or
        DesktopActionKind.OpenUrl or DesktopActionKind.RunCommand;

    /// <summary>
    /// Decides whether one action survives the detected intent.
    /// <paramref name="userHandedOver"/> comes from the user's own words for
    /// this turn — <see cref="IntentDecision.IsExplicit"/> on a
    /// <see cref="AssistanceIntent.TakeControl"/> reading — and never from the
    /// model's claim about them.
    /// </summary>
    public static bool Allows(AssistanceIntent intent, DesktopActionKind kind, bool userHandedOver)
    {
        if (!IsMutating(kind))
        {
            return true;
        }

        return For(intent).MayActWhenAsked && userHandedOver;
    }

    /// <summary>
    /// Applies the intent to a whole plan: drops the actions it forbids and
    /// trims the batch to its budget. Teaching keeps the pointer moves, so the
    /// user is still shown where to go.
    /// </summary>
    public static IReadOnlyList<DesktopAction> Filter(
        AssistanceIntent intent,
        IReadOnlyList<DesktopAction> actions,
        bool userHandedOver,
        out int withheldCount)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var permitted = actions
            .Where(action => Allows(intent, action.Kind, userHandedOver))
            .Take(For(intent).MaxActionsPerBatch)
            .ToArray();
        withheldCount = actions.Count - permitted.Length;
        return permitted;
    }

    /// <summary>
    /// How to annotate. This asks the model what it is pointing at and never
    /// what shape to draw, because the two questions get very different answer
    /// quality: a model reliably knows that a sentence is about a terminal
    /// rather than about a button inside it, and just as reliably answers
    /// "ring" to every question about shape. The shape is then derived from the
    /// target's real measured geometry by <c>AnnotationDirector</c>.
    /// </summary>
    public const string AnnotationInstruction = """
        Whenever your answer is about something on screen, attach an annotation describing what you are
        talking about. Give a "scope" naming the kind of thing it is:
          control    one thing the user can press — a button, icon, checkbox, menu entry, tab
          text       an exact run of text you are reading out or referring to
          region     a panel, toolbar, section, sidebar, or group of controls
          window     an entire application window, when the answer is about the application itself
          path       a movement such as a drag or a menu traversal, given as a "points" list
          offscreen  something real that is not visible on screen right now
        Do not ask for a shape. Metis chooses how to draw the mark from the target's real size on screen.

        Choose the scope that matches the subject of your sentence, not the nearest clickable thing.
        If the user asks what the black window full of text is, the subject is the terminal itself, so the
        scope is "window" and the annotation covers the whole window — pointing at one button inside it
        answers a question nobody asked. If you are explaining what a particular line or phrase means, the
        scope is "text" and you give the exact words. If you are naming a toolbar, the scope is "region".

        With the annotation give:
          x, y      normalized 0-1000 coordinates of the centre of the target
          w, h      its width and height in the same space, whenever the extent is visible
          element   the target's on-screen name, so Metis can snap the mark to the real control
          text      for scope "text", the exact words, so the mark lands on them rather than near them
          label     two or three words naming what is marked
        Coordinates are your estimate; element and text are what let Metis correct it against the real
        screen, so give them whenever you can.

        When the user asks where something is — "show me", "where is", "which button", "where do I type" —
        an annotation is the answer. Say the sentence too, but never send the sentence on its own.
        """;

    public static string BuildInstruction(AssistanceIntent intent) => intent switch
    {
        AssistanceIntent.TakeControl => """
            assistance_intent: TAKE CONTROL. The user handed this task over, so perform it.
            Work in small verified batches: act, look at the fresh screen, then continue. Keep spoken_text to a short progress note in plain language.
            Say what you are doing as you do it, so the user can follow along and stop you.
            Stop and ask before anything destructive, irreversible, financial, or security-sensitive; speed never overrides that.
            Leave creative and consequential choices to the user rather than guessing at them.
            Track what you did so you can answer "show me what you did" afterwards.

            You can change system settings, drivers, and services. Do it through the interface whenever there is one:
            open Settings, Device Manager, or the program's own dialog and operate it with clicks and typing, exactly as
            the user would. That route is visible while it happens, undoable afterwards, and lets the user follow along.
            Only when a thing genuinely has no interface — or the interface cannot reach it — return a run_command action
            with a single PowerShell command in "command". Metis shows every command to the user and runs nothing they
            have not agreed to, so write the shortest command that does the one job and never chain several together.
            Prefer reading state with a command over changing it, and prefer changing it through a dialog over a command.
            """,
        _ => """
            assistance_intent: TEACH. The user performs every step; you show them where and explain why.
            Do not click, type, press keys, or open apps or URLs even if it would be quicker; annotate the screen and let the user act.
            Explain in plain language using the names visible on their screen, and adapt the depth to the skills listed in user_skills.
            When the screen shows a mistake, explain the cause before the correction.

            Teach as a sequence the user works through, not as one block of prose. Return a "steps" array, in order, each with:
              instruction  what the user should do, one action, in plain words
              why          why this step matters, one short sentence
              done_when    what the screen looks like once this step has worked, in one short phrase
              scope        what the annotation for this step is about, from the list above
              x, y         normalized 0-1000 coordinates of the centre of the target, omitted when not visible
              w, h         its width and height in the same space
              element      the target's on-screen name, so the mark can snap to the real control
              text         for scope "text", the exact words being referred to
              to_x, to_y   only for a movement such as a drag: where the gesture ends
              label        two or three words naming the target
            Keep each step to a single action, and prefer more small steps to fewer large ones.
            Metis reads each step out and marks the screen for it, then moves to the next after a few seconds without
            waiting for the user to have done it — so write the steps as a walkthrough someone can listen to while they
            work, each one following on from the last, rather than as instructions that assume the previous one is
            already finished. Metis re-reads the screen between steps, so later coordinates are corrected against
            whatever is actually there by then.
            Put only the first step's explanation in spoken_text; the rest are read out as Metis reaches them.
            """
    };
}
