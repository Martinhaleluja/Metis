namespace Metis.Core.Models;

/// <summary>
/// How well the user already handles one skill. Metis uses this to shorten or
/// drop guidance it has already given, so assistance decreases over time.
/// </summary>
public enum SkillLevel
{
    New,
    Learning,
    Beginner,
    Intermediate,
    Advanced,
    Mastered
}

/// <summary>
/// One learned capability, scoped to the application it was learned in.
/// Skill memory is deliberately separate from task memory: what the user knows
/// outlives what the user is currently doing.
/// </summary>
public sealed record SkillRecord
{
    public string Application { get; init; } = string.Empty;
    public string Skill { get; init; } = string.Empty;
    public SkillLevel Level { get; init; } = SkillLevel.New;
    public int SuccessfulUses { get; init; }
    public int GuidedUses { get; init; }
    public DateTimeOffset? LastUsed { get; init; }

    public string Key => $"{Application.Trim().ToLowerInvariant()}::{Skill.Trim().ToLowerInvariant()}";
}

/// <summary>
/// A goal Metis has worked on. Retained so a request such as "carry on" or a
/// switch to another application can resume the same goal.
/// </summary>
public sealed record TaskMemoryEntry
{
    public string TaskId { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public string Application { get; init; } = string.Empty;
    public OperatingMode Mode { get; init; } = OperatingMode.Guide;
    public IReadOnlyList<string> CompletedSteps { get; init; } = [];
    public string? PendingStep { get; init; }
    public bool Success { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// The whole persisted memory document. Structured data rather than a chat
/// transcript, so the user can inspect and delete individual parts.
/// </summary>
public sealed record MemoryDocument
{
    public IReadOnlyList<SkillRecord> Skills { get; init; } = [];
    public IReadOnlyList<TaskMemoryEntry> Tasks { get; init; } = [];
    public IReadOnlyDictionary<string, string> Preferences { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}


/// <summary>
/// What happened to a skill when it was practised. Returned so Metis can tell
/// the user they just got better at something — the whole point of a tool that
/// claims to build capability is that the progress is visible at the moment it
/// is earned, not buried in a file.
/// </summary>
public sealed record SkillProgress(SkillRecord Record, SkillLevel Previous)
{
    public bool LevelledUp => Record.Level > Previous;

    /// <summary>
    /// True once the user has shown they can do it without being walked
    /// through it, which is the milestone worth celebrating.
    /// </summary>
    public bool ReachedIndependence =>
        LevelledUp && Record.Level is SkillLevel.Advanced or SkillLevel.Mastered;
}
