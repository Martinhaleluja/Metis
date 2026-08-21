namespace Metis.Core.Models;

/// <summary>
/// One answer from the model: what to say, what to mark on screen, and the
/// steps to walk the learner through.
///
/// Metis does not operate the computer. It used to carry an actions list here —
/// clicks, typing, commands to run — and executing them was a second product
/// bolted onto the first. Removing it is what makes this one honest: everything
/// Metis produces now is something the user reads, hears, or is shown, and the
/// user is the only one who touches their machine.
/// </summary>
public sealed record AssistantPlan(
    string SpokenText,
    string? BubbleCue,
    bool ScreenObserved = false,
    string? Goal = null,
    IReadOnlyList<LessonStep>? Steps = null,

    /// <summary>
    /// What this reply's annotation is about — a control, a window, a run of
    /// text. Named rather than drawn: the shape follows from the subject and
    /// its measured size on screen.
    /// </summary>
    string? ScopeName = null,

    /// <summary>The target's on-screen name, used to snap the mark to the real control.</summary>
    string? ElementName = null,

    /// <summary>The exact words, when the reply is about a run of text.</summary>
    string? AnnotationText = null,

    /// <summary>
    /// Where the mark goes, in the normalized 0-1000 space of the screenshot.
    ///
    /// These sat on a pointer action until Metis stopped having actions at all.
    /// A mark needs somewhere to go whether or not anything is ever pressed, so
    /// the coordinates belong to the annotation rather than to a movement that
    /// no longer happens.
    /// </summary>
    int NormalizedX = -1,
    int NormalizedY = -1,
    int NormalizedWidth = 0,
    int NormalizedHeight = 0,

    /// <summary>Two or three words naming what is marked.</summary>
    string? Label = null,

    /// <summary>
    /// What the user actually said, when the request reached the model as audio
    /// rather than as text.
    ///
    /// Metis reads what was asked from these words itself. The model reports
    /// what it heard, which is a fact about the recording, and nothing more.
    /// </summary>
    string? HeardText = null)
{
    public static AssistantPlan SpeechOnly(string text) => new(text, null);

    /// <summary>The steps for the learner to work through themselves.</summary>
    public IReadOnlyList<LessonStep> LessonSteps => Steps ?? [];

    public AnnotationScope Scope => AnnotationScopes.Parse(ScopeName);

    /// <summary>True when the reply said where on screen it was talking about.</summary>
    public bool HasAnnotation => NormalizedX >= 0 && NormalizedY >= 0;

    /// <summary>
    /// The annotation for this reply, combining what it said it was about with
    /// where it said to look.
    /// </summary>
    public AnnotationTarget ToAnnotationTarget() => new(
        Scope,
        NormalizedX,
        NormalizedY,
        NormalizedWidth,
        NormalizedHeight,
        Label,
        ElementName ?? Label,
        AnnotationText);
}

/// <summary>
/// A control located through the accessibility tree, in screen pixels. Its
/// bounds come from Windows itself rather than from a model's estimate, so a
/// highlight built from one wraps the control exactly.
/// </summary>
public sealed record LocatedElement(
    string Name,
    string ControlType,
    int ScreenX,
    int ScreenY,
    int Width,
    int Height,
    string? AutomationId = null);

/// <summary>
/// A control found by searching the accessibility tree for a description,
/// rather than by trusting a coordinate.
///
/// This is how Metis can still point at something when the model answered in
/// prose without saying where to look: Windows is asked where the control
/// really is, and the mark takes its true rectangle.
/// </summary>
public sealed record UiElementHit(
    string Name,
    string ControlType,
    int ScreenX,
    int ScreenY,
    int Width,
    int Height);
