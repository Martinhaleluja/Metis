using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The update check decides whether to download and run an installer, so the
/// comparison behind it is worth pinning down — particularly the two ways a
/// naive version check goes wrong: comparing as text, so 10 sorts below 9, and
/// choking on the decorations these strings arrive with.
/// </summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("3.5.0", "3.4.0")]
    [InlineData("3.4.1", "3.4.0")]
    [InlineData("4.0.0", "3.99.99")]
    [InlineData("3.10.0", "3.9.0")]
    public void A_higher_version_is_newer(string candidate, string current)
    {
        Assert.True(AppVersion.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("3.4.0", "3.4.0")]
    [InlineData("3.3.0", "3.4.0")]
    [InlineData("3.4.0", "3.4.1")]
    public void The_same_or_older_version_is_not(string candidate, string current)
    {
        Assert.False(AppVersion.IsNewer(candidate, current));
    }

    [Fact]
    public void Ten_is_newer_than_nine_rather_than_alphabetically_smaller()
    {
        // The bug this exists to prevent: as text, "3.10.0" < "3.9.0".
        Assert.True(AppVersion.IsNewer("3.10.0", "3.9.0"));
        Assert.False(AppVersion.IsNewer("3.9.0", "3.10.0"));
    }

    [Fact]
    public void A_git_tag_style_v_prefix_is_understood()
    {
        Assert.True(AppVersion.IsNewer("v3.5.0", "3.4.0"));
        Assert.Equal(new Version(3, 5, 0, 0), AppVersion.Parse("v3.5.0"));
    }

    [Fact]
    public void The_sdk_source_revision_suffix_is_ignored()
    {
        // InformationalVersion arrives as "3.4.0+9a1c2f3" by default.
        Assert.Equal(new Version(3, 4, 0, 0), AppVersion.Parse("3.4.0+9a1c2f3"));
        Assert.False(AppVersion.IsNewer("3.4.0+9a1c2f3", "3.4.0"));
    }

    [Fact]
    public void A_two_part_version_equals_its_three_part_spelling()
    {
        Assert.False(AppVersion.IsNewer("3.4", "3.4.0"));
        Assert.False(AppVersion.IsNewer("3.4.0", "3.4"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nightly")]
    [InlineData("latest")]
    public void An_unreadable_version_never_triggers_an_update(string? candidate)
    {
        // Staying on a working build beats downloading an installer because of
        // a string nobody could interpret.
        Assert.False(AppVersion.IsNewer(candidate, "3.4.0"));
        Assert.Null(AppVersion.Parse(candidate));
    }

    [Fact]
    public void An_unreadable_running_version_also_holds_the_update_back()
    {
        Assert.False(AppVersion.IsNewer("3.5.0", "not-a-version"));
    }

    [Fact]
    public void The_running_build_reports_something_parseable()
    {
        Assert.NotNull(AppVersion.Parse(AppVersion.Current));
    }
}
