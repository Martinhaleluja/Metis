namespace Metis.Core.Models;

/// <summary>
/// One thing the learner has to do themselves. A lesson step is deliberately
/// not a <see cref="DesktopAction"/>: Metis never performs these, it shows
/// where they are and waits. <paramref name="DoneWhen"/> is what makes the
/// waiting possible — it is the visible evidence Metis looks for on the screen
/// to decide the learner has finished.
/// </summary>
public sealed record LessonStep(
    string Instruction,
    string? Why = null,
    string? DoneWhen = null,
    int TargetX = -1,
    int TargetY = -1,
    string? TargetLabel = null,

    /// <summary>
    /// What the annotation for this step is about — a control, a window, a run
    /// of text. The shape is derived from it and the target's measured size, so
    /// this names the subject rather than the drawing.
    /// </summary>
    string? ScopeName = null,

    /// <summary>
    /// The control's size in the same normalized 0-1000 space as the target, so
    /// the highlight can take its shape rather than ringing a fixed circle.
    /// </summary>
    int TargetWidth = 0,
    int TargetHeight = 0,

    /// <summary>
    /// Where the gesture ends, for steps that are a movement rather than a
    /// click: a drag, a swipe, a menu traversal. The ghost cursor walks from
    /// the target to here.
    /// </summary>
    int DragToX = -1,
    int DragToY = -1,

    /// <summary>The target's on-screen name, used to snap the mark to the real control.</summary>
    string? ElementName = null,

    /// <summary>The exact words, when the step is about a run of text.</summary>
    string? Text = null,

    /// <summary>
    /// The shape to draw for this step, when the step is teaching an idea
    /// rather than pointing at software.
    ///
    /// Deliberately its own set of fields rather than reusing
    /// <paramref name="TargetX"/> and the rest. Those coordinates mean "find
    /// this on the real screen", and everything downstream treats them that
    /// way — a diagram borrowing them would send Metis hunting through the
    /// accessibility tree for whatever control happened to sit under an
    /// invented triangle, and mark that instead. Keeping the two vocabularies
    /// apart is what makes that impossible rather than merely discouraged.
    /// </summary>
    string? DiagramShapeKind = null,

    /// <summary>Centre of a polygon or circle, or the start of a line, arrow or wave.</summary>
    int DiagramCenterX = -1,
    int DiagramCenterY = -1,

    /// <summary>Where a line, arrow or wave ends.</summary>
    int DiagramEndX = -1,
    int DiagramEndY = -1,

    /// <summary>Radius for a polygon or circle; amplitude for a wave.</summary>
    int DiagramSize = 0,

    /// <summary>Sides of a polygon, or cycles in a wave.</summary>
    int DiagramSides = 0,

    /// <summary>How far a polygon is turned from its default upright position.</summary>
    int DiagramRotationDegrees = 0)
{
    public bool HasTarget => TargetX >= 0 && TargetY >= 0;

    /// <summary>
    /// True when the step says which control it means, even without saying
    /// where it is.
    ///
    /// A name outlives a coordinate. The screen moves on as the learner works,
    /// but "the Record button" still means the Record button, so a named step
    /// can be placed against the screen as it is now.
    /// </summary>
    public bool HasNamedTarget =>
        !string.IsNullOrWhiteSpace(ElementName) || !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// True when this step draws something Metis is explaining rather than
    /// marking something that is already on screen.
    /// </summary>
    public bool HasDiagram =>
        DiagramShapeKinds.Parse(DiagramShapeKind) is not Models.DiagramShapeKind.None;

    /// <summary>The shape this step draws, if any.</summary>
    public DiagramShapeKind Diagram => DiagramShapeKinds.Parse(DiagramShapeKind);

    public bool HasShape => TargetWidth > 0 && TargetHeight > 0;

    /// <summary>
    /// True when this step is a movement the user has to be shown rather than
    /// a single place they have to be pointed at.
    /// </summary>
    public bool HasGesture => HasTarget && DragToX >= 0 && DragToY >= 0;

    public AnnotationScope Scope => AnnotationScopes.Parse(ScopeName);

    /// <summary>
    /// The step's annotation, before Metis has looked at the screen to find out
    /// where its subject really is.
    /// </summary>
    public AnnotationTarget ToAnnotationTarget() => new(
        Scope,
        TargetX,
        TargetY,
        TargetWidth,
        TargetHeight,
        TargetLabel,
        ElementName,
        Text);
}

public enum LessonStatus
{
    Showing,
    Waiting,
    Complete,
    Abandoned
}

/// <summary>
/// A lesson in progress. Metis holds the whole sequence so it can keep the
/// goal in view while working through one step at a time, and so it can repeat
/// or re-explain a step without losing its place.
/// </summary>
public sealed record LessonState(
    string Goal,
    IReadOnlyList<LessonStep> Steps,
    int CurrentIndex = 0,
    LessonStatus Status = LessonStatus.Showing,
    int AttemptsOnCurrentStep = 0)
{
    public LessonStep? Current =>
        CurrentIndex >= 0 && CurrentIndex < Steps.Count ? Steps[CurrentIndex] : null;

    public bool IsFinished => CurrentIndex >= Steps.Count || Status is LessonStatus.Complete or LessonStatus.Abandoned;

    public int StepNumber => Math.Min(CurrentIndex + 1, Steps.Count);

    public LessonState Advance() => this with
    {
        CurrentIndex = CurrentIndex + 1,
        AttemptsOnCurrentStep = 0,
        Status = CurrentIndex + 1 >= Steps.Count ? LessonStatus.Complete : LessonStatus.Showing
    };

    public LessonState Waiting() => this with { Status = LessonStatus.Waiting };

    /// <summary>
    /// Records another unsuccessful check of the current step. The count is what
    /// lets Metis escalate from a hint to a fuller re-explanation instead of
    /// repeating itself identically.
    /// </summary>
    public LessonState Retry() => this with
    {
        AttemptsOnCurrentStep = AttemptsOnCurrentStep + 1,
        Status = LessonStatus.Waiting
    };
}
