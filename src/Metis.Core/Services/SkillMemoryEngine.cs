using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Advances and describes what the user already knows. Progression is
/// deliberately slow at the top end: Metis should stop explaining only once a
/// skill has been used successfully several times without guidance.
/// </summary>
public static class SkillMemoryEngine
{
    /// <summary>
    /// Records one use of a skill. <paramref name="neededGuidance"/> means Metis
    /// had to explain or point; an unguided success is what actually moves the
    /// user up a level.
    /// </summary>
    public static SkillRecord Record(SkillRecord existing, bool succeeded, bool neededGuidance)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var successfulUses = existing.SuccessfulUses + (succeeded ? 1 : 0);
        var guidedUses = existing.GuidedUses + (neededGuidance ? 1 : 0);
        var unguidedSuccesses = Math.Max(0, successfulUses - guidedUses);

        return existing with
        {
            SuccessfulUses = successfulUses,
            GuidedUses = guidedUses,
            LastUsed = DateTimeOffset.Now,
            Level = LevelFor(successfulUses, unguidedSuccesses, existing.Level)
        };
    }

    private static SkillLevel LevelFor(int successfulUses, int unguidedSuccesses, SkillLevel current)
    {
        var earned = (successfulUses, unguidedSuccesses) switch
        {
            (0, _) => SkillLevel.New,
            (_, >= 6) => SkillLevel.Mastered,
            (_, >= 4) => SkillLevel.Advanced,
            (_, >= 2) => SkillLevel.Intermediate,
            (>= 3, _) => SkillLevel.Beginner,
            _ => SkillLevel.Learning
        };

        // A skill never silently regresses; forgetting is the user's to declare
        // by clearing memory, not something Metis infers from one bad session.
        return earned > current ? earned : current;
    }

    /// <summary>
    /// How much explanation a skill still deserves. This is what turns a full
    /// walkthrough into a one-line hint and eventually into silence.
    /// </summary>
    public static string GuidanceDepth(SkillLevel level) => level switch
    {
        SkillLevel.New or SkillLevel.Learning => "full explanation with a visual demonstration",
        SkillLevel.Beginner => "short instruction with a pointer to the control",
        SkillLevel.Intermediate => "brief reminder only",
        SkillLevel.Advanced => "a one-word hint if anything",
        _ => "no unsolicited explanation"
    };

    /// <summary>
    /// A compact, readable digest of the skills relevant to one application,
    /// small enough to include in every request.
    /// </summary>
    public static string Describe(IReadOnlyList<SkillRecord> skills, string? application, int maxSkills = 12)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (skills.Count == 0)
        {
            return "none recorded yet";
        }

        var relevant = skills
            .OrderByDescending(skill => MatchesApplication(skill, application))
            .ThenByDescending(skill => skill.LastUsed ?? DateTimeOffset.MinValue)
            .Take(maxSkills)
            .Select(skill => $"{skill.Application}/{skill.Skill}: {skill.Level} ({GuidanceDepth(skill.Level)})");

        return string.Join("; ", relevant);
    }

    private static bool MatchesApplication(SkillRecord skill, string? application) =>
        !string.IsNullOrWhiteSpace(application) &&
        !string.IsNullOrWhiteSpace(skill.Application) &&
        application.Contains(skill.Application, StringComparison.OrdinalIgnoreCase);
}
