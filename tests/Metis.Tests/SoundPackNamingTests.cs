using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// These are the exact file names from the sound pack in the repository, so a
/// rename that quietly stops matching fails here rather than going silent in
/// the product.
/// </summary>
public sealed class SoundPackNamingTests
{
    [Theory]
    [InlineData("app started.mp3", MetisSound.AppStarted)]
    [InlineData("Audio recording started.mp3", MetisSound.RecordingStarted)]
    [InlineData("Inspect keys pressed.mp3", MetisSound.InspectPressed)]
    [InlineData("inspect keys relesed.mp3", MetisSound.InspectReleased)]
    [InlineData("Task complete.mp3", MetisSound.TaskComplete)]
    [InlineData("saved settings.mp3", MetisSound.SettingsSaved)]
    [InlineData("stop metis.mp3", MetisSound.Stopped)]
    [InlineData("error 1.mp3", MetisSound.Error)]
    [InlineData("error 2.mp3", MetisSound.Error)]
    [InlineData("error 3.mp3", MetisSound.Error)]
    [InlineData("error 4.mp3", MetisSound.Error)]
    public void The_shipped_pack_file_names_all_match(string fileName, MetisSound expected) =>
        Assert.Equal(expected, SoundPackNaming.Match(fileName));

    [Fact]
    public void The_misspelling_of_released_still_matches()
    {
        // "relesed" is how the file is actually named. A cue failing silently
        // over a typo is a miserable thing to debug, so both spellings work.
        Assert.Equal(MetisSound.InspectReleased, SoundPackNaming.Match("inspect keys relesed.mp3"));
        Assert.Equal(MetisSound.InspectReleased, SoundPackNaming.Match("inspect keys released.wav"));
    }

    [Fact]
    public void Recording_wins_over_started_because_both_names_contain_it()
    {
        // "Audio recording started" and "app started" both contain "started";
        // the more specific subject has to be tested first.
        Assert.Equal(MetisSound.RecordingStarted, SoundPackNaming.Match("Audio recording started.mp3"));
        Assert.Equal(MetisSound.AppStarted, SoundPackNaming.Match("app started.mp3"));
    }

    /// <summary>
    /// The two account cues, and the reason their order in the matcher matters.
    ///
    /// "allowance used up" contains "up" and "used"; "plan complete" contains
    /// "complete". Both would be claimed by an earlier rule if these were tested
    /// after the general ones rather than before them.
    /// </summary>
    [Theory]
    [InlineData("limit reached.mp3", MetisSound.LimitReached)]
    [InlineData("Allowance used up.wav", MetisSound.LimitReached)]
    [InlineData("quota-2.mp3", MetisSound.LimitReached)]
    [InlineData("plan changed.mp3", MetisSound.PlanChanged)]
    [InlineData("Upgrade.WAV", MetisSound.PlanChanged)]
    [InlineData("subscription active.mp3", MetisSound.PlanChanged)]
    public void The_account_cues_match_their_names(string fileName, MetisSound expected) =>
        Assert.Equal(expected, SoundPackNaming.Match(fileName));

    [Theory]
    [InlineData("ERROR 1.WAV")]
    [InlineData("Error_07.mp3")]
    [InlineData("  error-3.MP3  ")]
    public void Case_numbering_and_separators_do_not_affect_matching(string fileName) =>
        Assert.Equal(MetisSound.Error, SoundPackNaming.Match(fileName));

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("untitled.mp3")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_name_matches_nothing(string? fileName) =>
        Assert.Null(SoundPackNaming.Match(fileName));

    [Fact]
    public void Every_sound_has_at_least_one_name_that_selects_it()
    {
        var reachable = new[]
        {
            "app started", "audio recording started", "inspect keys pressed", "inspect keys released",
            "request sent", "task complete", "saved settings", "stop metis", "error",
            "plan changed", "limit reached"
        }.Select(SoundPackNaming.Match).ToHashSet();

        foreach (var sound in Enum.GetValues<MetisSound>())
        {
            Assert.Contains(sound, reachable);
        }
    }
}
