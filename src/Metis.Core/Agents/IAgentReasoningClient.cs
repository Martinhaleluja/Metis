namespace Metis.Core.Agents;

/// <summary>
/// Structured reasoning response returned from an AI model during agent execution.
/// </summary>
public sealed record AgentModelResponse(
    string? Thought,
    string? ToolName,
    Dictionary<string, object?>? ToolArguments,
    string? FinalAnswer,
    bool IsDone);

/// <summary>
/// AI provider contract for driving autonomous agent ReAct/tool-calling steps.
/// </summary>
public interface IAgentReasoningClient
{
    /// <summary>
    /// Evaluates the current task goal and history, generating the next thought, tool call, or completion.
    /// </summary>
    Task<AgentModelResponse> GenerateNextStepAsync(
        string goal,
        IReadOnlyList<AgentStep> previousSteps,
        IReadOnlyList<AgentToolDeclaration> availableTools,
        string? systemPromptExtra,
        CancellationToken cancellationToken);
}
