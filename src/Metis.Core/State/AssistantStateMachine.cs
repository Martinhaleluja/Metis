using Metis.Core.Models;

namespace Metis.Core.State;

public sealed class AssistantStateMachine
{
    private readonly object _gate = new();
    private AssistantState _current = AssistantState.Idle;

    public AssistantState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<AssistantState>? Changed;

    public bool TryTransition(AssistantState next)
    {
        lock (_gate)
        {
            if (!IsAllowed(_current, next))
            {
                return false;
            }

            if (_current == next)
            {
                return true;
            }

            _current = next;
        }

        Changed?.Invoke(this, next);
        return true;
    }

    public void Force(AssistantState next)
    {
        lock (_gate)
        {
            _current = next;
        }

        Changed?.Invoke(this, next);
    }

    private static bool IsAllowed(AssistantState current, AssistantState next) =>
        IsError(next) ||
        next == AssistantState.Paused ||
        (current, next) switch
        {
            (AssistantState.Idle, AssistantState.Listening) => true,
            (AssistantState.Idle, AssistantState.Thinking) => true,
            (AssistantState.Listening, AssistantState.Thinking) => true,
            (AssistantState.Listening, AssistantState.Idle) => true,
            (AssistantState.Thinking, AssistantState.Speaking) => true,
            (AssistantState.Thinking, AssistantState.Success) => true,
            (AssistantState.Speaking, AssistantState.Success) => true,
            (AssistantState.Success, AssistantState.Idle) => true,
            (var error, AssistantState.Idle) when IsError(error) => true,
            (AssistantState.Paused, AssistantState.Idle) => true,
            _ => current == next
        };

    private static bool IsError(AssistantState state) => state is
        AssistantState.Error or
        AssistantState.NetworkError or
        AssistantState.AuthenticationError or
        AssistantState.QuotaError or
        AssistantState.AutomationError;
}
