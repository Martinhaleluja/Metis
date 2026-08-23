namespace Metis.Core.Services;

/// <summary>
/// Keeps a mark on the part of the screen the user can actually see.
///
/// Everything upstream works in one big coordinate space that spans every
/// monitor, so a point that is perfectly valid there can still land in the gap
/// between two screens, or past the edge of the only one — where a highlight is
/// drawn but nobody can see it. This is the last thing a mark passes through
/// before it is drawn: it finds the monitor the mark belongs to and pins the
/// mark inside that monitor's visible bounds.
///
/// Pure geometry, no Windows: the monitors are passed in as plain rectangles so
/// the rule can be reasoned about and tested without a desktop attached.
/// </summary>
public static class ScreenBoundsClamp
{
    /// <summary>A monitor's pixel bounds. Right and Bottom are exclusive edges.</summary>
    public readonly record struct Monitor(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public int CentreX => Left + (Width / 2);
        public int CentreY => Top + (Height / 2);

        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    /// <summary>
    /// The monitor a point belongs to: the one that contains it, or failing
    /// that the nearest one by centre distance. Returns null only when no
    /// monitors were supplied at all, which the caller treats as "leave it be".
    /// </summary>
    public static Monitor? MonitorFor(int x, int y, IReadOnlyList<Monitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return null;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Contains(x, y))
            {
                return monitor;
            }
        }

        // In a gap or past every edge: snap to the closest screen rather than
        // guess, because a mark on the wrong-but-visible monitor is still worse
        // than one nobody can see, and this is the honest nearest answer.
        Monitor nearest = monitors[0];
        var nearestDistance = long.MaxValue;
        foreach (var monitor in monitors)
        {
            var dx = (long)(x - monitor.CentreX);
            var dy = (long)(y - monitor.CentreY);
            var distance = (dx * dx) + (dy * dy);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = monitor;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Pins a single point inside the visible bounds of the monitor it belongs
    /// to, leaving a small margin so a mark centred on it is not flush against
    /// the edge. With no monitors known, the point is returned untouched.
    /// </summary>
    public static (int X, int Y) ClampPoint(int x, int y, IReadOnlyList<Monitor> monitors, int margin = 4)
    {
        if (MonitorFor(x, y, monitors) is not { } monitor)
        {
            return (x, y);
        }

        return (
            Clamp(x, monitor.Left + margin, monitor.Right - margin),
            Clamp(y, monitor.Top + margin, monitor.Bottom - margin));
    }

    /// <summary>
    /// Pins a whole mark — a centre and an extent — inside its monitor. The
    /// mark is shrunk first if it is larger than the monitor (so both edges can
    /// fit), then shifted so none of it spills off. Returns the corrected
    /// centre and extent; the caller keeps drawing exactly as before, just with
    /// numbers that stay on screen.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ClampRect(
        int centreX,
        int centreY,
        int width,
        int height,
        IReadOnlyList<Monitor> monitors,
        int margin = 4)
    {
        if (MonitorFor(centreX, centreY, monitors) is not { } monitor)
        {
            return (centreX, centreY, width, height);
        }

        var maxWidth = Math.Max(1, monitor.Width - (2 * margin));
        var maxHeight = Math.Max(1, monitor.Height - (2 * margin));
        var clampedWidth = Math.Min(width, maxWidth);
        var clampedHeight = Math.Min(height, maxHeight);

        var halfWidth = clampedWidth / 2;
        var halfHeight = clampedHeight / 2;

        var clampedX = Clamp(centreX, monitor.Left + margin + halfWidth, monitor.Right - margin - halfWidth);
        var clampedY = Clamp(centreY, monitor.Top + margin + halfHeight, monitor.Bottom - margin - halfHeight);

        return (clampedX, clampedY, clampedWidth, clampedHeight);
    }

    /// <summary>
    /// Whether two points fall on the same monitor. Used to reject a control
    /// that was matched by name but sits on a different screen than where the
    /// model was pointing — the signature of the wrong control being found.
    /// </summary>
    public static bool SameMonitor(int aX, int aY, int bX, int bY, IReadOnlyList<Monitor> monitors)
    {
        var a = MonitorFor(aX, aY, monitors);
        var b = MonitorFor(bX, bY, monitors);
        return a.HasValue && b.HasValue && a.Value == b.Value;
    }

    /// <summary>Whether a point lies on any known monitor's visible bounds.</summary>
    public static bool OnAnyMonitor(int x, int y, IReadOnlyList<Monitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        foreach (var monitor in monitors)
        {
            if (monitor.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private static int Clamp(int value, int min, int max) =>
        min > max ? (min + max) / 2 : Math.Clamp(value, min, max);
}
