using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// When a list gives up its explanations to fit the screen.
///
/// The settings menu is ten rows, each a title over a sentence saying what the
/// section is for. That fits a desktop with room to spare and does not come
/// close to fitting a laptop at 150% scaling, where the whole work area is
/// around 550 points — and the failure was not a scrollbar, it was the list
/// being sliced through the middle of a row, which is the first thing anyone
/// sees of settings and reads as a broken window.
///
/// Losing the summaries is the right thing to lose. The rule is here rather
/// than in the panel because "does the menu fit" is arithmetic, and arithmetic
/// in a WPF code-behind is arithmetic nothing can test.
/// </summary>
public sealed class NotchListDensityTests
{
    // The settings menu's real numbers, measured from its style.
    private const int Rows = 10;
    private const double TallRow = 57;
    private const double ShortRow = 36;
    private const double Chrome = 96;

    private static bool Compact(double workAreaHeight) =>
        NotchGeometry.ListWantsCompactRows(Rows, TallRow, ShortRow, Chrome, workAreaHeight);

    /// <summary>
    /// A desktop keeps the explanations. There is no reason to take them away
    /// from a screen that can show them.
    /// </summary>
    [Theory]
    [InlineData(1040)]
    [InlineData(1400)]
    public void A_tall_screen_keeps_the_summaries(double workAreaHeight) =>
        Assert.False(Compact(workAreaHeight));

    /// <summary>
    /// The laptop this was reported from. Ten two-line rows need 666 points
    /// plus chrome and cannot fit; ten one-line rows can.
    /// </summary>
    [Theory]
    [InlineData(560)]
    [InlineData(620)]
    [InlineData(700)]
    public void A_short_screen_drops_them(double workAreaHeight) =>
        Assert.True(Compact(workAreaHeight));

    /// <summary>
    /// The case that would be a mistake to get wrong: a screen so short that
    /// the list does not fit even without its summaries. Removing them there
    /// would cost the explanations and still leave a scrolling list, so they
    /// stay and it scrolls.
    /// </summary>
    [Fact]
    public void A_screen_too_short_for_either_keeps_them() =>
        Assert.False(Compact(300));

    /// <summary>
    /// The decision moves one way as the screen grows. A rule that flipped back
    /// and forth would mean the menu changed shape for no reason a user could
    /// see.
    /// </summary>
    [Fact]
    public void The_answer_changes_at_most_twice_across_every_screen()
    {
        var changes = 0;
        var previous = Compact(200);

        for (var height = 220.0; height <= 2000; height += 20)
        {
            var current = Compact(height);
            if (current != previous)
            {
                changes++;
                previous = current;
            }
        }

        // Off below the floor, on through the middle, off again once it fits.
        Assert.True(changes <= 2, $"the density flipped {changes} times");
    }

    /// <summary>
    /// A list short enough to fit either way never gives anything up.
    /// </summary>
    [Fact]
    public void A_short_list_keeps_its_summaries_on_any_screen()
    {
        foreach (var height in new[] { 400.0, 560, 700, 1040 })
        {
            Assert.False(
                NotchGeometry.ListWantsCompactRows(3, TallRow, ShortRow, Chrome, height),
                $"a three-row list went compact at {height}");
        }
    }
}
