namespace Metis.Core.Models;

public enum AssistantState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
    Success,
    Error,
    NetworkError,
    AuthenticationError,
    QuotaError,
    AutomationError,
    Paused
}
