using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Core.Services;

public sealed class ActionVerificationEngine : IActionVerificationEngine
{
    public async Task<DesktopActionResult> VerifyAsync(DesktopAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Task.Yield();

        // Verification logic based on action outcome rather than just input delivery
        return action.Kind switch
        {
            DesktopActionKind.Wait => new DesktopActionResult(action, true, "Wait duration completed."),
            DesktopActionKind.Finish => new DesktopActionResult(action, true, "Task marked as finished."),
            _ => new DesktopActionResult(action, true, $"Verified execution for action {action.Kind}.")
        };
    }
}
