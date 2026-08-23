namespace Metis.Core.Services;

/// <summary>
/// One moment of a flight: where the companion is, how big it has swelled, and
/// how far it is leaning into its turn.
/// </summary>
public sealed record CompanionFlightFrame(
    double X,
    double Y,
    double Scale,
    double LeanDegrees);

/// <summary>
/// The path the companion takes when it leaves the cursor to point something
/// out, and the path it takes coming back.
///
/// A straight slide at a fixed speed reads as a widget being repositioned, and
/// the eye drops it halfway across. An arc that leans into its turn, swells as
/// it passes the top, and settles as it lands reads as something crossing the
/// screen under its own power, and the eye follows it all the way to the
/// target. That difference is the whole point: the companion's job on this
/// journey is to carry the user's attention to a control, so being followable
/// matters more than being quick.
///
/// The maths is kept here rather than in the window so it can be checked
/// without a desktop, and so the demonstration path can share the same easing
/// without inheriting the arc.
/// </summary>
public sealed class CompanionFlight
{
    /// <summary>
    /// Roughly how far the companion covers per second before the clamps take
    /// over. Distance-proportional timing is what stops a hop to the next
    /// button taking as long as a crossing of two monitors.
    /// </summary>
    private static readonly double PixelsPerSecond = GuidanceTuning.ScaleSpeed(800d);

    /// <summary>
    /// A flight shorter than this still gets enough time to be seen as motion
    /// rather than a jump, and a long one stops short of feeling like a
    /// cutscene the user has to sit through.
    /// </summary>
    private static readonly TimeSpan ShortestFlight = GuidanceTuning.Scale(TimeSpan.FromMilliseconds(550));
    private static readonly TimeSpan LongestFlight = GuidanceTuning.Scale(TimeSpan.FromMilliseconds(1400));

    /// <summary>
    /// How far the arc bows away from the straight line, as a share of the
    /// distance travelled and capped in absolute terms. Without the cap, a
    /// long flight would loop absurdly high; without the share, a short hop
    /// would bow more than it travels.
    /// </summary>
    private const double ArcShareOfDistance = 0.2;
    private const double MaximumArcHeight = 80d;

    /// <summary>
    /// The companion is a rounded shape with no tip, so it cannot point itself
    /// along the tangent the way an arrow-shaped cursor would — a blob rotated
    /// to face its heading just looks wrong. It banks into the turn instead,
    /// which is how a round thing shows momentum.
    /// </summary>
    private const double MaximumLeanDegrees = 16d;

    /// <summary>
    /// How much larger the companion grows at the top of its arc. The swell is
    /// what sells the flight as a swoop rather than a slide.
    /// </summary>
    private const double PeakScaleGain = 0.3;

    private readonly double _startX;
    private readonly double _startY;
    private readonly double _endX;
    private readonly double _endY;
    private readonly double _controlX;
    private readonly double _controlY;

    private CompanionFlight(
        double startX,
        double startY,
        double endX,
        double endY,
        double controlX,
        double controlY,
        TimeSpan duration)
    {
        _startX = startX;
        _startY = startY;
        _endX = endX;
        _endY = endY;
        _controlX = controlX;
        _controlY = controlY;
        Duration = duration;
    }

    public TimeSpan Duration { get; }

    /// <summary>
    /// Plots a flight between two window origins. Coordinates are in the same
    /// space the window's Left and Top live in, so the caller converts from
    /// screen pixels once and the flight never has to think about device
    /// scaling.
    /// </summary>
    public static CompanionFlight Create(double startX, double startY, double endX, double endY)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

        var seconds = Math.Clamp(
            distance / PixelsPerSecond,
            ShortestFlight.TotalSeconds,
            LongestFlight.TotalSeconds);

        // The control point sits above the midpoint, so the companion rises
        // through the middle of the journey and comes back down onto the
        // target rather than sliding flat into it. Upward is negative Y.
        var arcHeight = Math.Min(distance * ArcShareOfDistance, MaximumArcHeight);
        var controlX = (startX + endX) / 2d;
        var controlY = ((startY + endY) / 2d) - arcHeight;

        return new CompanionFlight(
            startX,
            startY,
            endX,
            endY,
            controlX,
            controlY,
            TimeSpan.FromSeconds(seconds));
    }

    public bool IsComplete(TimeSpan elapsed) => elapsed >= Duration;

    /// <summary>
    /// The companion's state part-way through the flight. Elapsed time past
    /// the duration returns the landing frame, so a caller that ticks slightly
    /// late still finishes cleanly on the target rather than overshooting it.
    /// </summary>
    public CompanionFlightFrame FrameAt(TimeSpan elapsed)
    {
        var linearProgress = Duration <= TimeSpan.Zero
            ? 1d
            : Math.Clamp(elapsed.TotalSeconds / Duration.TotalSeconds, 0d, 1d);

        // Smoothstep, so the companion pushes off gently and settles rather
        // than starting and stopping at full speed.
        var eased = linearProgress * linearProgress * (3d - (2d * linearProgress));
        var inverse = 1d - eased;

        var x = (inverse * inverse * _startX) +
                (2d * inverse * eased * _controlX) +
                (eased * eased * _endX);
        var y = (inverse * inverse * _startY) +
                (2d * inverse * eased * _controlY) +
                (eased * eased * _endY);

        // Both the swell and the lean ride a half sine over the flight, which
        // is zero at each end. That is what guarantees the companion lands at
        // its resting size and perfectly upright, whatever the path did in the
        // middle, without a separate settle animation.
        var envelope = Math.Sin(linearProgress * Math.PI);

        var tangentX = (2d * inverse * (_controlX - _startX)) +
                       (2d * eased * (_endX - _controlX));
        var tangentY = (2d * inverse * (_controlY - _startY)) +
                       (2d * eased * (_endY - _controlY));
        var tangentLength = Math.Sqrt((tangentX * tangentX) + (tangentY * tangentY));
        var horizontalShare = tangentLength <= double.Epsilon ? 0d : tangentX / tangentLength;

        return new CompanionFlightFrame(
            x,
            y,
            1d + (envelope * PeakScaleGain),
            horizontalShare * MaximumLeanDegrees * envelope);
    }

    /// <summary>
    /// How long a deliberate traced movement over the given distance should
    /// take. A demonstration follows its literal path — the learner is copying
    /// the gesture, so an invented arc would teach the wrong motion — but it
    /// still deserves the same distance-proportional pacing as a flight
    /// instead of a fixed frame count that blurs long drags and dawdles over
    /// short ones.
    /// </summary>
    public static TimeSpan DurationForTracedMovement(double distance) =>
        TimeSpan.FromSeconds(Math.Clamp(
            distance / PixelsPerSecond,
            ShortestFlight.TotalSeconds,
            LongestFlight.TotalSeconds));

    /// <summary>
    /// Smoothstep easing on its own, for callers walking a literal path.
    /// </summary>
    public static double Ease(double linearProgress)
    {
        var clamped = Math.Clamp(linearProgress, 0d, 1d);
        return clamped * clamped * (3d - (2d * clamped));
    }
}
