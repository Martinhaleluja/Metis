using System.Globalization;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// What the update progress reports, and what it says.
///
/// The download had no feedback of any kind before this: <c>CopyToAsync</c>
/// reports nothing, the content length was never read, and a user on a slow
/// connection watched a dimmed button for minutes with no way to tell it from a
/// hang. These cover the parts that decide what a person actually reads.
/// </summary>
public sealed class UpdateProgressTests
{
    [Fact]
    public void A_known_total_gives_a_fraction()
    {
        var progress = new UpdateProgress(UpdatePhase.Downloading, 25_000_000, 100_000_000);

        Assert.Equal(0.25, progress.Fraction!.Value, 3);
    }

    /// <summary>
    /// A server that sends no content length is ordinary, not broken. The
    /// indicator has to fall back to an indeterminate state rather than invent a
    /// percentage, so the fraction must be genuinely absent.
    /// </summary>
    [Fact]
    public void No_total_means_no_fraction() =>
        Assert.Null(new UpdateProgress(UpdatePhase.Downloading, 5_000_000).Fraction);

    [Fact]
    public void A_zero_total_does_not_divide_by_zero() =>
        Assert.Null(new UpdateProgress(UpdatePhase.Downloading, 0, 0).Fraction);

    /// <summary>
    /// A server that under-reports its own length must not produce a bar past
    /// the end of its track.
    /// </summary>
    [Fact]
    public void More_bytes_than_promised_is_clamped() =>
        Assert.Equal(1.0, new UpdateProgress(UpdatePhase.Downloading, 120, 100).Fraction!.Value, 3);

    /// <summary>
    /// Megabytes to one decimal, not raw bytes. "23.4 MB of 61.8 MB" tells
    /// someone how long they are waiting; a count of bytes does not.
    /// </summary>
    [Fact]
    public void The_caption_is_in_units_a_person_reads()
    {
        var caption = new UpdateProgress(UpdatePhase.Downloading, 24_536_192, 64_800_000).Caption;

        // Built with the running culture rather than written out, because the
        // caption is deliberately localised: a reader in Johannesburg should see
        // "23,4 MB" and one in London "23.4 MB". Hard-coding the separator here
        // would fail on exactly the machines the product is right on.
        Assert.Contains(23.4.ToString("0.0", CultureInfo.CurrentCulture), caption, StringComparison.Ordinal);
        Assert.Contains(61.8.ToString("0.0", CultureInfo.CurrentCulture), caption, StringComparison.Ordinal);
        Assert.Contains("MB", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("24536192", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_total_still_says_how_far_it_has_got()
    {
        var caption = new UpdateProgress(UpdatePhase.Downloading, 5_242_880).Caption;

        Assert.Contains(5.0.ToString("0.0", CultureInfo.CurrentCulture), caption, StringComparison.Ordinal);
        Assert.DoesNotContain(" of ", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_downloaded_yet_says_so() =>
        Assert.Equal("Starting the download…", new UpdateProgress(UpdatePhase.Downloading).Caption);

    /// <summary>
    /// The phase that exists because it looked like a hang. Hashing a sixty
    /// megabyte installer happens after the bar has filled, so it needs to say
    /// something rather than sit at 100%.
    /// </summary>
    [Fact]
    public void Verifying_says_what_it_is_doing() =>
        Assert.Equal("Checking the download…", new UpdateProgress(UpdatePhase.Verifying).Caption);

    [Fact]
    public void A_failure_carries_its_own_reason() =>
        Assert.Equal(
            "The release could not be reached.",
            new UpdateProgress(UpdatePhase.Failed, Detail: "The release could not be reached.").Caption);

    [Fact]
    public void A_failure_with_no_reason_still_says_something() =>
        Assert.False(string.IsNullOrWhiteSpace(new UpdateProgress(UpdatePhase.Failed).Caption));

    /// <summary>
    /// Every phase has to render something. A blank caption beside a moving bar
    /// is the state this whole type exists to remove.
    /// </summary>
    [Theory]
    [InlineData(UpdatePhase.Checking)]
    [InlineData(UpdatePhase.Downloading)]
    [InlineData(UpdatePhase.Verifying)]
    [InlineData(UpdatePhase.Starting)]
    [InlineData(UpdatePhase.Failed)]
    public void Every_phase_has_something_to_say(UpdatePhase phase) =>
        Assert.False(string.IsNullOrWhiteSpace(new UpdateProgress(phase).Caption));
}
