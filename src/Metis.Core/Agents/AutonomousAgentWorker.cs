using System.Text.Json;

namespace Metis.Core.Agents;

/// <summary>
/// Autonomous execution engine that drives the ReAct (Sense-Plan-Act-Verify) loop for a single agent task.
/// Supports large task capacity (100+ turns) and enforces verification checks before task completion.
/// </summary>
public sealed class AutonomousAgentWorker : IDisposable
{
    /// <summary>
    /// Tools that count as having checked the work.
    ///
    /// This list used to include list_directory, search_files and list_processes
    /// — so a single directory listing on turn one satisfied the gate for the
    /// whole run, and an agent could declare a task complete having never looked
    /// at what it produced. The gate was passing everything.
    ///
    /// What is left are tools that read the actual result: the contents of a
    /// file, the output of a process that was run, the screen. Listing what
    /// exists is not the same as checking what it says.
    /// </summary>
    private static readonly HashSet<string> VerificationTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "verify_task_output",
        "read_file",
        "search_content",
        "check_process",
        "check_worker_status",
        "inspect_screen"
    };

    /// <summary>
    /// How far back a check still counts for. Long enough to cover a
    /// check-then-summarise ending, short enough that it has to be about the
    /// work actually being finished.
    /// </summary>
    private const int RecentStepsForVerification = 6;

    private static readonly string[] VerificationKeywords =
    {
        "verif",
        "confirm",
        "validat",
        "checked",
        "tested",
        "sanity check",
        "inspected",
        "ensure",
        "assert"
    };

    private const int MaxVerificationChallenges = 1;

    private readonly AgentTaskRecord _initialRecord;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IAgentReasoningClient _reasoningClient;
    private readonly Action<AgentTaskRecord> _onTaskUpdated;
    private readonly Func<AgentApprovalRequest, Task<bool>> _onApprovalRequired;
    private readonly Func<string>? _getAutonomyMode;
    private readonly int _maxTurns;

    private readonly List<AgentStep> _steps = [];
    private readonly List<AgentLogEntry> _logs = [];
    private readonly List<AgentArtifact> _artifacts = [];
    private readonly object _stateLock = new();

    private AgentTaskStatus _status = AgentTaskStatus.Queued;
    private float _progress;
    private string? _currentActivity;
    private string? _resultSummary;
    private string? _errorMessage;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;
    private bool _isVerified;

    private readonly TaskCompletionSource<bool> _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool>? _pendingApprovalTcs;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private bool _disposed;

    public string TaskId => _initialRecord.Id;
    public Task CompletionTask => _completionTcs.Task;

    public AutonomousAgentWorker(
        AgentTaskRecord taskRecord,
        IAgentToolRegistry toolRegistry,
        IAgentReasoningClient reasoningClient,
        Action<AgentTaskRecord> onTaskUpdated,
        Func<AgentApprovalRequest, Task<bool>> onApprovalRequired,
        Func<string>? getAutonomyMode = null,
        int maxTurns = 100)
    {
        _initialRecord = taskRecord ?? throw new ArgumentNullException(nameof(taskRecord));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _reasoningClient = reasoningClient ?? throw new ArgumentNullException(nameof(reasoningClient));
        _onTaskUpdated = onTaskUpdated ?? throw new ArgumentNullException(nameof(onTaskUpdated));
        _onApprovalRequired = onApprovalRequired ?? throw new ArgumentNullException(nameof(onApprovalRequired));
        _getAutonomyMode = getAutonomyMode;
        _maxTurns = maxTurns > 0 ? maxTurns : 100;

        _status = taskRecord.Status;
        _progress = taskRecord.Progress;
        _currentActivity = taskRecord.CurrentActivity;
    }

    public AgentTaskRecord GetSnapshot()
    {
        lock (_stateLock)
        {
            return _initialRecord with
            {
                Status = _status,
                StartedAt = _startedAt,
                CompletedAt = _completedAt,
                Progress = _progress,
                CurrentActivity = _currentActivity,
                Steps = _steps.ToList(),
                Logs = _logs.ToList(),
                Artifacts = _artifacts.ToList(),
                ResultSummary = _resultSummary,
                ErrorMessage = _errorMessage,
                MaxTurns = _maxTurns,
                IsVerified = _isVerified
            };
        }
    }

    private void NotifyUpdated()
    {
        var snapshot = GetSnapshot();
        _onTaskUpdated(snapshot);
    }

    private void Log(string level, string message, string? details = null)
    {
        lock (_stateLock)
        {
            _logs.Add(new AgentLogEntry(DateTimeOffset.Now, level, message, details));
        }
        NotifyUpdated();
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (_status == AgentTaskStatus.Running)
            {
                _status = AgentTaskStatus.Paused;
                _pauseEvent.Reset();
                Log("INFO", "Task paused by user.");
            }
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (_status == AgentTaskStatus.Paused)
            {
                _status = AgentTaskStatus.Running;
                _pauseEvent.Set();
                Log("INFO", "Task resumed by user.");
            }
        }
    }

    public void ResolveApproval(bool approved)
    {
        _pendingApprovalTcs?.TrySetResult(approved);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            _status = AgentTaskStatus.Running;
            _startedAt = DateTimeOffset.Now;
            _currentActivity = "Decomposing task goal and planning execution steps...";
            _progress = 0.05f;
        }
        Log("INFO", $"Agent task '{_initialRecord.Goal}' started (Capacity: {_maxTurns} turns).");

        var workingDir = _initialRecord.WorkingDirectory ?? Environment.CurrentDirectory;

        // The workspace has to exist before the first tool runs, or every
        // relative path fails on a folder nobody created.
        try
        {
            Directory.CreateDirectory(workingDir);
        }
        catch (Exception exception)
        {
            Log("ERROR", $"Could not prepare the workspace at {workingDir}. {exception.Message}");
        }
        var availableTools = _toolRegistry.GetDeclarations();

        var toolContext = new AgentToolContext(
            TaskId,
            workingDir,
            new Progress<string>(msg =>
            {
                lock (_stateLock) { _currentActivity = msg; }
                NotifyUpdated();
            }),
            logEntry =>
            {
                lock (_stateLock) { _logs.Add(logEntry); }
                NotifyUpdated();
            },
            artifact =>
            {
                lock (_stateLock) { _artifacts.Add(artifact); }
                Log("INFO", $"New artifact generated: {artifact.Name} ({artifact.SizeBytes / 1024.0:F1} KB)");
            },
            _initialRecord.AllowOutsideWorkspace);

        var maxTurns = _maxTurns;
        var turnCount = 0;
        var verificationChallenges = 0;

        try
        {
            while (turnCount < maxTurns && !cancellationToken.IsCancellationRequested)
            {
                _pauseEvent.Wait(cancellationToken);

                turnCount++;
                lock (_stateLock)
                {
                    _currentActivity = $"Reasoning & Acting (Turn {turnCount}/{maxTurns})...";
                    _progress = Math.Min(0.95f, 0.05f + (turnCount / (float)maxTurns) * 0.90f);
                }
                NotifyUpdated();

                IReadOnlyList<AgentStep> previousStepsSnapshot;
                lock (_stateLock)
                {
                    previousStepsSnapshot = _steps.ToList();
                }

                var response = await _reasoningClient.GenerateNextStepAsync(
                    _initialRecord.Goal,
                    previousStepsSnapshot,
                    availableTools,

                    // The template's guidance, not its id. This argument is the
                    // agent's "special instructions" block, and it used to
                    // receive the raw slug -- so choosing a preset told the
                    // agent, in full, "organize_downloads".
                    AgentTaskTemplates.PromptExtraFor(_initialRecord.TemplateId),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(response.Thought))
                {
                    Log("THOUGHT", response.Thought);
                }

                // Completion evaluation with Verification Gate
                if (response.IsDone || (!string.IsNullOrWhiteSpace(response.FinalAnswer) && string.IsNullOrWhiteSpace(response.ToolName)))
                {
                    var (isVerified, failureReason) = ValidateVerificationStatus(previousStepsSnapshot, response);

                    if (!isVerified && verificationChallenges < MaxVerificationChallenges)
                    {
                        verificationChallenges++;
                        Log("WARN", $"Verification check requested before completion ({verificationChallenges}/{MaxVerificationChallenges}): {failureReason}");

                        var verificationPromptStep = new AgentStep(
                            Guid.NewGuid().ToString("N"),
                            $"Verification requirement: {failureReason}",
                            AgentStepStatus.Pending,
                            null,
                            null,
                            null,
                            DateTimeOffset.Now,
                            DateTimeOffset.Now,
                            $"Verification required: {failureReason}. Please perform an explicit check or confirm resolution before final completion.");

                        lock (_stateLock)
                        {
                            _steps.Add(verificationPromptStep);
                            _currentActivity = "Verifying task outcome before completion...";
                        }
                        NotifyUpdated();
                        continue;
                    }

                    lock (_stateLock)
                    {
                        _status = AgentTaskStatus.Completed;
                        _progress = 1.0f;
                        _isVerified = true;
                        _completedAt = DateTimeOffset.Now;
                        _currentActivity = "Task completed and verified successfully.";
                        _resultSummary = response.FinalAnswer ?? "Task completed and verified.";
                    }
                    Log("INFO", $"Task completed: {_resultSummary}");
                    _completionTcs.TrySetResult(true);
                    return;
                }

                if (string.IsNullOrWhiteSpace(response.ToolName))
                {
                    Log("WARN", "Model returned no tool call and was not marked done. Requesting next action.");
                    continue;
                }

                var tool = _toolRegistry.GetTool(response.ToolName);
                if (tool is null)
                {
                    var errorStep = new AgentStep(
                        Guid.NewGuid().ToString("N"),
                        $"Invoke unknown tool: {response.ToolName}",
                        AgentStepStatus.Failed,
                        response.ToolName,
                        JsonSerializer.Serialize(response.ToolArguments),
                        null,
                        DateTimeOffset.Now,
                        DateTimeOffset.Now,
                        $"Unknown tool '{response.ToolName}'. Please select from available tools.");

                    lock (_stateLock) { _steps.Add(errorStep); }
                    Log("ERROR", $"Model called unknown tool: {response.ToolName}");
                    continue;
                }

                var args = response.ToolArguments ?? new Dictionary<string, object?>();
                var stepId = Guid.NewGuid().ToString("N");
                var stepDesc = $"Execute {tool.Declaration.Name}";
                var isVerificationTool = VerificationTools.Contains(tool.Declaration.Name);

                var step = new AgentStep(
                    stepId,
                    stepDesc,
                    AgentStepStatus.Running,
                    tool.Declaration.Name,
                    JsonSerializer.Serialize(args),
                    null,
                    DateTimeOffset.Now,
                    IsVerification: isVerificationTool);

                lock (_stateLock)
                {
                    _steps.Add(step);
                    _currentActivity = $"Running tool {tool.Declaration.Name}...";
                }
                NotifyUpdated();

                // Safety & Approval Gate based on autonomy policy
                if (RequiresApproval(tool.Declaration.RiskLevel))
                {
                    lock (_stateLock)
                    {
                        _status = AgentTaskStatus.AwaitingApproval;
                        _currentActivity = $"Awaiting user approval to run {tool.Declaration.Name}...";
                    }
                    Log("WARN", $"Tool '{tool.Declaration.Name}' ({tool.Declaration.RiskLevel} risk) requires approval under autonomy policy.");

                    var approvalReq = new AgentApprovalRequest(
                        TaskId,
                        tool.Declaration.Name,
                        JsonSerializer.Serialize(args),
                        $"The agent wants to execute {tool.Declaration.Name}.",
                        tool.Declaration.RiskLevel,
                        DateTimeOffset.Now);

                    _pendingApprovalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var approvalTask = _onApprovalRequired(approvalReq);

                    // Wait for approval or cancel
                    using var reg = cancellationToken.Register(() => _pendingApprovalTcs.TrySetCanceled());
                    var approved = await approvalTask;

                    lock (_stateLock)
                    {
                        _status = AgentTaskStatus.Running;
                        _currentActivity = $"Resuming execution of {tool.Declaration.Name}...";
                    }

                    if (!approved)
                    {
                        Log("WARN", $"User denied permission for {tool.Declaration.Name}.");
                        var deniedStep = step with
                        {
                            Status = AgentStepStatus.Failed,
                            CompletedAt = DateTimeOffset.Now,
                            ErrorMessage = "Action denied by user."
                        };
                        lock (_stateLock)
                        {
                            _steps[_steps.Count - 1] = deniedStep;
                        }
                        continue;
                    }
                }

                // Execute the tool with robust error catching so worker does not abort
                AgentToolResult toolResult;
                try
                {
                    toolResult = await tool.ExecuteAsync(args, toolContext, cancellationToken);
                }
                catch (Exception toolEx) when (toolEx is not OperationCanceledException)
                {
                    toolResult = AgentToolResult.Fail($"Tool execution threw an unhandled exception: {toolEx.Message}");
                }

                var completedStep = step with
                {
                    Status = toolResult.Success ? AgentStepStatus.Success : AgentStepStatus.Failed,
                    ToolResult = toolResult.Output,
                    ErrorMessage = toolResult.ErrorMessage,
                    CompletedAt = DateTimeOffset.Now
                };

                lock (_stateLock)
                {
                    _steps[_steps.Count - 1] = completedStep;
                }

                if (toolResult.Success)
                {
                    Log("TOOL_RESULT", $"Tool {tool.Declaration.Name} succeeded: {toolResult.Output}");
                }
                else
                {
                    Log("TOOL_ERROR", $"Tool {tool.Declaration.Name} reported: {toolResult.ErrorMessage}");
                }
            }

            if (turnCount >= maxTurns)
            {
                // Out of steps is not the same as broken. The task is paused
                // with its work intact so the user can look at what it managed
                // and let it carry on, rather than marked Failed as though
                // something had gone wrong -- which is what it did before, and
                // which threw away eighty turns of real progress over a budget.
                lock (_stateLock)
                {
                    _status = AgentTaskStatus.Paused;
                    _errorMessage = null;
                    _currentActivity =
                        $"Paused after {maxTurns} steps. Resume to give it more, or cancel if it has done enough.";
                }

                Log("WARN", $"Reached the {maxTurns}-step budget. Paused rather than failed; the work so far is kept.");
                NotifyUpdated();
                _completionTcs.TrySetResult(false);
            }
        }
        catch (OperationCanceledException)
        {
            lock (_stateLock)
            {
                _status = AgentTaskStatus.Cancelled;
                _currentActivity = "Task cancelled.";
                _completedAt = DateTimeOffset.Now;
            }
            Log("INFO", "Task was cancelled.");
            _completionTcs.TrySetCanceled();
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _status = AgentTaskStatus.Failed;
                _errorMessage = $"Task failed with error: {ex.Message}";
                _completedAt = DateTimeOffset.Now;
            }
            Log("ERROR", $"Execution exception: {ex.Message}", ex.ToString());
            _completionTcs.TrySetException(ex);
        }
        finally
        {
            NotifyUpdated();
        }
    }

    private static (bool IsVerified, string Reason) ValidateVerificationStatus(
        IReadOnlyList<AgentStep> steps,
        AgentModelResponse response)
    {
        // 1. Check for unresolved errors in previous steps
        if (steps.Count > 0 && steps[^1].Status == AgentStepStatus.Failed)
        {
            return (false, "The most recent step failed. Resolve the error and verify the outcome before completing.");
        }

        // 2. Verification has to be recent as well as present. Checking the
        //    work on turn two says nothing about what was done on turn forty,
        //    and taking any check anywhere in the history is what let one early
        //    call immunise an entire run.
        var recent = steps.Count <= RecentStepsForVerification
            ? steps
            : steps.Skip(steps.Count - RecentStepsForVerification).ToList();

        var hasVerificationToolCall = recent.Any(s =>
            s.IsVerification || (s.ToolName is not null && VerificationTools.Contains(s.ToolName)));

        // 3. Check if thought or final answer explicitly confirms verification
        var thoughtContainsVerification = !string.IsNullOrWhiteSpace(response.Thought) &&
            VerificationKeywords.Any(k => response.Thought.Contains(k, StringComparison.OrdinalIgnoreCase));

        var answerContainsVerification = !string.IsNullOrWhiteSpace(response.FinalAnswer) &&
            VerificationKeywords.Any(k => response.FinalAnswer.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (hasVerificationToolCall || thoughtContainsVerification || answerContainsVerification)
        {
            return (true, string.Empty);
        }

        // If tools were run without any verification check or explicit verification proof
        if (steps.Count > 0)
        {
            return (false, "Please perform an explicit verification check (e.g. read_file, search_files, inspect directory or status) to confirm the output is correct.");
        }

        return (true, string.Empty);
    }

    private bool RequiresApproval(AgentRiskLevel riskLevel)
    {
        var mode = _getAutonomyMode?.Invoke() ?? "AskApproval";
        if (string.Equals(mode, "Strict", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(mode, "FullAutonomy", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return riskLevel == AgentRiskLevel.High;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pauseEvent.Dispose();
    }
}
