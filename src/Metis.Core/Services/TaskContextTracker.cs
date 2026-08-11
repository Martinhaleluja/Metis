using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Keeps one goal alive across steps and across applications. Switching from a
/// browser to an editor is a step inside the same task, not a new task, so the
/// active window alone never resets the goal.
/// </summary>
public sealed class TaskContextTracker
{
    private readonly object _gate = new();
    private readonly TimeSpan _idleTimeout;
    private AgentTaskState? _current;
    private DateTimeOffset _lastTouched = DateTimeOffset.MinValue;

    public TaskContextTracker(TimeSpan? idleTimeout = null)
    {
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(20);
    }

    public AgentTaskState? Current
    {
        get
        {
            lock (_gate)
            {
                return IsStale() ? null : _current;
            }
        }
    }

    /// <summary>
    /// Starts a task for a new goal, or continues the existing one when the
    /// request reads as a continuation ("keep going", "and now…") of a goal
    /// that is still fresh.
    /// </summary>
    public AgentTaskState BeginTurn(string request, string application, OperatingMode mode)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var continues = _current is not null && !IsStale() && IsContinuation(request);
            _current = continues
                ? _current! with
                {
                    CurrentObjective = request.Trim(),
                    CurrentStep = _current.CurrentStep + 1,
                    CurrentApplication = application,
                    CurrentMode = mode
                }
                : AgentTaskState.Start(request.Trim(), application, mode);
            _lastTouched = DateTimeOffset.Now;
            return _current;
        }
    }

    /// <summary>
    /// Appends what actually happened. Observations are recorded separately from
    /// actions so a later prompt can distinguish what Metis did from what it saw.
    /// </summary>
    public void RecordProgress(string? action, string? observation)
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return;
            }

            var actions = _current.PreviousActions;
            if (!string.IsNullOrWhiteSpace(action))
            {
                actions = [.. actions.TakeLast(9), action.Trim()];
            }

            var observations = _current.Observations;
            if (!string.IsNullOrWhiteSpace(observation))
            {
                observations = [.. observations.TakeLast(4), observation.Trim()];
            }

            _current = _current with { PreviousActions = actions, Observations = observations };
            _lastTouched = DateTimeOffset.Now;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _current = null;
            _lastTouched = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// A short block describing the ongoing goal for the next request. Returns
    /// null when there is no live task, so a first request stays clean.
    /// </summary>
    public string? Describe()
    {
        var state = Current;
        if (state is null || state.CurrentStep == 0 && state.PreviousActions.Count == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"goal: {state.OriginalUserGoal}",
            $"step: {state.CurrentStep}",
            $"application: {state.CurrentApplication}"
        };

        if (state.PreviousActions.Count > 0)
        {
            lines.Add($"already done: {string.Join(" | ", state.PreviousActions)}");
        }

        if (state.Observations.Count > 0)
        {
            lines.Add($"observed: {string.Join(" | ", state.Observations)}");
        }

        return string.Join("\n", lines);
    }

    private bool IsStale() =>
        _current is null || DateTimeOffset.Now - _lastTouched > _idleTimeout;

    private static bool IsContinuation(string request)
    {
        string[] continuationTerms =
        [
            "continue", "keep going", "carry on", "next step", "next", "and then", "after that",
            "now ", "then ", "finish it", "same thing", "again", "go on", "what now", "what next"
        ];
        var trimmed = request.Trim();
        return continuationTerms.Any(term =>
            trimmed.StartsWith(term, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains($" {term}", StringComparison.OrdinalIgnoreCase));
    }
}
