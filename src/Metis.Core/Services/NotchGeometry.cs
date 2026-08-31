namespace Metis.Core.Services;

/// <summary>
/// How big the notch is allowed to get.
///
/// This was four methods with three different formulas — the chat clamped a
/// measurement to 520, the sign-in panel did the same, and the two agent panels
/// added twenty pixels and clamped to 520 with a floor. Nothing made them agree,
/// and 520 was a number chosen when the notch held a chat and nothing else.
///
/// It is here rather than in the window for the same reason the routing is: the
/// shell is seventeen hundred lines of WPF with no test coverage, and "how tall
/// may this get" is exactly the kind of arithmetic that is wrong on somebody
/// else's screen. The work area comes in as a parameter rather than being read
/// from <c>SystemParameters</c>, so a laptop, a 1080p monitor and a 1440p
/// monitor can all be checked without one.
/// </summary>
public static class NotchGeometry
{
    /// <summary>The resting height of the pill, before any page opens.</summary>
    public const double RestingHeight = 34;

    /// <summary>
    /// The gap between the body and the host window that contains it. The window
    /// has to be grown before the body animates, because a WPF window clips its
    /// content rather than scrolling it.
    /// </summary>
    public const double WindowSlack = 26;

    /// <summary>
    /// How much of the usable screen the notch may take.
    ///
    /// Deliberately not all of it. Metis exists to explain what is on the
    /// screen, so a notch that covers the screen has eaten the thing it is
    /// talking about. Four fifths leaves the subject visible and still reads as
    /// a panel rather than a takeover.
    /// </summary>
    public const double WorkAreaShare = 0.82;

    /// <summary>
    /// The floor, for small laptops. On a 768-tall screen the share above gives
    /// about 604px; this only binds on something smaller still, and it exists so
    /// the notch is never so short that a page has no room to say anything.
    /// </summary>
    public const double MinimumMaxHeight = 320;

    /// <summary>
    /// The tallest the body may be on a screen with this much usable height.
    ///
    /// "Usable" means the work area — the desktop minus the taskbar — because
    /// that is what the notch shares the screen with.
    /// </summary>
    public static double MaxBodyHeight(double workAreaHeight) =>
        Math.Max(MinimumMaxHeight, (workAreaHeight * WorkAreaShare) - WindowSlack);

    /// <summary>
    /// How tall the body should be for a page that measured
    /// <paramref name="measuredContentHeight"/>, including its chrome.
    ///
    /// Never below the resting height, never above what the screen allows.
    /// Anything taller than the cap scrolls inside the notch instead — which is
    /// the part that did not exist before, and why content simply vanished when
    /// a page outgrew 520 pixels.
    /// </summary>
    public static double BodyHeight(
        double measuredContentHeight,
        double chromeHeight,
        double workAreaHeight)
    {
        var wanted = chromeHeight + Math.Max(0, measuredContentHeight);
        return Math.Clamp(wanted, RestingHeight, MaxBodyHeight(workAreaHeight));
    }

    /// <summary>
    /// Whether a page needs to scroll: it wanted more room than the screen
    /// allows.
    /// </summary>
    public static bool NeedsScrolling(
        double measuredContentHeight,
        double chromeHeight,
        double workAreaHeight) =>
        chromeHeight + measuredContentHeight > MaxBodyHeight(workAreaHeight);

    /// <summary>
    /// How tall the host window must be to contain a body of this height,
    /// never taller than the screen it sits on.
    /// </summary>
    public static double WindowHeight(double bodyHeight, double workAreaHeight) =>
        Math.Min(bodyHeight + WindowSlack, workAreaHeight);

    /// <summary>
    /// Whether a height change is worth animating.
    ///
    /// Below this the change is assigned directly. The notch re-measures on
    /// every keystroke that reflows the composer, and animating a two-pixel
    /// difference over hundreds of milliseconds made it jitter continuously
    /// while somebody typed.
    /// </summary>
    public static bool WorthAnimating(double from, double to) => Math.Abs(to - from) >= 6;
}
