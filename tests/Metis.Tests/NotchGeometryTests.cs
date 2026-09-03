using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// How tall the notch is allowed to get, on the screens people actually have.
///
/// The old answer was 520 pixels, hard-coded into four methods with three
/// different formulas, chosen when the notch held a chat and nothing else. Once
/// settings moved in — the provider page alone measures about fourteen hundred
/// pixels — 520 stopped being a cap and started being a place where content
/// silently disappeared, because the body clips rather than scrolls.
/// </summary>
public sealed class NotchGeometryTests
{
    // The three screens worth checking: a small laptop, the common desktop, and
    // a large monitor. Work area rather than screen height, because the taskbar
    // is not the notch's to use.
    private const double SmallLaptop = 728;   // 768 with a 40px taskbar
    private const double CommonDesktop = 1040; // 1080 with a 40px taskbar
    private const double LargeMonitor = 1400;  // 1440 with a 40px taskbar

    [Theory]
    [InlineData(SmallLaptop)]
    [InlineData(CommonDesktop)]
    [InlineData(LargeMonitor)]
    public void The_notch_never_covers_the_whole_screen(double workArea)
    {
        // Metis exists to explain what is on the screen. A notch that fills the
        // screen has eaten the thing it is talking about.
        Assert.True(NotchGeometry.MaxBodyHeight(workArea) < workArea * 0.9);
    }

    [Fact]
    public void A_bigger_screen_allows_a_taller_notch()
    {
        Assert.True(
            NotchGeometry.MaxBodyHeight(CommonDesktop) > NotchGeometry.MaxBodyHeight(SmallLaptop));
        Assert.True(
            NotchGeometry.MaxBodyHeight(LargeMonitor) > NotchGeometry.MaxBodyHeight(CommonDesktop));
    }

    /// <summary>
    /// Every screen has to leave room for a page to say something. The floor
    /// only binds on something smaller than a netbook, and it exists so the
    /// answer is never "forty pixels".
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(0)]
    public void A_tiny_screen_still_gets_a_usable_notch(double workArea) =>
        Assert.Equal(NotchGeometry.MinimumMaxHeight, NotchGeometry.MaxBodyHeight(workArea));

    /// <summary>
    /// The old cap. Every screen from a small laptop upwards now gets more room
    /// than the hard-coded 520 it used to have.
    /// </summary>
    [Theory]
    [InlineData(SmallLaptop)]
    [InlineData(CommonDesktop)]
    [InlineData(LargeMonitor)]
    public void Every_ordinary_screen_beats_the_old_hard_coded_cap(double workArea) =>
        Assert.True(NotchGeometry.MaxBodyHeight(workArea) > 520);

    // ------------------------------ Body height ------------------------------

    [Fact]
    public void A_short_page_gets_exactly_what_it_asked_for() =>
        Assert.Equal(244, NotchGeometry.BodyHeight(200, chromeHeight: 44, CommonDesktop));

    [Fact]
    public void A_tall_page_is_capped_rather_than_granted()
    {
        var height = NotchGeometry.BodyHeight(1400, chromeHeight: 44, CommonDesktop);

        Assert.Equal(NotchGeometry.MaxBodyHeight(CommonDesktop), height);
    }

    [Fact]
    public void An_empty_page_never_shrinks_below_the_resting_pill() =>
        Assert.Equal(NotchGeometry.RestingHeight, NotchGeometry.BodyHeight(0, 0, CommonDesktop));

    [Fact]
    public void A_negative_measurement_is_treated_as_nothing() =>
        Assert.Equal(NotchGeometry.RestingHeight, NotchGeometry.BodyHeight(-500, 0, CommonDesktop));

    // ------------------------------- Scrolling -------------------------------

    /// <summary>
    /// The settings page that made the old cap untenable. On every screen it
    /// has to scroll, which is the behaviour that did not exist — before this,
    /// the excess was simply clipped away and unreachable.
    /// </summary>
    [Theory]
    [InlineData(SmallLaptop)]
    [InlineData(CommonDesktop)]
    [InlineData(LargeMonitor)]
    public void The_provider_settings_page_scrolls_on_every_screen(double workArea) =>
        Assert.True(NotchGeometry.NeedsScrolling(1400, chromeHeight: 44, workArea));

    [Fact]
    public void A_short_page_does_not_scroll() =>
        Assert.False(NotchGeometry.NeedsScrolling(200, chromeHeight: 44, CommonDesktop));

    // -------------------------------- Window --------------------------------

    [Fact]
    public void The_window_leaves_room_for_the_shadow() =>
        Assert.Equal(
            400 + NotchGeometry.WindowSlack,
            NotchGeometry.WindowHeight(400, CommonDesktop));

    [Fact]
    public void The_window_never_grows_past_the_screen() =>
        Assert.Equal(SmallLaptop, NotchGeometry.WindowHeight(SmallLaptop, SmallLaptop));

    // ------------------------------- Jitter ---------------------------------

    /// <summary>
    /// The notch re-measures on every keystroke that reflows the composer.
    /// Animating a two-pixel difference over hundreds of milliseconds made it
    /// shiver continuously while somebody typed.
    /// </summary>
    [Theory]
    [InlineData(300, 301)]
    [InlineData(300, 295)]
    [InlineData(300, 300)]
    public void A_tiny_height_change_is_not_worth_animating(double from, double to) =>
        Assert.False(NotchGeometry.WorthAnimating(from, to));

    [Theory]
    [InlineData(300, 340)]
    [InlineData(300, 200)]
    public void A_real_height_change_is(double from, double to) =>
        Assert.True(NotchGeometry.WorthAnimating(from, to));

    // ------------------------------- Width ----------------------------------

    /// <summary>
    /// The horizontal axis had no rule at all until now. Every width in the
    /// notch was a constant picked against one monitor, and the host window's
    /// width came from a chain of per-page tests that simply omitted first run —
    /// so the 640-point welcome wizard was drawn inside a 560-point window and
    /// lost forty points off each side. That is the "most UI elements are cut
    /// out" report, and it is only fixable by deriving the window from the same
    /// number the page is given.
    /// </summary>
    [Theory]
    [InlineData(1920, 640)]
    [InlineData(2560, 640)]
    [InlineData(1366, 640)]
    public void A_page_that_fits_gets_the_width_it_asked_for(double screen, double wanted) =>
        Assert.Equal(wanted, NotchGeometry.BodyWidth(wanted, screen));

    /// <summary>
    /// A page too wide for the screen is narrowed, not clipped. A narrow panel
    /// still reads; one whose right edge is past the monitor does not.
    /// </summary>
    [Theory]
    [InlineData(700, 640)]
    [InlineData(1920, 4000)]
    [InlineData(1024, 1200)]
    public void A_page_wider_than_the_screen_is_brought_back_onto_it(double screen, double wanted)
    {
        var width = NotchGeometry.BodyWidth(wanted, screen);

        Assert.True(width < wanted, $"{wanted} should not have survived a {screen}-wide screen.");
        Assert.True(NotchGeometry.WindowWidth(width, screen) <= screen);
    }

    /// <summary>
    /// The window is always at least as wide as the body it contains. A WPF
    /// window clips its content rather than scrolling it, so a window narrower
    /// than its body loses the difference silently.
    /// </summary>
    [Theory]
    [InlineData(1920)]
    [InlineData(1366)]
    [InlineData(1024)]
    [InlineData(800)]
    public void The_window_always_contains_the_body(double screen)
    {
        foreach (var wanted in new double[] { 104, 200, 430, 540, 640, 900, 4000 })
        {
            var body = NotchGeometry.BodyWidth(wanted, screen);
            var window = NotchGeometry.WindowWidth(body, screen);

            Assert.True(
                window >= body,
                $"A {body}-wide body needs at least that much window, got {window} on a {screen}-wide screen.");
            Assert.True(window <= screen, $"The window ran off a {screen}-wide screen at {window}.");
        }
    }

    /// <summary>
    /// The resting pill is the floor. Nothing may make the notch narrower than
    /// the thing the user grabs to pull it down.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(-100)]
    public void The_notch_never_narrows_past_the_resting_pill(double wanted) =>
        Assert.Equal(NotchGeometry.TuckedWidth, NotchGeometry.BodyWidth(wanted, 1920));
}
