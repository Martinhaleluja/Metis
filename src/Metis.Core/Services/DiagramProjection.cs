using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Turns a diagram's normalized 0-1000 coordinates into screen pixels.
///
/// Separate from <see cref="CaptureProjection"/> for one reason that matters:
/// that one scales X and Y independently, against the width and height of
/// whatever window was captured. For a real button that is right — the button
/// genuinely has the window's aspect ratio. For a shape Metis invented it is
/// wrong, and visibly so: an independent scale onto a 16:9 screen turns every
/// circle into an ellipse and every regular hexagon into a squashed one. Here
/// one scale factor serves both axes, so the shape keeps its proportions
/// wherever it lands.
/// </summary>
public static class DiagramProjection
{
    public static (int X, int Y) ToScreenPoint(int normalizedX, int normalizedY, DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return (
            canvas.Left + (int)Math.Round(normalizedX / 1000d * canvas.Side),
            canvas.Top + (int)Math.Round(normalizedY / 1000d * canvas.Side));
    }

    /// <summary>
    /// A normalized length in screen pixels — a radius, an amplitude. Uses the
    /// same single scale as <see cref="ToScreenPoint"/>, which is what keeps a
    /// radius meaning the same thing in both directions.
    /// </summary>
    public static int ToScreenLength(int normalizedLength, DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return (int)Math.Round(normalizedLength / 1000d * canvas.Side);
    }

    public static IReadOnlyList<GuidancePoint> ToScreenPoints(
        IReadOnlyList<(int X, int Y)> normalizedPoints,
        DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(normalizedPoints);
        ArgumentNullException.ThrowIfNull(canvas);

        var points = new List<GuidancePoint>(normalizedPoints.Count);
        foreach (var (x, y) in normalizedPoints)
        {
            var (screenX, screenY) = ToScreenPoint(x, y, canvas);
            points.Add(new GuidancePoint(screenX, screenY));
        }

        return points;
    }
}
