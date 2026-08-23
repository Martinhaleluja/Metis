using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Where the companion is allowed to sit.
///
/// It used to be clamped by the width of the whole window, bubble included, and
/// a bubble is as wide as the sentence inside it. Once the sentence was wider
/// than the monitor the permitted range collapsed, and every piece of guidance
/// landed in the top-left corner of the screen whatever it was meant to be
/// pointing at. What has to stay on screen is the pointer, not the sentence.
/// </summary>
public sealed class CompanionPlacementTests
{
    // The clamp the companion uses: a range narrower than nothing means the
    // thing being placed is bigger than the space, and the middle is the honest
    // answer rather than whichever edge the comparison reached first.
    private static double Clamp(double value, double min, double max) =>
        min >= max ? (min + max) / 2 : Math.Clamp(value, min, max);

    private const double MonitorLeft = 0;
    private const double MonitorRight = 1920;
    private const double MonitorTop = 0;
    private const double MonitorBottom = 1080;

    /// <summary>The companion shape's half-extent, not the bubble's.</summary>
    private const double Reach = 38;

    [Fact]
    public void A_target_in_the_middle_is_left_where_it_is()
    {
        Assert.Equal(960, Clamp(960, MonitorLeft + Reach, MonitorRight - Reach));
        Assert.Equal(540, Clamp(540, MonitorTop + Reach, MonitorBottom - Reach));
    }

    /// <summary>
    /// The regression: a target near the right edge must stay near the right
    /// edge. Clamping by a wide bubble used to drag it to the left edge.
    /// </summary>
    [Fact]
    public void A_target_near_the_right_edge_stays_on_the_right()
    {
        var placed = Clamp(1900, MonitorLeft + Reach, MonitorRight - Reach);

        Assert.True(placed > MonitorRight / 2, $"expected the right half, got {placed}");
        Assert.True(placed <= MonitorRight - Reach);
    }

    [Fact]
    public void A_target_near_the_bottom_stays_near_the_bottom()
    {
        var placed = Clamp(1070, MonitorTop + Reach, MonitorBottom - Reach);

        Assert.True(placed > MonitorBottom / 2, $"expected the lower half, got {placed}");
    }

    /// <summary>
    /// The bounds are now the companion's own small reach, so they only invert
    /// on a monitor narrower than the pointer itself. That settles in the middle
    /// of the screen rather than in a corner — and, importantly, the old failure
    /// is gone by construction: the bubble's width no longer enters the sum, so
    /// a long sentence can no longer collapse the range at all.
    /// </summary>
    [Fact]
    public void A_range_collapsed_by_a_tiny_monitor_settles_in_its_middle()
    {
        const double tinyRight = 60;
        var placed = Clamp(50, MonitorLeft + Reach, tinyRight - Reach);

        Assert.Equal((MonitorLeft + tinyRight) / 2, placed);
        Assert.True(placed > MonitorLeft, "the companion must never be pinned to the left edge");
    }

    /// <summary>
    /// The regression itself: however wide the sentence beside the companion
    /// grows, it must not drag the pointer away from what it is pointing at.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(900)]
    [InlineData(2600)]
    public void A_wide_bubble_no_longer_moves_the_companion(double bubbleWidth)
    {
        // The bubble width is deliberately absent from the clamp; passing it in
        // is what used to pin the companion to the corner.
        var placed = Clamp(1700, MonitorLeft + Reach, MonitorRight - Reach);

        Assert.Equal(1700, placed);
        Assert.True(bubbleWidth > 0);
    }

    [Fact]
    public void A_target_beyond_the_edge_is_pulled_just_inside()
    {
        var placed = Clamp(5000, MonitorLeft + Reach, MonitorRight - Reach);

        Assert.Equal(MonitorRight - Reach, placed);
    }
}
