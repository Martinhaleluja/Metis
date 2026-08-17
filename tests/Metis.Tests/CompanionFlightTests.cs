using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The flight is what carries the user's eye from wherever they were looking to
/// the control Metis is pointing at. These check the properties that make it
/// followable: it arrives exactly where it was sent, it takes longer for longer
/// journeys, it bows away from the straight line, and it lands at rest.
/// </summary>
public sealed class CompanionFlightTests
{
    [Fact]
    public void A_flight_starts_at_its_origin_and_ends_on_its_target()
    {
        var flight = CompanionFlight.Create(100, 100, 900, 400);

        var start = flight.FrameAt(TimeSpan.Zero);
        Assert.Equal(100, start.X, precision: 6);
        Assert.Equal(100, start.Y, precision: 6);

        var landing = flight.FrameAt(flight.Duration);
        Assert.Equal(900, landing.X, precision: 6);
        Assert.Equal(400, landing.Y, precision: 6);
    }

    [Fact]
    public void Ticking_past_the_end_lands_on_the_target_rather_than_overshooting()
    {
        // A dispatcher tick can arrive late. When it does, the companion has to
        // settle on the control rather than sail past it.
        var flight = CompanionFlight.Create(0, 0, 500, 500);

        var late = flight.FrameAt(flight.Duration + TimeSpan.FromSeconds(3));

        Assert.Equal(500, late.X, precision: 6);
        Assert.Equal(500, late.Y, precision: 6);
        Assert.True(flight.IsComplete(flight.Duration + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void A_longer_journey_takes_longer_than_a_short_one()
    {
        var hop = CompanionFlight.Create(0, 0, 120, 0);
        var crossing = CompanionFlight.Create(0, 0, 2600, 0);

        Assert.True(
            crossing.Duration > hop.Duration,
            "crossing two monitors should not take the same time as a hop to the next button");
    }

    [Fact]
    public void Flight_time_stays_within_bounds_at_both_extremes()
    {
        // A one-pixel nudge must still read as movement, and a flight across a
        // very wide desktop must not become something the user waits out.
        var nudge = CompanionFlight.Create(0, 0, 1, 0);
        var marathon = CompanionFlight.Create(0, 0, 20_000, 0);

        Assert.InRange(nudge.Duration.TotalMilliseconds, 400, 900);
        Assert.InRange(marathon.Duration.TotalMilliseconds, 900, 1600);
    }

    [Fact]
    public void The_path_bows_above_the_straight_line_between_the_points()
    {
        // The arc is what separates a companion crossing the screen from a
        // rectangle being repositioned.
        var flight = CompanionFlight.Create(0, 500, 1000, 500);

        var middle = flight.FrameAt(TimeSpan.FromSeconds(flight.Duration.TotalSeconds / 2));

        Assert.True(
            middle.Y < 500,
            $"the midpoint should rise above the straight line, but sat at {middle.Y}");
    }

    [Fact]
    public void The_companion_swells_at_the_top_of_the_arc_and_lands_at_rest()
    {
        var flight = CompanionFlight.Create(0, 0, 1200, 0);

        var middle = flight.FrameAt(TimeSpan.FromSeconds(flight.Duration.TotalSeconds / 2));

        Assert.True(middle.Scale > 1.2, $"expected a swell at the apex, got {middle.Scale}");
        Assert.Equal(1d, flight.FrameAt(TimeSpan.Zero).Scale, precision: 6);
        Assert.Equal(1d, flight.FrameAt(flight.Duration).Scale, precision: 6);
    }

    [Fact]
    public void The_companion_lands_upright_however_far_it_leaned_on_the_way()
    {
        // The lean and the swell both ride a half sine that is zero at each
        // end, so no separate settling step is needed to straighten it up.
        var flight = CompanionFlight.Create(0, 0, 1500, 200);

        var middle = flight.FrameAt(TimeSpan.FromSeconds(flight.Duration.TotalSeconds / 2));

        Assert.True(Math.Abs(middle.LeanDegrees) > 5, "the companion should bank into a long turn");
        Assert.Equal(0d, flight.FrameAt(TimeSpan.Zero).LeanDegrees, precision: 6);
        Assert.Equal(0d, flight.FrameAt(flight.Duration).LeanDegrees, precision: 6);
    }

    [Fact]
    public void The_companion_leans_the_way_it_is_travelling()
    {
        var rightward = CompanionFlight.Create(0, 0, 1200, 0);
        var leftward = CompanionFlight.Create(1200, 0, 0, 0);

        var rightLean = rightward.FrameAt(
            TimeSpan.FromSeconds(rightward.Duration.TotalSeconds / 2)).LeanDegrees;
        var leftLean = leftward.FrameAt(
            TimeSpan.FromSeconds(leftward.Duration.TotalSeconds / 2)).LeanDegrees;

        Assert.True(rightLean > 0, "travelling right should lean right");
        Assert.True(leftLean < 0, "travelling left should lean left");
    }

    [Fact]
    public void Progress_along_the_path_never_goes_backwards()
    {
        // Smoothstep easing is monotonic, and the bezier is plotted from it, so
        // the companion must never stutter or reverse part-way.
        var flight = CompanionFlight.Create(0, 0, 1000, 0);
        var previousX = double.NegativeInfinity;

        for (var frame = 0; frame <= 100; frame++)
        {
            var elapsed = TimeSpan.FromSeconds(flight.Duration.TotalSeconds * frame / 100d);
            var x = flight.FrameAt(elapsed).X;
            Assert.True(x >= previousX, $"the companion moved backwards at frame {frame}");
            previousX = x;
        }
    }

    [Fact]
    public void A_flight_to_where_it_already_is_stays_put()
    {
        var flight = CompanionFlight.Create(640, 480, 640, 480);

        var middle = flight.FrameAt(TimeSpan.FromSeconds(flight.Duration.TotalSeconds / 2));

        Assert.Equal(640, middle.X, precision: 6);
        Assert.Equal(480, middle.Y, precision: 6);
        Assert.Equal(0d, middle.LeanDegrees, precision: 6);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(0.5d, 0.5d)]
    [InlineData(1d, 1d)]
    public void Easing_pins_both_ends_and_passes_through_the_middle(double input, double expected) =>
        Assert.Equal(expected, CompanionFlight.Ease(input), precision: 6);

    [Fact]
    public void Easing_clamps_input_outside_the_flight()
    {
        Assert.Equal(0d, CompanionFlight.Ease(-2), precision: 6);
        Assert.Equal(1d, CompanionFlight.Ease(4), precision: 6);
    }

    [Fact]
    public void A_traced_movement_is_paced_by_its_distance_like_a_flight()
    {
        var shortDrag = CompanionFlight.DurationForTracedMovement(80);
        var longDrag = CompanionFlight.DurationForTracedMovement(2400);

        Assert.True(
            longDrag > shortDrag,
            "a drag across the screen should take longer to demonstrate than a nudge");
    }
}
