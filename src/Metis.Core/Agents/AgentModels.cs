using System.Text.Json.Serialization;

namespace Metis.Core.Agents;

/// <summary>
/// The lifecycle status of an autonomous background agent task.
/// </summary>
public enum AgentTaskStatus
{
    Queued,
    Planning,
    Running,
    AwaitingApproval,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// The risk level associated with an agent tool or action.
/// </summary>
public enum AgentRiskLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// Who asked for an agent.
///
/// This is not bookkeeping. An agent can write files and run commands, and how
/// much latitude that deserves depends entirely on where the goal came from. A
/// goal the user typed into the spawn panel is their own words. A goal the
/// model wrote is an inference drawn from a conversation that also contains up
/// to a hundred thousand characters of whatever happened to be on screen, so it
/// gets less rope no matter what the autonomy setting says.
/// </summary>
public enum AgentSpawnOrigin
{
    /// <summary>The user filled in the spawn panel themselves.</summary>
    Panel,

    /// <summary>The user typed /spawn or /agent with the goal attached.</summary>
    SlashCommand,

    /// <summary>Metis read the request and proposed it; the user confirmed.</summary>
    ModelProposed,

    /// <summary>Another agent asked for a helper.</summary>
    SubAgent
}

/// <summary>
/// The status of an individual step in an agent's execution plan.
/// </summary>
public enum AgentStepStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Skipped
}

/// <summary>
/// A single step or subgoal executed by an agent.
/// </summary>
public sealed record AgentStep(
    string Id,
    string Description,
    AgentStepStatus Status,
    string? ToolName = null,
    string? ToolArguments = null,
    string? ToolResult = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? ErrorMessage = null,
    bool IsVerification = false);

/// <summary>
/// An append-only log entry produced during agent execution.
/// </summary>
public sealed record AgentLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Details = null);

/// <summary>
/// A file or structured data output produced by an agent task.
/// </summary>
public sealed record AgentArtifact(
    string Id,
    string Name,
    string FilePath,
    string MimeType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string? Summary = null);

/// <summary>
/// A pending approval request when an agent attempts a high-risk tool call.
/// </summary>
public sealed record AgentApprovalRequest(
    string TaskId,
    string ToolName,
    string Arguments,
    string Reason,
    AgentRiskLevel RiskLevel,
    DateTimeOffset RequestedAt);

/// <summary>
/// Preset template for spawning specialized agents.
/// </summary>
public sealed record AgentTaskTemplate(
    string Id,
    string Name,
    string Description,
    string Icon,
    IReadOnlyList<string> EnabledToolCategories,
    string SystemPromptExtra);

/// <summary>
/// Parameter definition for an agent tool, compatible with JSON Schema.
/// </summary>
public sealed record AgentToolParameter(
    string Name,
    string Type,
    string Description,
    bool Required = true,
    object? DefaultValue = null,
    IReadOnlyList<string>? EnumValues = null);

/// <summary>
/// Declaration metadata describing an agent tool.
/// </summary>
public sealed record AgentToolDeclaration(
    string Name,
    string Description,
    string Category,
    AgentRiskLevel RiskLevel,
    IReadOnlyList<AgentToolParameter> Parameters);

/// <summary>
/// The result returned from executing an agent tool.
/// </summary>
public sealed record AgentToolResult(
    bool Success,
    string Output,
    string? ErrorMessage = null,
    AgentArtifact? Artifact = null,
    AgentRiskLevel RiskLevel = AgentRiskLevel.Low)
{
    public static AgentToolResult Ok(string output, AgentArtifact? artifact = null) =>
        new(true, output, null, artifact);

    public static AgentToolResult Fail(string errorMessage) =>
        new(false, string.Empty, errorMessage);
}

/// <summary>
/// Full persistent record of an autonomous background agent task.
/// </summary>
public sealed record AgentTaskRecord(
    string Id,
    string Goal,
    AgentTaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? TemplateId = null,
    string? WorkingDirectory = null,
    float Progress = 0f,
    string? CurrentActivity = null,
    IReadOnlyList<AgentStep>? Steps = null,
    IReadOnlyList<AgentLogEntry>? Logs = null,
    IReadOnlyList<AgentArtifact>? Artifacts = null,
    AgentApprovalRequest? PendingApproval = null,
    string? ResultSummary = null,
    string? ErrorMessage = null,
    int MaxTurns = 100,
    bool IsVerified = false,

    /// <summary>Where this agent's goal came from. Drives how much it may do unattended.</summary>
    AgentSpawnOrigin Origin = AgentSpawnOrigin.Panel,

    /// <summary>
    /// How many agents deep this one is. A top-level agent is 0; one it spawned
    /// is 1. Without this an agent could ask for a helper that asks for a
    /// helper, indefinitely, and nothing in the system would stop it.
    /// </summary>
    int Depth = 0,

    /// <summary>
    /// Whether this agent may read and write outside its own workspace.
    ///
    /// False unless the user chose one of their own folders when spawning it,
    /// which is the only way it should ever become true: an agent given a goal
    /// in passing has no business in Documents, and one pointed deliberately at
    /// a folder plainly does.
    /// </summary>
    bool AllowOutsideWorkspace = false)
{
    public IReadOnlyList<AgentStep> AllSteps => Steps ?? [];
    public IReadOnlyList<AgentLogEntry> AllLogs => Logs ?? [];
    public IReadOnlyList<AgentArtifact> AllArtifacts => Artifacts ?? [];
    public bool IsActive => Status is AgentTaskStatus.Queued
        or AgentTaskStatus.Planning
        or AgentTaskStatus.Running
        or AgentTaskStatus.AwaitingApproval
        or AgentTaskStatus.Paused;
}
