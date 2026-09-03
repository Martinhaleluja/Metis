using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Whether the interface moves, and who gets to decide.
///
/// The setting these cover was declared, shown in a checkbox, written to disk,
/// and read by nothing at all — its own documentation claimed it shortened the
/// companion's flight and the notch's unfurl, and neither was true. Someone who
/// ticked it got an identical application. These are the tests that make it
/// stay real.
/// </summary>
public sealed class MotionTuningTests : IDisposable
{
    // Static state, so every test puts it back. Reduced motion leaking from one
    // test into another would make failures depend on ordering.
    public void Dispose() => MotionTuning.Apply(false, true);

    [Fact]
    public void Motion_is_on_by_default()
    {
        MotionTuning.Apply(reduceMotionSetting: false, operatingSystemAllowsAnimation: true);

        Assert.False(MotionTuning.Reduced);
        Assert.Equal(1.0, MotionTuning.Pace);
    }

    [Fact]
    public void The_applications_own_setting_turns_it_off()
    {
        MotionTuning.Apply(reduceMotionSetting: true, operatingSystemAllowsAnimation: true);

        Assert.True(MotionTuning.Reduced);
    }

    /// <summary>
    /// The half that was missing entirely. Windows has had a "Show animations"
    /// switch for years and Metis ignored it, so a person who had already told
    /// their computer they did not want animation had to find and tick a second
    /// box in a second place.
    /// </summary>
    [Fact]
    public void The_operating_system_turns_it_off_on_its_own()
    {
        MotionTuning.Apply(reduceMotionSetting: false, operatingSystemAllowsAnimation: false);

        Assert.True(MotionTuning.Reduced);
    }

    /// <summary>
    /// The application may ask for less motion than Windows allows. It may never
    /// ask for more.
    /// </summary>
    [Fact]
    public void The_application_cannot_overrule_the_operating_system()
    {
        MotionTuning.Apply(reduceMotionSetting: false, operatingSystemAllowsAnimation: false);

        Assert.True(MotionTuning.Reduced);
    }

    /// <summary>
    /// Reduced means none, not brief. A sixty-millisecond slide is still a
    /// slide, and still the thing the setting was ticked to avoid.
    /// </summary>
    [Fact]
    public void Reduced_motion_is_no_motion_rather_than_quick_motion()
    {
        MotionTuning.Apply(true, true);

        Assert.Equal(TimeSpan.Zero, MotionTuning.Scale(TimeSpan.FromMilliseconds(420)));
        Assert.Equal(0, MotionTuning.ScaleMs(420));
    }

    [Fact]
    public void Normal_motion_passes_durations_through_unchanged()
    {
        MotionTuning.Apply(false, true);

        Assert.Equal(TimeSpan.FromMilliseconds(420), MotionTuning.Scale(TimeSpan.FromMilliseconds(420)));
        Assert.Equal(420, MotionTuning.ScaleMs(420));
    }

    // ------------------------------- Staggering -------------------------------

    [Fact]
    public void A_stagger_steps_evenly()
    {
        MotionTuning.Apply(false, true);

        Assert.Equal(0, MotionTuning.StaggerDelayMs(0));
        Assert.Equal(36, MotionTuning.StaggerDelayMs(1));
        Assert.Equal(72, MotionTuning.StaggerDelayMs(2));
    }

    /// <summary>
    /// A stagger reads as one considered arrival for about half a dozen items.
    /// Past that the last item's delay stops being a flourish and becomes a
    /// wait, and the list reads as being slowly typed out.
    /// </summary>
    [Fact]
    public void A_long_list_stops_staggering_rather_than_crawling()
    {
        MotionTuning.Apply(false, true);

        Assert.Equal(180, MotionTuning.StaggerDelayMs(5));
        Assert.Equal(0, MotionTuning.StaggerDelayMs(6));
        Assert.Equal(0, MotionTuning.StaggerDelayMs(40));
    }

    [Fact]
    public void Nothing_is_staggered_when_motion_is_off()
    {
        MotionTuning.Apply(true, true);

        Assert.Equal(0, MotionTuning.StaggerDelayMs(0));
        Assert.Equal(0, MotionTuning.StaggerDelayMs(3));
    }
}
