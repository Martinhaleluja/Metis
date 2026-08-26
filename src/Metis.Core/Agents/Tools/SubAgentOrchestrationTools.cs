using Metis.Core.Agents;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Delegate hooks providing sub-agent management and orchestration to agent tools.
/// </summary>
public sealed class SubAgentOrchestrationHooks
{
    /// <summary>
    /// Spawns a helper for a running agent: goal, template, working directory,
    /// and the id of the agent asking. The last one matters — without knowing
    /// who the parent is there is no way to tell how deep the chain already
    /// goes, and an agent that can ask for a helper can ask indefinitely.
    /// </summary>
    public Func<string, string?, string?, string, Task<AgentTaskRecord>>? SpawnWorkerAsync { get; init; }
    public Func<string, AgentTaskRecord?>? GetWorkerStatus { get; init; }
    public Func<IReadOnlyList<AgentTaskRecord>>? ListActiveWorkers { get; init; }
    public Action<string>? CancelWorker { get; init; }
}

/// <summary>
/// Tool that allows the companion agent to spawn a dedicated autonomous background worker agent for asynchronous tasks.
/// </summary>
public sealed class SpawnBackgroundWorkerTool : IAgentTool
{
    private readonly SubAgentOrchestrationHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "spawn_background_worker",
        Description: "Spawns an autonomous background worker agent to handle tasks like file organization, web research, or script execution without blocking companion chat.",
        Category: "orchestration",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("goal", "string", "The autonomous goal or objective for the worker to achieve", Required: true),
            new("template_id", "string", "Optional preset template ID (e.g., 'research', 'file_organizer')", Required: false),
            new("working_directory", "string", "Working directory for the worker (optional)", Required: false)
        ]);

    public SpawnBackgroundWorkerTool(SubAgentOrchestrationHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var goal = arguments.GetValueOrDefault("goal")?.ToString();
        if (string.IsNullOrWhiteSpace(goal))
        {
            return AgentToolResult.Fail("Parameter 'goal' is required.");
        }

        var templateId = arguments.GetValueOrDefault("template_id")?.ToString();
        var workingDir = arguments.GetValueOrDefault("working_directory")?.ToString() ?? context.WorkingDirectory;

        if (_hooks.SpawnWorkerAsync is null)
        {
            return AgentToolResult.Ok($"Background worker scheduled for goal: '{goal}' (TaskId: agent-simulated).");
        }

        try
        {
            var task = await _hooks.SpawnWorkerAsync(goal, templateId, workingDir, context.TaskId);
            var output = $"Spawned autonomous background worker [{task.Id}] for goal: '{task.Goal}'. Status: {task.Status}.";
            return AgentToolResult.Ok(output);
        }
        catch (Exception ex)
        {
            return AgentToolResult.Fail($"Failed to spawn worker agent: {ex.Message}");
        }
    }
}

/// <summary>
/// Tool that checks the progress and status of a running or completed background worker task.
/// </summary>
public sealed class CheckWorkerStatusTool : IAgentTool
{
    private readonly SubAgentOrchestrationHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "check_worker_status",
        Description: "Checks the status, progress, latest activity, and generated artifacts of a background worker agent.",
        Category: "orchestration",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("task_id", "string", "The task ID of the background worker agent", Required: true)
        ]);

    public CheckWorkerStatusTool(SubAgentOrchestrationHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var taskId = arguments.GetValueOrDefault("task_id")?.ToString();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'task_id' is required."));
        }

        if (_hooks.GetWorkerStatus is null)
        {
            return Task.FromResult(AgentToolResult.Ok($"Worker [{taskId}] status: Running (simulated)."));
        }

        var task = _hooks.GetWorkerStatus(taskId);
        if (task is null)
        {
            return Task.FromResult(AgentToolResult.Fail($"No worker task found with ID '{taskId}'."));
        }

        var summary = $"Worker [{task.Id}]: {task.Status} ({task.Progress:P0})\n" +
                      $"Goal: {task.Goal}\n" +
                      $"Current Activity: {task.CurrentActivity ?? "None"}\n" +
                      $"Steps Completed: {task.AllSteps.Count(s => s.Status == AgentStepStatus.Success)} / {task.AllSteps.Count}\n" +
                      $"Artifacts: {task.AllArtifacts.Count}";

        if (!string.IsNullOrWhiteSpace(task.ResultSummary))
        {
            summary += $"\nResult Summary: {task.ResultSummary}";
        }

        if (!string.IsNullOrWhiteSpace(task.ErrorMessage))
        {
            summary += $"\nError: {task.ErrorMessage}";
        }

        return Task.FromResult(AgentToolResult.Ok(summary));
    }
}

/// <summary>
/// Tool that lists all active background worker sub-agents.
/// </summary>
public sealed class ListWorkersTool : IAgentTool
{
    private readonly SubAgentOrchestrationHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "list_workers",
        Description: "Lists all currently active or recently spawned background worker agents.",
        Category: "orchestration",
        RiskLevel: AgentRiskLevel.Low,
        Parameters: []);

    public ListWorkersTool(SubAgentOrchestrationHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        if (_hooks.ListActiveWorkers is null)
        {
            return Task.FromResult(AgentToolResult.Ok("No active worker tracking configured."));
        }

        try
        {
            var workers = _hooks.ListActiveWorkers();
            if (workers.Count == 0)
            {
                return Task.FromResult(AgentToolResult.Ok("No active background worker agents running."));
            }

            var lines = workers.Select(w =>
                $"- [{w.Id}] {w.Status} ({w.Progress:P0}) - Goal: \"{w.Goal}\" (Activity: {w.CurrentActivity ?? "None"}, Artifacts: {w.AllArtifacts.Count})");

            var output = $"Active Background Workers ({workers.Count}):\n" + string.Join("\n", lines);
            return Task.FromResult(AgentToolResult.Ok(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to list workers: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool that cancels a running background worker sub-agent.
/// </summary>
public sealed class CancelWorkerTool : IAgentTool
{
    private readonly SubAgentOrchestrationHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "cancel_worker",
        Description: "Cancels or stops a running background worker agent task.",
        Category: "orchestration",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("task_id", "string", "The task ID of the worker agent to cancel", Required: true)
        ]);

    public CancelWorkerTool(SubAgentOrchestrationHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var taskId = arguments.GetValueOrDefault("task_id")?.ToString();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'task_id' is required."));
        }

        if (_hooks.CancelWorker is null)
        {
            return Task.FromResult(AgentToolResult.Ok($"Cancellation requested for worker [{taskId}] (simulated)."));
        }

        try
        {
            _hooks.CancelWorker(taskId);
            return Task.FromResult(AgentToolResult.Ok($"Cancellation signal sent to worker [{taskId}]."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to cancel worker: {ex.Message}"));
        }
    }
}
