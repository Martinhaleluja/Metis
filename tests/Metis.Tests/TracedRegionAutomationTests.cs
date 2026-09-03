using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class TracedRegionAutomationTests
{
    [Fact]
    public void IsTrace_returns_false_for_tap_within_threshold()
    {
        var tapPoints = new[]
        {
            new GuidancePoint(100, 100),
            new GuidancePoint(105, 102),
            new GuidancePoint(103, 106)
        };

        Assert.False(TracePath.IsTrace(tapPoints));
    }

    [Fact]
    public void IsTrace_returns_true_for_drag_beyond_threshold()
    {
        var dragPoints = new[]
        {
            new GuidancePoint(100, 100),
            new GuidancePoint(150, 120),
            new GuidancePoint(200, 200)
        };

        Assert.True(TracePath.IsTrace(dragPoints));
    }

    [Fact]
    public void Bounds_and_Centre_calculate_accurate_region_geometry()
    {
        var points = new[]
        {
            new GuidancePoint(100, 200),
            new GuidancePoint(500, 200),
            new GuidancePoint(500, 600),
            new GuidancePoint(100, 600)
        };

        var (x, y, width, height) = TracePath.Bounds(points, padding: 0);
        Assert.Equal(100, x);
        Assert.Equal(200, y);
        Assert.Equal(400, width);
        Assert.Equal(400, height);

        var centre = TracePath.Centre(points);
        Assert.Equal(300, centre.ScreenX);
        Assert.Equal(400, centre.ScreenY);
    }

    [Fact]
    public void Bounds_with_padding_expands_boundaries()
    {
        var points = new[]
        {
            new GuidancePoint(100, 200),
            new GuidancePoint(300, 400)
        };

        var (x, y, width, height) = TracePath.Bounds(points, padding: 10);
        Assert.Equal(90, x);
        Assert.Equal(190, y);
        Assert.Equal(220, width);
        Assert.Equal(220, height);
    }

    [Fact]
    public void ScreenRegion_is_usable_when_dimensions_are_positive()
    {
        var validRegion = new ScreenRegion(10, 10, 100, 100, [new GuidancePoint(10, 10), new GuidancePoint(110, 110)]);
        Assert.True(validRegion.IsUsable);

        var zeroWidthRegion = new ScreenRegion(10, 10, 0, 100, []);
        Assert.False(zeroWidthRegion.IsUsable);

        var zeroHeightRegion = new ScreenRegion(10, 10, 100, 0, []);
        Assert.False(zeroHeightRegion.IsUsable);
    }
}
