using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Turns the raw points a hand produces into a path worth drawing and worth
/// sending. Mouse input arrives jittery and unevenly spaced — fast drags leave
/// long gaps, slow ones pile up dozens of points in the same place — so it is
/// resampled to even spacing before anything else looks at it.
/// </summary>
public static class TracePath
{
    /// <summary>
    /// Movement below this is a click, not a trace. It keeps a shaky hand from
    /// turning a tap on a button into an accidental lasso.
    /// </summary>
    public const int TapThreshold = 20;

    /// <summary>Spacing for resampled points, in screen pixels.</summary>
    public const int ResampleSpacing = 9;

    /// <summary>
    /// A cap so a long slow drag cannot produce a path with thousands of
    /// points, which would cost geometry work on every frame.
    /// </summary>
    public const int MaxPoints = 220;

    /// <summary>
    /// Whether the gesture travelled far enough to count as tracing an area
    /// rather than pointing at one spot.
    /// </summary>
    public static bool IsTrace(IReadOnlyList<GuidancePoint> points)
    {
        if (points is null || points.Count < 3)
        {
            return false;
        }

        var (minX, minY, maxX, maxY) = Extent(points);
        return maxX - minX >= TapThreshold || maxY - minY >= TapThreshold;
    }

    /// <summary>
    /// Resamples to even spacing and drops duplicates. The result is what gets
    /// drawn and what gets measured; the raw input is never used directly.
    /// </summary>
    public static IReadOnlyList<GuidancePoint> Smooth(IReadOnlyList<GuidancePoint> points)
    {
        if (points is null || points.Count < 2)
        {
            return points ?? [];
        }

        var output = new List<GuidancePoint> { points[0] };

        for (var index = 1; index < points.Count; index++)
        {
            // Distance is measured from the last point actually kept, so a
            // point is emitted exactly once per spacing interval. Accumulating
            // these distances as well would count the same gap repeatedly and
            // emit points far closer together than the spacing asks for.
            var from = output[^1];
            var to = points[index];
            var dx = to.ScreenX - from.ScreenX;
            var dy = to.ScreenY - from.ScreenY;
            if ((dx * dx) + (dy * dy) < ResampleSpacing * ResampleSpacing)
            {
                continue;
            }

            output.Add(to);
            if (output.Count >= MaxPoints)
            {
                break;
            }
        }

        // Always keep the final point: it is where the user let go, and losing
        // it leaves a visible gap before the loop closes.
        var last = points[^1];
        if (output[^1] != last && output.Count < MaxPoints)
        {
            output.Add(last);
        }

        return output;
    }

    /// <summary>
    /// The bounding box of a path in screen pixels, padded outward so a mark
    /// sits just outside what was circled rather than on top of it.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Bounds(
        IReadOnlyList<GuidancePoint> points,
        int padding = 0)
    {
        if (points is null || points.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var (minX, minY, maxX, maxY) = Extent(points);
        return (
            minX - padding,
            minY - padding,
            Math.Max(1, maxX - minX + (padding * 2)),
            Math.Max(1, maxY - minY + (padding * 2)));
    }

    /// <summary>
    /// The centre of the traced region, which is where a mark or the companion
    /// is aimed.
    /// </summary>
    public static GuidancePoint Centre(IReadOnlyList<GuidancePoint> points)
    {
        var (x, y, width, height) = Bounds(points);
        return new GuidancePoint(x + (width / 2), y + (height / 2));
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) Extent(IReadOnlyList<GuidancePoint> points)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var point in points)
        {
            minX = Math.Min(minX, point.ScreenX);
            minY = Math.Min(minY, point.ScreenY);
            maxX = Math.Max(maxX, point.ScreenX);
            maxY = Math.Max(maxY, point.ScreenY);
        }

        return (minX, minY, maxX, maxY);
    }
}
