namespace Metis.Core.Services;

/// <summary>
/// One knob for how fast the guidance feels.
///
/// The pace of a lesson is spread across a dozen separate durations — how long
/// a mark holds, how quickly it draws on, how fast the companion flies, how
/// long the pause after an answer. Tuning them one at a time drifts out of
/// step. This is the single multiplier they all pass through, so "make it
/// snappier" is one number rather than a scavenger hunt.
///
/// Below 1 is faster. It deliberately does not touch the length of spoken
/// audio, which is fixed by the clip — speeding the visuals past the voice
/// would let the words fall behind what is on screen.
/// </summary>
public static class GuidanceTuning
{
    /// <summary>
    /// The current pace. 0.83 makes everything about 17% quicker — the middle
    /// of the range the tempo was asked to move by.
    /// </summary>
    public const double Pace = 0.83;

    /// <summary>Scales a duration by the pace, never below a sensible floor.</summary>
    public static TimeSpan Scale(TimeSpan duration)
    {
        var scaled = duration.TotalMilliseconds * Pace;
        return TimeSpan.FromMilliseconds(scaled < 1 ? 1 : scaled);
    }

    /// <summary>Scales a millisecond count by the pace.</summary>
    public static double ScaleMs(double milliseconds) => milliseconds * Pace;

    /// <summary>
    /// Scales a speed the other way — a higher pixels-per-second makes motion
    /// finish sooner — so raising the tempo and shortening a hold stay in
    /// agreement instead of pulling against each other.
    /// </summary>
    public static double ScaleSpeed(double unitsPerSecond) => unitsPerSecond / Pace;
}
