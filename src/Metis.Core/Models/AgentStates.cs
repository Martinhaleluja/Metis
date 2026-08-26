namespace Metis.Core.Models;

/// <summary>
/// The four Metis operating modes. The mode decides how much Metis teaches
/// versus how much it performs. It is enforced by <c>ModePolicy</c> and the
/// safety layer, separately from model reasoning, so a persuasive model
/// response can never widen what the current mode allows.
/// </summary>
/// <summary>
/// A vestige of when Metis had modes. It teaches and nothing else now, so the
/// one remaining value exists only because task and memory records still carry
/// it in their stored shape.
/// </summary>
public enum OperatingMode
{
    Guide
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public enum AgentState
{
    Idle,
    Listening,
    Understanding,
    Observing,
    Planning,
    WaitingForPermission,
    Executing,
    Verifying,
    Recovering,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// How the user reached Metis for this turn. The activation decides which
/// context is gathered: <see cref="Inspect"/> adds the exact control under the
/// pointer, while <see cref="Context"/> and <see cref="PushToTalk"/> rely on
/// the whole visible desktop.
/// </summary>
public enum ActivationKind
{
    Typed,
    PushToTalk,
    Context,
    Inspect,
    DirectAgent
}

/// <summary>
/// What Metis is allowed and expected to do in one mode. Every field is a
/// product decision from the mode behaviour matrix rather than a model choice.
/// </summary>
public sealed record ModeCapabilities(
    OperatingMode Mode,
    string DisplayName,
    string Summary,
    bool MayActUnprompted,
    bool MayActWhenAsked,
    int MaxActionsPerBatch,
    bool ExplainConcepts,
    bool ExplainWhy,
    bool TrackSkills,
    bool ShowVisualGuidance);

/// <summary>
/// The user's broader goal, carried across steps and across applications so a
/// change of active window does not reset what Metis is helping with.
/// </summary>
public sealed record AgentTaskState(
    string TaskId,
    string OriginalUserGoal,
    string CurrentObjective,
    int CurrentStep,
    IReadOnlyList<string> PreviousActions,
    IReadOnlyList<string> Observations,
    RiskLevel RiskLevel,
    int Retries,
    DateTimeOffset StartTime,
    string CurrentApplication,
    OperatingMode CurrentMode,
    string CompletionCriteria)
{
    public static AgentTaskState Start(string goal, string application, OperatingMode mode) => new(
        TaskId: Guid.NewGuid().ToString("n")[..12],
        OriginalUserGoal: goal,
        CurrentObjective: goal,
        CurrentStep: 0,
        PreviousActions: [],
        Observations: [],
        RiskLevel: RiskLevel.Low,
        Retries: 0,
        StartTime: DateTimeOffset.Now,
        CurrentApplication: application,
        CurrentMode: mode,
        CompletionCriteria: "The user confirms the goal is met or the screen visibly proves it.");
}
