namespace Metis.Core.Services;

/// <summary>
/// The vertices behind each drawable shape, in the normalized 0-1000 space.
///
/// Pure trigonometry, no screen and no drawing surface: the same points are
/// used to reason about a shape in tests as to render it, and the projection
/// onto real pixels happens once, afterwards, in <see cref="DiagramProjection"/>.
/// </summary>
public static class DiagramGeometry
{
    /// <summary>How many segments approximate a circle. Enough to read as round.</summary>
    public const int CircleSegments = 40;

    /// <summary>Samples per wave cycle. Enough for the curve to stay smooth.</summary>
    private const int WaveSamplesPerCycle = 24;

    private const int MinimumSides = 3;
    private const int MaximumSides = 12;

    /// <summary>
    /// A regular polygon around a centre.
    ///
    /// The first vertex sits at the top by default rather than to the right,
    /// because a triangle drawn point-up is the one people recognise; the
    /// mathematical convention of starting at zero degrees would draw every
    /// shape rotated a sixth of a turn from how a textbook prints it.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> RegularPolygonPoints(
        int centreX,
        int centreY,
        int radius,
        int sides,
        int rotationDegrees = 0)
    {
        var count = Math.Clamp(sides, MinimumSides, MaximumSides);
        var points = new List<(int X, int Y)>(count);
        var start = (-Math.PI / 2) + (rotationDegrees * Math.PI / 180d);

        for (var index = 0; index < count; index++)
        {
            var angle = start + (2 * Math.PI * index / count);
            points.Add((
                (int)Math.Round(centreX + (radius * Math.Cos(angle))),
                (int)Math.Round(centreY + (radius * Math.Sin(angle)))));
        }

        return points;
    }

    /// <summary>
    /// A circle, as enough points that the renderer's curve fit reads as round.
    /// Deliberately the same shape of data as a polygon, so both travel through
    /// one drawing path and only differ in whether the edges are smoothed.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> CirclePoints(int centreX, int centreY, int radius)
    {
        var points = new List<(int X, int Y)>(CircleSegments);
        for (var index = 0; index < CircleSegments; index++)
        {
            var angle = 2 * Math.PI * index / CircleSegments;
            points.Add((
                (int)Math.Round(centreX + (radius * Math.Cos(angle))),
                (int)Math.Round(centreY + (radius * Math.Sin(angle)))));
        }

        return points;
    }

    /// <summary>
    /// A sine wave running from one point to another, bulging perpendicular to
    /// that line. Given as a start and an end rather than a box so a wave can
    /// run at any angle — light arriving at a surface is rarely horizontal.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> WavePoints(
        int startX,
        int startY,
        int endX,
        int endY,
        int cycles,
        int amplitude)
    {
        var cycleCount = Math.Clamp(cycles, 1, 8);
        var samples = cycleCount * WaveSamplesPerCycle;

        var spanX = (double)(endX - startX);
        var spanY = (double)(endY - startY);
        var length = Math.Sqrt((spanX * spanX) + (spanY * spanY));
        if (length <= 0)
        {
            return [(startX, startY)];
        }

        // The unit normal to the axis. The wave rides along the line and swings
        // across it, so it stays readable whichever way the line points.
        var normalX = -spanY / length;
        var normalY = spanX / length;

        var points = new List<(int X, int Y)>(samples + 1);
        for (var index = 0; index <= samples; index++)
        {
            var travel = (double)index / samples;
            var swing = Math.Sin(travel * cycleCount * 2 * Math.PI) * amplitude;
            points.Add((
                (int)Math.Round(startX + (spanX * travel) + (normalX * swing)),
                (int)Math.Round(startY + (spanY * travel) + (normalY * swing))));
        }

        return points;
    }
}
