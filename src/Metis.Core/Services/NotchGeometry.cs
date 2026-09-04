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
    public const double RestingHeight = 28;

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
    /// How much of the screen a page that is not about the screen may take.
    ///
    /// The 0.82 above exists so that a chat answering a question about what is
    /// behind it does not cover the thing being asked about. Settings and first
    /// run are not about anything behind them — nobody opens the companion
    /// colour picker to look past it — and holding them to the same share was
    /// costing a laptop over a hundred pixels for no benefit, which is the
    /// difference between a page that fits and a page that scrolls.
    ///
    /// Not 1.0: the notch has to keep looking like something hanging from the
    /// top edge rather than a full-screen window, and a strip of desktop under
    /// it is what says so.
    /// </summary>
    public const double TallPageShare = 0.94;

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
    public static double MaxBodyHeight(double workAreaHeight, double share = WorkAreaShare) =>
        Math.Max(MinimumMaxHeight, (workAreaHeight * share) - WindowSlack);

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
        double workAreaHeight,
        double share = WorkAreaShare)
    {
        var wanted = chromeHeight + Math.Max(0, measuredContentHeight);
        return Math.Clamp(wanted, RestingHeight, MaxBodyHeight(workAreaHeight, share));
    }

    /// <summary>
    /// Whether a page needs to scroll: it wanted more room than the screen
    /// allows.
    /// </summary>
    public static bool NeedsScrolling(
        double measuredContentHeight,
        double chromeHeight,
        double workAreaHeight,
        double share = WorkAreaShare) =>
        chromeHeight + measuredContentHeight > MaxBodyHeight(workAreaHeight, share);

    /// <summary>
    /// Whether a list of rows should drop its explanatory second lines to fit
    /// the screen.
    ///
    /// The settings menu is ten rows, each a title over a sentence saying what
    /// the section is for. On a desktop that fits with room to spare. On a
    /// laptop at 150% scaling the whole work area is around 550 points, and the
    /// list is cut through the middle of a row — which is the first thing
    /// somebody sees of settings and reads as a broken window rather than as a
    /// list that continues.
    ///
    /// Dropping the summaries is the right thing to lose. The titles alone are
    /// still navigable and the whole list becomes visible at once, which is what
    /// a menu is for; a scrolling front door is worse than a terse one.
    /// </summary>
    /// <param name="rows">How many rows the list has.</param>
    /// <param name="tallRowHeight">A row with its summary line.</param>
    /// <param name="shortRowHeight">A row with the summary removed.</param>
    /// <param name="chromeHeight">Header, padding and anything else above.</param>
    public static bool ListWantsCompactRows(
        int rows,
        double tallRowHeight,
        double shortRowHeight,
        double chromeHeight,
        double workAreaHeight,
        double share = TallPageShare)
    {
        var available = MaxBodyHeight(workAreaHeight, share);

        // Only worth doing if it actually rescues the list. A list too long even
        // without its summaries keeps them and scrolls, because losing them
        // would cost the explanations and still not fit.
        return chromeHeight + (rows * tallRowHeight) > available
            && chromeHeight + (rows * shortRowHeight) <= available;
    }

    /// <summary>
    /// The narrowest the notch ever is: the resting pill.
    /// </summary>
    public const double TuckedWidth = 90;

    /// <summary>
    /// The gap between the body and the host window on the horizontal axis.
    ///
    /// Smaller than <see cref="WindowSlack"/> because there is no drop shadow
    /// to clear at the sides, only the one-pixel border and the rounded corner.
    /// </summary>
    public const double HorizontalSlack = 24;

    /// <summary>
    /// How much of the usable width the notch may take.
    ///
    /// Higher than the vertical share because width is not what covers the
    /// thing being asked about — a panel that reaches most of the way across a
    /// narrow screen is still a strip hanging from the top edge, while one held
    /// to four fifths of a 1024-wide laptop has to wrap its text twice as often
    /// for no benefit.
    /// </summary>
    public const double WidthShare = 0.92;

    /// <summary>
    /// The widest the body may be on a screen this wide.
    ///
    /// The horizontal axis had no rule of any kind before this. Every width in
    /// the notch was a constant chosen against one monitor — 640 for settings,
    /// 640 for first run, 560 for the window that was supposed to contain it —
    /// and the window's width was picked from a chain of per-page tests that
    /// simply omitted first run, so the wizard rendered forty pixels wider than
    /// the window drawing it and lost that much off each side. A measured rule
    /// removes the chain, and with it the possibility of forgetting a page.
    /// </summary>
    public static double MaxBodyWidth(double workAreaWidth) =>
        Math.Max(TuckedWidth, (workAreaWidth * WidthShare) - HorizontalSlack);

    /// <summary>
    /// How wide the body should be for a page that wants
    /// <paramref name="desiredWidth"/>.
    ///
    /// Never narrower than the resting pill, never wider than the screen
    /// allows. A page asking for more than fits is narrowed rather than
    /// clipped, because a panel that is too narrow still reads, and one whose
    /// right-hand edge is off the screen does not.
    /// </summary>
    public static double BodyWidth(double desiredWidth, double workAreaWidth) =>
        Math.Clamp(desiredWidth, TuckedWidth, MaxBodyWidth(workAreaWidth));

    /// <summary>
    /// How wide the host window must be to contain a body of this width,
    /// never wider than the screen it sits on.
    /// </summary>
    public static double WindowWidth(double bodyWidth, double workAreaWidth) =>
        Math.Min(bodyWidth + HorizontalSlack, workAreaWidth);

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
