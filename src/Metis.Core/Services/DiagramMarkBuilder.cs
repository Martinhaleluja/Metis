using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Turns a lesson step's shape description into the mark the overlay draws.
///
/// The counterpart to <see cref="AnnotationDirector"/>: that one decides how to
/// mark something real once Windows has said where it is, this one decides how
/// to draw something that exists nowhere but on the canvas. Both leave the
/// overlay itself knowing nothing about scopes, subjects or shapes — it
/// receives points and a kind, and draws them.
/// </summary>
public static class DiagramMarkBuilder
{
    /// <summary>
    /// Fallbacks for a model that names a shape and then leaves out its
    /// numbers. Drawing a sensibly-sized shape in the middle of the canvas is a
    /// better answer than drawing nothing, and the narration still carries the
    /// lesson either way.
    /// </summary>
    private const int DefaultCentre = 500;
    private const int DefaultRadius = 300;
    private const int DefaultSides = 3;
    private const int DefaultWaveCycles = 3;
    private const int DefaultWaveAmplitude = 120;

    public static GuidanceMark? Build(LessonStep step, DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(canvas);

        var kind = step.Diagram;
        if (kind == DiagramShapeKind.None)
        {
            return null;
        }

        var centreX = Normalize(step.DiagramCenterX, DefaultCentre);
        var centreY = Normalize(step.DiagramCenterY, DefaultCentre);
        var label = step.TargetLabel ?? step.Text;

        return kind switch
        {
            DiagramShapeKind.Label => BuildLabel(centreX, centreY, label, canvas),
            DiagramShapeKind.Line or DiagramShapeKind.Arrow => BuildSegment(step, kind, centreX, centreY, label, canvas),
            DiagramShapeKind.Wave => BuildWave(step, centreX, centreY, label, canvas),
            DiagramShapeKind.Circle => BuildRing(step, centreX, centreY, label, canvas, smooth: true),
            _ => BuildRing(step, centreX, centreY, label, canvas, smooth: false)
        };
    }

    private static GuidanceMark BuildLabel(int centreX, int centreY, string? label, DiagramCanvas canvas)
    {
        var (x, y) = DiagramProjection.ToScreenPoint(centreX, centreY, canvas);
        return new GuidanceMark(GuidanceMarkKind.Label, x, y, Label: label, Persistent: true);
    }

    private static GuidanceMark BuildSegment(
        LessonStep step,
        DiagramShapeKind kind,
        int startX,
        int startY,
        string? label,
        DiagramCanvas canvas)
    {
        // A segment with no end point still has to go somewhere, so it runs to
        // the centre of the canvas — which for an arrow reads as pointing at
        // whatever is already drawn there.
        var endX = Normalize(step.DiagramEndX, DefaultCentre);
        var endY = Normalize(step.DiagramEndY, DefaultCentre);

        var points = DiagramProjection.ToScreenPoints([(startX, startY), (endX, endY)], canvas);
        var (screenX, screenY) = DiagramProjection.ToScreenPoint(
            (startX + endX) / 2,
            (startY + endY) / 2,
            canvas);

        // An arrow already draws itself with a head; a plain line is a stroke.
        var mark = kind == DiagramShapeKind.Arrow ? GuidanceMarkKind.Arrow : GuidanceMarkKind.Stroke;
        return new GuidanceMark(
            mark,
            screenX,
            screenY,
            Label: label,
            Points: points,
            StraightEdges: true,
            Persistent: true);
    }

    private static GuidanceMark BuildWave(
        LessonStep step,
        int startX,
        int startY,
        string? label,
        DiagramCanvas canvas)
    {
        var endX = Normalize(step.DiagramEndX, 1000 - startX);
        var endY = Normalize(step.DiagramEndY, startY);
        var amplitude = step.DiagramSize > 0 ? step.DiagramSize : DefaultWaveAmplitude;
        var cycles = step.DiagramSides > 0 ? step.DiagramSides : DefaultWaveCycles;

        var normalized = DiagramGeometry.WavePoints(startX, startY, endX, endY, cycles, amplitude);
        var points = DiagramProjection.ToScreenPoints(normalized, canvas);
        var (screenX, screenY) = DiagramProjection.ToScreenPoint(
            (startX + endX) / 2,
            (startY + endY) / 2,
            canvas);

        // A stroke, not a polygon: a wave is an open curve. Closing it would
        // run a line back from the last crest to the first and tint the inside.
        return new GuidanceMark(
            GuidanceMarkKind.Stroke,
            screenX,
            screenY,
            Label: label,
            Points: points,
            StraightEdges: false,
            Persistent: true);
    }

    private static GuidanceMark BuildRing(
        LessonStep step,
        int centreX,
        int centreY,
        string? label,
        DiagramCanvas canvas,
        bool smooth)
    {
        var radius = step.DiagramSize > 0 ? step.DiagramSize : DefaultRadius;
        var sides = step.DiagramSides > 0 ? step.DiagramSides : DefaultSides;

        var normalized = smooth
            ? DiagramGeometry.CirclePoints(centreX, centreY, radius)
            : DiagramGeometry.RegularPolygonPoints(centreX, centreY, radius, sides, step.DiagramRotationDegrees);

        var points = DiagramProjection.ToScreenPoints(normalized, canvas);
        var (screenX, screenY) = DiagramProjection.ToScreenPoint(centreX, centreY, canvas);
        var diameter = DiagramProjection.ToScreenLength(radius * 2, canvas);

        return new GuidanceMark(
            GuidanceMarkKind.Polygon,
            screenX,
            screenY,
            diameter,
            diameter,
            label,
            Points: points,
            StraightEdges: !smooth,
            Persistent: true);
    }

    private static int Normalize(int value, int fallback) =>
        value is >= 0 and <= 1000 ? value : fallback;
}
