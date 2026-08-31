namespace Metis.Core.Services;

/// <summary>
/// One knob for how much the interface moves.
///
/// This is the reduced-motion setting, made real. It had been declared in
/// settings, shown in a checkbox, written back to disk, and read by nothing at
/// all — its own doc comment claimed it shortened the companion's flight and the
/// notch's unfurl, and neither was true. Someone who ticked it got exactly the
/// same application.
///
/// It follows the shape <see cref="GuidanceTuning"/> already established for the
/// lesson tempo: one multiplier everything passes through, so the answer to
/// "why is this still animating" is one place rather than a hunt through five
/// files of hard-coded literals.
///
/// The important difference from a tempo knob: reduced motion is not *faster*
/// motion. A sixty-millisecond slide is still a slide, and it is still motion to
/// someone whose vestibular system objects to it. <see cref="Reduced"/> means
/// jump straight to the end, and the call sites are written to do exactly that
/// rather than to scale down.
/// </summary>
public static class MotionTuning
{
    /// <summary>
    /// The current multiplier. 1 is normal; 0 means no motion at all.
    ///
    /// Nothing in between is used today, but the shape leaves room for a "less
    /// motion" middle setting without changing any call site.
    /// </summary>
    public static double Pace { get; private set; } = 1.0;

    /// <summary>
    /// Whether motion is off. Call sites branch on this rather than animating
    /// over a very short duration, because a short animation is still motion.
    /// </summary>
    public static bool Reduced => Pace <= 0.001;

    /// <summary>
    /// Applies the user's preference and the operating system's.
    ///
    /// Both are honoured, and either one is enough. Windows has had a "Show
    /// animations" accessibility switch for years, exposed to WPF as
    /// <c>SystemParameters.ClientAreaAnimation</c>, and Metis ignored it
    /// completely — so a person who had already told Windows they did not want
    /// animation had to find and tick a second box in a second place. Someone
    /// who has set the preference once should not have to set it again.
    ///
    /// The application's own checkbox can only turn motion *off*, never back on
    /// against the operating system's wishes.
    /// </summary>
    public static void Apply(bool reduceMotionSetting, bool operatingSystemAllowsAnimation)
    {
        Pace = reduceMotionSetting || !operatingSystemAllowsAnimation ? 0.0 : 1.0;
    }

    /// <summary>
    /// Scales a duration, or collapses it to nothing when motion is off.
    ///
    /// Callers that can branch should branch on <see cref="Reduced"/> and skip
    /// the animation entirely; this is for the ones that cannot, where a
    /// zero-length animation is the closest thing available to not animating.
    /// </summary>
    public static TimeSpan Scale(TimeSpan duration) =>
        Reduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(duration.TotalMilliseconds * Pace);

    /// <summary>Scales a millisecond count, on the same rule.</summary>
    public static double ScaleMs(double milliseconds) => Reduced ? 0 : milliseconds * Pace;

    /// <summary>
    /// How long a per-item stagger delay should be, and how many items may
    /// carry one.
    ///
    /// Capped deliberately. A stagger reads as one considered arrival up to
    /// about half a dozen items; past that it reads as a list being slowly typed
    /// out, and the last item's delay becomes a wait rather than a flourish. The
    /// interface rules this project follows put the same limit differently —
    /// animate one or two things per view — and this is where that gets
    /// enforced for lists.
    /// </summary>
    public static double StaggerDelayMs(int index, double stepMs = 36, int maxStaggered = 6) =>
        Reduced || index >= maxStaggered ? 0 : ScaleMs(index * stepMs);
}
