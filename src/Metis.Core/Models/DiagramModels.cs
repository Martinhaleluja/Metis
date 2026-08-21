namespace Metis.Core.Models;

/// <summary>
/// The shapes Metis can draw to explain an idea, as opposed to the marks it
/// draws to point at something real.
///
/// The vocabulary is deliberately small and fixed. Every entry has to survive
/// the round trip through the provider's response schema, and that schema is
/// already close to a complexity budget Gemini does not document — so this is a
/// handful of primitives with a few numbers each, rather than an open-ended
/// drawing language. A triangle, a circle, an arrow and a wave cover the
/// diagrams that carry most of school maths, physics and biology; anything more
/// elaborate is built by drawing several of them in sequence.
/// </summary>
public enum DiagramShapeKind
{
    /// <summary>Not a diagram step at all.</summary>
    None,

    /// <summary>A regular polygon: triangle, square, pentagon, hexagon.</summary>
    Polygon,

    /// <summary>A circle, for cells, orbits, and anything round.</summary>
    Circle,

    /// <summary>A plain line between two points — an axis, an edge, a divider.</summary>
    Line,

    /// <summary>A line with a head. Forces, velocities, flow, direction.</summary>
    Arrow,

    /// <summary>A sine wave, for oscillation, light, and sound.</summary>
    Wave,

    /// <summary>Words on the canvas, naming a part of what is already drawn.</summary>
    Label
}

/// <summary>
/// Names for the shapes, for reading a model's answer. Kept beside the enum so
/// a new shape cannot be added without deciding what the model may call it.
/// </summary>
public static class DiagramShapeKinds
{
    public static DiagramShapeKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "polygon" or "triangle" or "square" or "rectangle" or "pentagon" or "hexagon" or "shape"
            => DiagramShapeKind.Polygon,
        "circle" or "ellipse" or "oval" or "round" => DiagramShapeKind.Circle,
        "line" or "segment" or "edge" or "axis" => DiagramShapeKind.Line,
        "arrow" or "vector" or "force" or "flow" => DiagramShapeKind.Arrow,
        "wave" or "sine" or "oscillation" or "curve" => DiagramShapeKind.Wave,
        "label" or "text" or "caption" or "annotate" => DiagramShapeKind.Label,
        _ => DiagramShapeKind.None
    };

    public static string Name(DiagramShapeKind kind) => kind switch
    {
        DiagramShapeKind.Polygon => "polygon",
        DiagramShapeKind.Circle => "circle",
        DiagramShapeKind.Line => "line",
        DiagramShapeKind.Arrow => "arrow",
        DiagramShapeKind.Wave => "wave",
        DiagramShapeKind.Label => "label",
        _ => "none"
    };
}

/// <summary>
/// Where on screen a diagram is drawn.
///
/// Square on purpose. Normalized coordinates map onto it with one scale factor
/// for both axes, so a circle stays a circle and a hexagon stays regular —
/// unlike a real annotation, which is stretched to whatever aspect the window
/// it is marking happens to have.
/// </summary>
public sealed record DiagramCanvas(int Left, int Top, int Side)
{
    /// <summary>
    /// A canvas centred on the given screen bounds, taking the given share of
    /// the shorter side so the whole shape fits on one monitor with room around
    /// it for labels.
    /// </summary>
    public static DiagramCanvas Centred(int screenLeft, int screenTop, int screenWidth, int screenHeight, double share)
    {
        var side = (int)Math.Round(Math.Min(screenWidth, screenHeight) * Math.Clamp(share, 0.1, 1.0));
        return new DiagramCanvas(
            screenLeft + ((screenWidth - side) / 2),
            screenTop + ((screenHeight - side) / 2),
            Math.Max(1, side));
    }
}
