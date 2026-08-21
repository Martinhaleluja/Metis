namespace Metis.Core.Models;

/// <summary>
/// Where the pointer was when the user activated Metis, plus the control it was
/// over. This is what lets a question like "what does this do?" resolve without
/// the user describing the screen.
/// </summary>
public sealed record PointerContext(
    int ScreenX,
    int ScreenY,
    int NormalizedX,
    int NormalizedY,
    string? HoveredElement = null);

/// <summary>
/// A temporary mark drawn over the desktop. Overlay marks never alter the
/// target application; they are click-through and expire on their own.
/// </summary>
public enum GuidanceMarkKind
{
    FocusRing,
    Box,

    /// <summary>
    /// A hand-drawn curved arrow that draws itself toward the target and then
    /// fades. Used both when the user points during Inspect and when Metis
    /// points something out, so the same gesture means the same thing in both
    /// directions.
    /// </summary>
    Arrow,
    Label,
    NumberedStep,

    /// <summary>
    /// A marker stroke drawn under or across a region, for pointing at text,
    /// a row, or a span rather than a single control.
    /// </summary>
    Underline,

    /// <summary>
    /// A freehand stroke through a list of points: a line, a curve, a shape, or
    /// a path traced across whatever is on screen. This is what lets Metis
    /// explain visually — circling a term, connecting two things, sketching a
    /// triangle over a diagram — instead of only pointing at controls.
    /// </summary>
    Stroke,

    /// <summary>
    /// A rounded outline hugging a wide, short target — a search field, a table
    /// row, a menu entry. A ring around one of these either misses its ends or
    /// swallows everything around it.
    /// </summary>
    Capsule,

    /// <summary>
    /// Four corner marks around a large region. A full outline at this size
    /// reads as a border and visually swallows the content it is meant to draw
    /// attention to; brackets mark the same area and leave it clear.
    /// </summary>
    Bracket,

    /// <summary>
    /// A closed freehand loop — what the user draws when they trace an area,
    /// and what Metis draws back when the target is irregular.
    /// </summary>
    Lasso,

    /// <summary>
    /// A shape Metis drew to explain something, rather than a mark over
    /// something real. Straight-edged or smooth depending on
    /// <see cref="GuidanceMark.StraightEdges"/>.
    /// </summary>
    Polygon
}

public static class GuidanceMarkKinds
{
    /// <summary>
    /// Maps the name a model chose onto a mark. Metis picks the shape to suit
    /// what it is pointing at — a ring for a button, an arrow for somewhere
    /// off to one side, a box for a panel, an underline for text — so the mark
    /// itself carries meaning instead of everything looking the same.
    /// </summary>
    public static GuidanceMarkKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "arrow" or "point" or "pointer" => GuidanceMarkKind.Arrow,
        "box" or "rectangle" => GuidanceMarkKind.Box,
        "capsule" or "field" or "row" or "bar" => GuidanceMarkKind.Capsule,
        "bracket" or "region" or "panel" or "area" => GuidanceMarkKind.Bracket,
        "lasso" or "loop" or "circle" => GuidanceMarkKind.Lasso,
        "underline" or "text" or "line" or "span" => GuidanceMarkKind.Underline,
        "label" or "note" => GuidanceMarkKind.Label,
        _ => GuidanceMarkKind.FocusRing
    };
}

/// <summary>
/// One overlay mark in screen pixels on the Windows virtual desktop.
/// </summary>
/// <summary>
/// How the user wants to mark out an area. Freehand is the default because it
/// is the most expressive; the others exist because a rectangle is faster when
/// the target is already rectangular, and a whole screen needs no drawing.
/// </summary>
public enum TraceTool
{
    Freehand,
    Rectangle,
    FullScreen
}

/// <summary>One screen point, used for freehand strokes and demo paths.</summary>
public sealed record GuidancePoint(int ScreenX, int ScreenY);

public sealed record GuidanceMark(
    GuidanceMarkKind Kind,
    int ScreenX,
    int ScreenY,
    int Width = 0,
    int Height = 0,
    string? Label = null,
    int StepNumber = 0,

    /// <summary>
    /// The path for a <see cref="GuidanceMarkKind.Stroke"/>. Two points draw a
    /// line, more draw a curve through them.
    /// </summary>
    IReadOnlyList<GuidancePoint>? Points = null,

    /// <summary>
    /// Whether the points are joined by straight lines instead of a curve.
    ///
    /// The renderer smooths a path through its points by default, which is
    /// right for a hand-drawn lasso and wrong for a triangle — a spline would
    /// round the corners off the very thing being taught.
    /// </summary>
    bool StraightEdges = false,

    /// <summary>
    /// Whether the mark stays until it is cleared, rather than fading on its
    /// own. A pointer annotation has said what it needs to within a couple of
    /// seconds; a diagram has to survive the whole explanation, and several
    /// more steps drawn on top of it.
    /// </summary>
    bool Persistent = false)
{
    /// <summary>
    /// True when the mark knows the real size of what it is marking, so the
    /// highlight can take the control's shape instead of a fixed circle.
    /// </summary>
    public bool HasShape => Width > 0 && Height > 0;
}

/// <summary>
/// A movement for the ghost cursor to perform: the companion detaches, walks
/// the path as though a second person were driving the mouse, and returns.
/// This is how Metis shows a gesture — a drag, a menu traversal — that cannot
/// be explained by pointing at one place.
/// </summary>
/// <summary>
/// A request to fly the companion to a point on screen and rest there,
/// holding a short label, so the learner's eye is carried to what is being
/// explained. It marks; it never presses.
/// </summary>
public sealed record CompanionGuidance(
    int ScreenX,
    int ScreenY,
    string Cue,
    TimeSpan HoldDuration);

public sealed record CompanionDemo(
    IReadOnlyList<GuidancePoint> Path,
    string? Label = null,
    bool HoldAtEnd = false)
{
    public bool IsUsable => Path.Count >= 2;
}

/// <summary>
/// An area the user traced by hand, in normalized 0-1000 capture space plus the
/// screen-pixel path that drew it. Unlike a control's bounds guessed by a model,
/// this is exactly right by definition — the user drew it.
/// </summary>
public sealed record ScreenRegion(
    int NormalizedX,
    int NormalizedY,
    int NormalizedWidth,
    int NormalizedHeight,
    IReadOnlyList<GuidancePoint> Path)
{
    public bool IsUsable => NormalizedWidth > 0 && NormalizedHeight > 0;
}

/// <summary>
/// A complete overlay frame. Metis replaces the previous frame rather than
/// stacking marks, so the screen never accumulates stale guidance.
/// </summary>
public sealed record GuidanceOverlayRequest(
    IReadOnlyList<GuidanceMark> Marks,
    bool DimBackground,
    TimeSpan HoldDuration,

    /// <summary>
    /// Whether these marks join what is already drawn instead of replacing it.
    ///
    /// Replacing is right for pointing: the previous mark is stale the moment
    /// Metis moves on. Building a diagram is the opposite — the triangle has to
    /// still be there when its sides get labelled two steps later.
    /// </summary>
    bool Accumulate = false)
{
    public static GuidanceOverlayRequest Clear { get; } = new([], false, TimeSpan.Zero);
}
