using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class SkillMemoryEngineTests
{
    private static readonly SkillRecord Fresh = new()
    {
        Application = "FL Studio",
        Skill = "Mixer routing"
    };

    [Fact]
    public void A_guided_success_advances_the_skill_but_not_to_mastery()
    {
        var record = Fresh;
        for (var use = 0; use < 8; use++)
        {
            record = SkillMemoryEngine.Record(record, succeeded: true, neededGuidance: true);
        }

        Assert.Equal(8, record.SuccessfulUses);
        Assert.Equal(8, record.GuidedUses);
        Assert.Equal(SkillLevel.Beginner, record.Level);
    }

    [Fact]
    public void Repeated_unguided_successes_reach_mastery()
    {
        var record = Fresh;
        for (var use = 0; use < 6; use++)
        {
            record = SkillMemoryEngine.Record(record, succeeded: true, neededGuidance: false);
        }

        Assert.Equal(SkillLevel.Mastered, record.Level);
        Assert.Equal("no unsolicited explanation", SkillMemoryEngine.GuidanceDepth(record.Level));
    }

    [Fact]
    public void A_skill_never_regresses_after_one_guided_session()
    {
        var mastered = Fresh with { Level = SkillLevel.Mastered, SuccessfulUses = 6 };

        var afterSetback = SkillMemoryEngine.Record(mastered, succeeded: false, neededGuidance: true);

        Assert.Equal(SkillLevel.Mastered, afterSetback.Level);
    }

    [Fact]
    public void A_failed_first_attempt_leaves_the_skill_unproven()
    {
        var record = SkillMemoryEngine.Record(Fresh, succeeded: false, neededGuidance: true);

        Assert.Equal(0, record.SuccessfulUses);
        Assert.Equal(SkillLevel.New, record.Level);
    }

    [Fact]
    public void The_digest_puts_the_active_application_first()
    {
        SkillRecord[] skills =
        [
            new() { Application = "Photoshop", Skill = "Layer masks", Level = SkillLevel.Advanced, LastUsed = DateTimeOffset.Now },
            new() { Application = "FL Studio", Skill = "Sidechain", Level = SkillLevel.Learning, LastUsed = DateTimeOffset.Now.AddDays(-3) }
        ];

        var digest = SkillMemoryEngine.Describe(skills, "FL Studio 21 - project.flp");

        Assert.StartsWith("FL Studio/Sidechain", digest, StringComparison.Ordinal);
        Assert.Contains("Photoshop/Layer masks", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_memory_says_so_rather_than_returning_an_empty_string() =>
        Assert.Equal("none recorded yet", SkillMemoryEngine.Describe([], "Anything"));
}
