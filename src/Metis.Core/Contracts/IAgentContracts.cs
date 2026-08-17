using Metis.Core.Models;

namespace Metis.Core.Contracts;

public interface ISafetyPolicyEngine
{
    RiskLevel ClassifyRisk(DesktopAction action);

    /// <summary>
    /// Decides whether one action may run. <paramref name="userAskedForAction"/>
    /// comes from the user's own words for this turn, never from the model's
    /// claim about them.
    /// </summary>
    bool IsPermitted(DesktopAction action, OperatingMode mode, bool userAskedForAction, out string reason);

    bool RequiresUserConfirmation(DesktopAction action, OperatingMode mode);
}

public interface IActionVerificationEngine
{
    Task<DesktopActionResult> VerifyAsync(DesktopAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns what the model said it was talking about into a rectangle on the real
/// screen.
///
/// This is the step that decides whether an annotation is trusted or merely
/// plausible. A model estimating coordinates from a resized screenshot is
/// routinely tens of pixels out, which is the difference between marking the
/// Save button and marking the gap beside it — and a mark in the gap is worse
/// than no mark, because the user believes it. Windows knows exactly where its
/// own windows, controls, and text runs are, so wherever it can answer, its
/// answer replaces the estimate.
/// </summary>
public interface IAnnotationResolver
{
    /// <summary>
    /// Resolves one target against the screen. Returns null only when there is
    /// nothing to draw at all; a target that could not be improved comes back
    /// with <see cref="AnnotationSource.Estimated"/> rather than nothing, so a
    /// missing accessibility tree costs precision and not the annotation.
    /// </summary>
    Task<ResolvedAnnotation?> ResolveAsync(
        AnnotationTarget target,
        ScreenCapture capture,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Structured, user-inspectable memory. Skill memory (what the user can do) is
/// intentionally separate from task memory (what the user is doing now).
/// </summary>
public interface IMemoryService
{
    string MemoryPath { get; }

    Task<MemoryDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one practice of a skill and reports what it did to the user's
    /// level, so a milestone can be shown as it is reached.
    /// </summary>
    Task<SkillProgress?> RecordSkillUseAsync(
        string application,
        string skill,
        bool succeeded,
        bool neededGuidance,
        CancellationToken cancellationToken = default);

    Task RecordTaskOutcomeAsync(
        AgentTaskState state,
        bool success,
        string summary,
        CancellationToken cancellationToken = default);

    Task<string?> GetPreferenceAsync(string key, CancellationToken cancellationToken = default);

    Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
