namespace Metis.Core.Models;

/// <summary>
/// What kind of thing Metis is explaining. This is the one judgement the model
/// is asked to make about a mark, and it is deliberately not a shape.
///
/// Asking a model which shape to draw produces rings around everything: a ring
/// is the safe answer, so it becomes the only answer. Asking it what it is
/// talking about is a question about meaning, which is what models are actually
/// good at — and the shape then follows from the target's real geometry, which
/// Windows knows exactly and the model can only estimate.
/// </summary>
public enum AnnotationScope
{
    /// <summary>
    /// One thing the user can press: a button, icon, checkbox, menu entry, tab.
    /// </summary>
    Control,

    /// <summary>
    /// An exact run of text being read out or referred to. Marked under the
    /// words themselves, because the point of the mark is "these words", and a
    /// box around a line of prose says "this area" instead.
    /// </summary>
    TextSpan,

    /// <summary>
    /// A group of controls that belong together: a toolbar, a panel, a section
    /// of a dialog, a column of a table.
    /// </summary>
    Region,

    /// <summary>
    /// An entire application window. This is the answer to "what is this?" —
    /// pointing at one control inside a terminal to explain that the terminal is
    /// where commands are typed marks the wrong thing entirely, because the
    /// subject of the sentence is the window.
    /// </summary>
    Window,

    /// <summary>
    /// A movement rather than a place: a drag, a menu traversal, a swipe. There
    /// is nothing to ring, so the mark is the path itself.
    /// </summary>
    Path,

    /// <summary>
    /// Something real but not currently visible — behind another window, below
    /// the fold, on the other monitor. An arrow from the edge is honest about
    /// direction without claiming a position that is not on screen.
    /// </summary>
    Offscreen
}

/// <summary>
/// Where a resolved annotation's geometry came from. Kept because the two
/// sources fail in completely different ways: an element rect is exact or
/// absent, whereas a model estimate is always present and sometimes wrong, and
/// a mark that silently drifted forty pixels is the one failure the user reads
/// as Metis not knowing what it is talking about.
/// </summary>
public enum AnnotationSource
{
    /// <summary>The model's normalized estimate, used because nothing better was found.</summary>
    Estimated,

    /// <summary>Snapped to a real control's bounds from the accessibility tree.</summary>
    Element,

    /// <summary>Taken from a real window's rectangle.</summary>
    Window,

    /// <summary>Taken from the bounding rectangle of an actual run of text.</summary>
    Text,

    /// <summary>Drawn by the user, so correct by definition.</summary>
    Traced
}

/// <summary>
/// What the model asked to be marked, before Metis has looked at the screen to
/// find out where that thing really is. Coordinates are in the normalized
/// 0-1000 capture space; the hints are what let the resolver do better than
/// them.
/// </summary>
public sealed record AnnotationTarget(
    AnnotationScope Scope,
    int NormalizedX = -1,
    int NormalizedY = -1,
    int NormalizedWidth = 0,
    int NormalizedHeight = 0,
    string? Label = null,

    /// <summary>
    /// The control's name as it appears on screen, used to look the element up
    /// in the accessibility tree when the estimated point misses it.
    /// </summary>
    string? ElementName = null,

    /// <summary>
    /// The exact text of a <see cref="AnnotationScope.TextSpan"/>, matched
    /// against what is really on screen so the underline lands on the words
    /// rather than near them.
    /// </summary>
    string? Text = null)
{
    public bool HasPoint => NormalizedX >= 0 && NormalizedY >= 0;

    public bool HasExtent => NormalizedWidth > 0 && NormalizedHeight > 0;
}

/// <summary>
/// An annotation after resolution: a real rectangle in screen pixels, the mark
/// chosen for it, and where the rectangle came from.
/// </summary>
public sealed record ResolvedAnnotation(
    AnnotationScope Scope,
    GuidanceMarkKind Mark,
    int ScreenX,
    int ScreenY,
    int Width,
    int Height,
    string? Label,
    AnnotationSource Source,
    IReadOnlyList<GuidancePoint>? Points = null)
{
    /// <summary>
    /// Turns the annotation into the mark the overlay draws. The overlay stays
    /// unaware of scopes and sources: it is a drawing surface, and keeping the
    /// judgement out of it is what allows the rules to be tested without one.
    /// </summary>
    public GuidanceMark ToMark(int stepNumber = 0) =>
        new(Mark, ScreenX, ScreenY, Width, Height, Label, stepNumber, Points);
}

/// <summary>
/// Names for the scopes, for reading a model's answer. Kept beside the enum so
/// a new scope cannot be added without deciding what the model may call it.
/// </summary>
public static class AnnotationScopes
{
    public static AnnotationScope Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "window" or "app" or "application" or "program" => AnnotationScope.Window,
        "text" or "textspan" or "text_span" or "words" or "line" => AnnotationScope.TextSpan,
        "region" or "panel" or "area" or "toolbar" or "section" or "group" => AnnotationScope.Region,
        "path" or "gesture" or "drag" or "movement" => AnnotationScope.Path,
        "offscreen" or "off_screen" or "hidden" or "elsewhere" => AnnotationScope.Offscreen,
        _ => AnnotationScope.Control
    };

    public static string Name(AnnotationScope scope) => scope switch
    {
        AnnotationScope.Window => "window",
        AnnotationScope.TextSpan => "text",
        AnnotationScope.Region => "region",
        AnnotationScope.Path => "path",
        AnnotationScope.Offscreen => "offscreen",
        _ => "control"
    };
}
