using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// One rule decides the mark for both a traced region and an answered one, so
/// these cases are what stops the two directions drifting apart.
/// </summary>
public sealed class MarkGeometryTests
{
    private const long Screen = 1920L * 1080;

    [Theory]
    [InlineData(48, 48)]
    [InlineData(56, 52)]
    [InlineData(40, 44)]
    public void A_compact_control_gets_a_ring(int width, int height) =>
        Assert.Equal(GuidanceMarkKind.FocusRing, MarkGeometry.ForShape(width, height, Screen));

    [Theory]
    [InlineData(620, 44)]
    [InlineData(300, 60)]
    public void A_wide_short_control_gets_a_capsule(int width, int height) =>
        Assert.Equal(GuidanceMarkKind.Capsule, MarkGeometry.ForShape(width, height, Screen));

    [Fact]
    public void A_tall_narrow_control_also_gets_a_capsule() =>
        Assert.Equal(GuidanceMarkKind.Capsule, MarkGeometry.ForShape(44, 260, Screen));

    [Theory]
    [InlineData(400, 22)]
    [InlineData(900, 30)]
    public void A_flat_wide_span_gets_an_underline(int width, int height) =>
        Assert.Equal(GuidanceMarkKind.Underline, MarkGeometry.ForShape(width, height, Screen));

    [Fact]
    public void A_large_region_gets_brackets_rather_than_an_outline()
    {
        // A full outline at this size reads as a border and swallows the
        // content it is meant to draw attention to.
        Assert.Equal(GuidanceMarkKind.Box, MarkGeometry.ForShape(1200, 700, Screen));
    }

    [Fact]
    public void The_bracket_threshold_is_relative_to_the_screen()
    {
        // The same target is "large" on a small screen and ordinary on a big one.
        const int width = 700, height = 420;
        Assert.Equal(GuidanceMarkKind.Box, MarkGeometry.ForShape(width, height, 1366L * 768));
        Assert.NotEqual(GuidanceMarkKind.Box, MarkGeometry.ForShape(width, height, 3840L * 2160));
    }

    [Fact]
    public void An_underline_wins_over_brackets_for_a_wide_thin_span()
    {
        // Wide enough to pass the area test, but it is a line of text.
        Assert.Equal(GuidanceMarkKind.Underline, MarkGeometry.ForShape(1800, 26, Screen));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(40, 0)]
    [InlineData(-5, -5)]
    public void An_unknown_size_falls_back_to_a_ring(int width, int height) =>
        Assert.Equal(GuidanceMarkKind.FocusRing, MarkGeometry.ForShape(width, height, Screen));

    [Fact]
    public void A_hand_traced_region_keeps_its_lasso() =>
        Assert.Equal(GuidanceMarkKind.Lasso, MarkGeometry.ForTrace(40));

    [Fact]
    public void A_trace_with_too_few_points_is_not_a_lasso() =>
        Assert.Equal(GuidanceMarkKind.FocusRing, MarkGeometry.ForTrace(2));
}

public sealed class TracePathTests
{
    private static GuidancePoint[] Line(int count, int step = 10) =>
        Enumerable.Range(0, count).Select(i => new GuidancePoint(100 + (i * step), 100)).ToArray();

    [Fact]
    public void A_short_wobble_is_a_tap_not_a_trace()
    {
        var jitter = new[]
        {
            new GuidancePoint(400, 300),
            new GuidancePoint(404, 302),
            new GuidancePoint(401, 298),
            new GuidancePoint(403, 301)
        };

        Assert.False(TracePath.IsTrace(jitter));
    }

    [Fact]
    public void A_real_drag_is_a_trace() => Assert.True(TracePath.IsTrace(Line(12)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Too_few_points_is_never_a_trace(int count) =>
        Assert.False(TracePath.IsTrace(Line(count)));

    [Fact]
    public void Resampling_thins_a_dense_path()
    {
        // A slow drag piles up points in almost the same place.
        var dense = Enumerable.Range(0, 400).Select(i => new GuidancePoint(100 + i, 100)).ToArray();

        var smoothed = TracePath.Smooth(dense);

        Assert.True(smoothed.Count < dense.Length / 4, $"expected thinning, got {smoothed.Count}");
        Assert.True(smoothed.Count >= 2);
    }

    [Fact]
    public void Resampling_keeps_the_first_and_last_point()
    {
        var points = Line(30);

        var smoothed = TracePath.Smooth(points);

        Assert.Equal(points[0], smoothed[0]);
        Assert.Equal(points[^1], smoothed[^1]);
    }

    [Fact]
    public void A_very_long_drag_is_capped()
    {
        var huge = Enumerable.Range(0, 8000)
            .Select(i => new GuidancePoint(100 + (i * 30), 100 + (i % 400)))
            .ToArray();

        Assert.True(TracePath.Smooth(huge).Count <= TracePath.MaxPoints);
    }

    [Fact]
    public void Duplicate_points_are_dropped()
    {
        var stalled = Enumerable.Repeat(new GuidancePoint(500, 500), 50).ToArray();

        Assert.Single(TracePath.Smooth(stalled));
    }

    [Fact]
    public void Bounds_wrap_the_whole_path()
    {
        var points = new[]
        {
            new GuidancePoint(100, 200),
            new GuidancePoint(340, 150),
            new GuidancePoint(260, 420)
        };

        var (x, y, width, height) = TracePath.Bounds(points);

        Assert.Equal(100, x);
        Assert.Equal(150, y);
        Assert.Equal(240, width);
        Assert.Equal(270, height);
    }

    [Fact]
    public void Padding_expands_the_bounds_on_every_side()
    {
        var (x, y, width, height) = TracePath.Bounds(Line(5), padding: 8);

        Assert.Equal(92, x);
        Assert.Equal(92, y);
        Assert.Equal(40 + 16, width);
        Assert.Equal(16, height);
    }

    [Fact]
    public void The_centre_sits_in_the_middle_of_the_region()
    {
        var points = new[] { new GuidancePoint(100, 100), new GuidancePoint(300, 500) };

        var centre = TracePath.Centre(points);

        Assert.Equal(200, centre.ScreenX);
        Assert.Equal(300, centre.ScreenY);
    }

    [Fact]
    public void An_empty_path_has_empty_bounds() =>
        Assert.Equal((0, 0, 0, 0), TracePath.Bounds([]));
}
