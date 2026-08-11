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
/// Structured, user-inspectable memory. Skill memory (what the user can do) is
/// intentionally separate from task memory (what the user is doing now).
/// </summary>
public interface IMemoryService
{
    string MemoryPath { get; }

    Task<MemoryDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task RecordSkillUseAsync(
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
