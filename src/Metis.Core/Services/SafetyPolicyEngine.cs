using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Permission checks kept deliberately outside model reasoning. The engine sees
/// only the action, the mode, and whether the user asked for a computer action
/// in this turn; nothing a provider returns can widen what it permits.
/// </summary>
public sealed class SafetyPolicyEngine : ISafetyPolicyEngine
{
    private static readonly HashSet<DesktopActionKind> LowRiskActions =
    [
        DesktopActionKind.MovePointer,
        DesktopActionKind.Observe,
        DesktopActionKind.Verify,
        DesktopActionKind.Wait,
        DesktopActionKind.WaitForWindow,
        DesktopActionKind.WaitForElement,
        DesktopActionKind.WaitForText,
        DesktopActionKind.Finish
    ];

    private static readonly string[] SensitiveKeywords =
    [
        "delete", "remove", "uninstall", "buy", "purchase", "pay", "checkout", "order",
        "format", "sudo", "admin", "administrator", "password", "passcode", "credential",
        "secret", "token", "permission", "privacy", "security", "firewall", "antivirus",
        "submit", "send", "publish", "transfer", "bank", "sign in", "log in", "two-factor"
    ];

    public RiskLevel ClassifyRisk(DesktopAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // A command is high risk whatever it says. There is no wording that
        // makes a shell safe, and sorting commands into tiers would teach the
        // user that some of them do not need reading.
        if (action.Kind == DesktopActionKind.RunCommand)
        {
            return RiskLevel.High;
        }

        if (LowRiskActions.Contains(action.Kind))
        {
            return RiskLevel.Low;
        }

        var payload = $"{action.Text} {action.Label} {action.Key} {action.Id}";
        if (SensitiveKeywords.Any(keyword => payload.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return RiskLevel.High;
        }

        return RiskLevel.Medium;
    }

    public bool IsPermitted(DesktopAction action, OperatingMode mode, bool userAskedForAction, out string reason)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!ModePolicy.Allows(mode, action.Kind, userAskedForAction))
        {
            var capabilities = ModePolicy.For(mode);
            reason = capabilities.MayActWhenAsked
                ? $"{capabilities.DisplayName} mode performs computer actions only when you ask for them directly."
                : $"{capabilities.DisplayName} mode shows you the step instead of performing it.";
            return false;
        }

        if (ClassifyRisk(action) == RiskLevel.High)
        {
            reason = "This looks high-impact, so Metis points at the control and leaves the final action to you.";
            return false;
        }

        reason = "Permitted.";
        return true;
    }

    public bool RequiresUserConfirmation(DesktopAction action, OperatingMode mode) =>
        ClassifyRisk(action) == RiskLevel.High;
}
