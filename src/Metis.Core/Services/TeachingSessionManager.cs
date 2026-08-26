using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Manages the state and step-by-step lifecycle of an active interactive tutoring/teaching session.
/// </summary>
public sealed class TeachingSessionManager
{
    private readonly object _lock = new();
    private LessonState? _currentLesson;

    public event EventHandler<LessonState>? LessonStarted;
    public event EventHandler<LessonState>? LessonStepChanged;
    public event EventHandler<LessonState>? LessonCompleted;
    public event EventHandler<LessonState>? LessonAbandoned;

    public LessonState? CurrentLesson
    {
        get
        {
            lock (_lock)
            {
                return _currentLesson;
            }
        }
    }

    public bool HasActiveLesson
    {
        get
        {
            lock (_lock)
            {
                return _currentLesson is not null && !_currentLesson.IsFinished;
            }
        }
    }

    /// <summary>
    /// Starts a new multi-step lesson.
    /// </summary>
    public LessonState StartLesson(string goal, IReadOnlyList<LessonStep> steps)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(steps);

        LessonState state;
        lock (_lock)
        {
            state = new LessonState(goal, steps, CurrentIndex: 0, Status: LessonStatus.Showing);
            _currentLesson = state;
        }

        LessonStarted?.Invoke(this, state);
        return state;
    }

    /// <summary>
    /// Advances the lesson to the next step.
    /// </summary>
    public LessonState? NextStep()
    {
        LessonState? updated = null;
        bool isComplete = false;

        lock (_lock)
        {
            if (_currentLesson is null || _currentLesson.IsFinished)
            {
                return _currentLesson;
            }

            _currentLesson = _currentLesson.Advance();
            updated = _currentLesson;
            isComplete = updated.IsFinished;
        }

        if (updated is not null)
        {
            if (isComplete)
            {
                LessonCompleted?.Invoke(this, updated);
            }
            else
            {
                LessonStepChanged?.Invoke(this, updated);
            }
        }

        return updated;
    }

    /// <summary>
    /// Sets the lesson status to waiting for user execution.
    /// </summary>
    public LessonState? WaitForUser()
    {
        lock (_lock)
        {
            if (_currentLesson is null || _currentLesson.IsFinished)
            {
                return _currentLesson;
            }

            _currentLesson = _currentLesson.Waiting();
            return _currentLesson;
        }
    }

    /// <summary>
    /// Increments retry attempts on the current step if the user got stuck.
    /// </summary>
    public LessonState? RetryCurrentStep()
    {
        LessonState? updated = null;
        lock (_lock)
        {
            if (_currentLesson is null || _currentLesson.IsFinished)
            {
                return _currentLesson;
            }

            _currentLesson = _currentLesson.Retry();
            updated = _currentLesson;
        }

        if (updated is not null)
        {
            LessonStepChanged?.Invoke(this, updated);
        }

        return updated;
    }

    /// <summary>
    /// Cancels or abandons the active lesson.
    /// </summary>
    public void AbandonLesson()
    {
        LessonState? abandoned = null;
        lock (_lock)
        {
            if (_currentLesson is not null && !_currentLesson.IsFinished)
            {
                _currentLesson = _currentLesson with { Status = LessonStatus.Abandoned };
                abandoned = _currentLesson;
            }
        }

        if (abandoned is not null)
        {
            LessonAbandoned?.Invoke(this, abandoned);
        }
    }
}
