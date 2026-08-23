using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Marks are computed in one desktop-wide coordinate space, so the last thing
/// before drawing is pinning them onto a monitor the user can actually see.
/// These fix the rules that keep a highlight from being drawn where nobody can
/// find it.
/// </summary>
public sealed class ScreenBoundsClampTests
{
    private static readonly ScreenBoundsClamp.Monitor Primary = new(0, 0, 1920, 1080);
    private static readonly ScreenBoundsClamp.Monitor Secondary = new(1920, 0, 3840, 1080);
    private static readonly IReadOnlyList<ScreenBoundsClamp.Monitor> Both = [Primary, Secondary];

    [Fact]
    public void A_point_already_on_screen_is_left_alone()
    {
        var (x, y) = ScreenBoundsClamp.ClampPoint(500, 400, Both);

        Assert.Equal(500, x);
        Assert.Equal(400, y);
    }

    [Fact]
    public void A_point_past_the_right_edge_is_pulled_back_onto_its_monitor()
    {
        var (x, y) = ScreenBoundsClamp.ClampPoint(2500, 400, [Primary]);

        Assert.True(x <= Primary.Right);
        Assert.True(x >= Primary.Left);
        Assert.Equal(400, y);
    }

    [Fact]
    public void A_point_in_the_gap_snaps_to_the_nearest_monitor()
    {
        // A tall stacked layout leaves a gap below the primary monitor.
        var stacked = new List<ScreenBoundsClamp.Monitor> { new(0, 0, 1920, 1080), new(0, 1080, 1920, 2160) };
        var monitor = ScreenBoundsClamp.MonitorFor(960, 5000, stacked);

        Assert.NotNull(monitor);
        Assert.Equal(1080, monitor!.Value.Top); // the lower screen, nearest to y=5000
    }

    [Fact]
    public void A_rect_wider_than_the_monitor_is_shrunk_to_fit()
    {
        var (_, _, width, height) = ScreenBoundsClamp.ClampRect(960, 540, 5000, 4000, [Primary]);

        Assert.True(width <= Primary.Width);
        Assert.True(height <= Primary.Height);
    }

    [Fact]
    public void A_rect_near_an_edge_is_shifted_so_all_of_it_stays_on_screen()
    {
        var (x, y, width, height) = ScreenBoundsClamp.ClampRect(1900, 1060, 200, 120, [Primary]);

        Assert.True(x - (width / 2) >= Primary.Left);
        Assert.True(x + (width / 2) <= Primary.Right);
        Assert.True(y - (height / 2) >= Primary.Top);
        Assert.True(y + (height / 2) <= Primary.Bottom);
    }

    [Fact]
    public void A_mark_on_the_second_monitor_stays_on_the_second_monitor()
    {
        var (x, _, _, _) = ScreenBoundsClamp.ClampRect(3800, 540, 200, 120, Both);

        Assert.True(x >= Secondary.Left);
        Assert.True(x <= Secondary.Right);
    }

    [Fact]
    public void Negative_coordinates_on_a_left_hand_monitor_are_handled()
    {
        var left = new ScreenBoundsClamp.Monitor(-1920, 0, 0, 1080);
        var (x, y) = ScreenBoundsClamp.ClampPoint(-3000, 500, [left]);

        Assert.True(x >= left.Left && x <= left.Right);
        Assert.Equal(500, y);
    }

    [Fact]
    public void With_no_monitors_known_a_point_is_left_untouched()
    {
        var (x, y) = ScreenBoundsClamp.ClampPoint(9999, 9999, []);

        Assert.Equal(9999, x);
        Assert.Equal(9999, y);
    }

    [Fact]
    public void Off_every_monitor_is_reported_as_such()
    {
        Assert.False(ScreenBoundsClamp.OnAnyMonitor(5000, 5000, Both));
        Assert.True(ScreenBoundsClamp.OnAnyMonitor(500, 400, Both));
    }

    [Fact]
    public void Two_points_on_different_monitors_are_not_the_same_monitor()
    {
        Assert.False(ScreenBoundsClamp.SameMonitor(500, 400, 2500, 400, Both));
        Assert.True(ScreenBoundsClamp.SameMonitor(500, 400, 700, 600, Both));
    }
}

/// <summary>
/// One knob drives how fast the guidance feels. These pin that it actually
/// makes things quicker, and that it never speeds visuals past the fixed length
/// of the spoken audio by producing a nonsensical zero duration.
/// </summary>
public sealed class GuidanceTuningTests
{
    [Fact]
    public void The_pace_makes_things_faster_not_slower() =>
        Assert.True(GuidanceTuning.Pace < 1.0 && GuidanceTuning.Pace > 0.5);

    [Fact]
    public void Scaling_a_duration_shortens_it()
    {
        var scaled = GuidanceTuning.Scale(TimeSpan.FromSeconds(10));

        Assert.True(scaled < TimeSpan.FromSeconds(10));
        Assert.Equal(10_000 * GuidanceTuning.Pace, scaled.TotalMilliseconds, 1);
    }

    [Fact]
    public void A_tiny_duration_never_scales_to_nothing() =>
        Assert.True(GuidanceTuning.Scale(TimeSpan.Zero) >= TimeSpan.FromMilliseconds(1));

    [Fact]
    public void Scaling_a_speed_raises_it_so_motion_finishes_sooner() =>
        Assert.True(GuidanceTuning.ScaleSpeed(800d) > 800d);
}
