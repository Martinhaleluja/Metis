using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class SafetyPolicyEngineTests
{
    private readonly SafetyPolicyEngine _engine = new();

    [Fact]
    public void A_click_labelled_as_a_purchase_is_high_risk()
    {
        var action = new DesktopAction(DesktopActionKind.LeftClick, 500, 500, Label: "Confirm purchase");

        Assert.Equal(RiskLevel.High, _engine.ClassifyRisk(action));
        Assert.True(_engine.RequiresUserConfirmation(action, OperatingMode.Autopilot));
    }

    [Fact]
    public void Autopilot_still_withholds_a_high_risk_click()
    {
        var action = new DesktopAction(DesktopActionKind.LeftClick, 500, 500, Label: "Delete account");

        Assert.False(_engine.IsPermitted(action, OperatingMode.Autopilot, userAskedForAction: true, out var reason));
        Assert.Contains("high-impact", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pointing_at_a_high_risk_control_stays_permitted()
    {
        var action = new DesktopAction(DesktopActionKind.MovePointer, 500, 500, Label: "Delete account");

        Assert.Equal(RiskLevel.Low, _engine.ClassifyRisk(action));
        Assert.True(_engine.IsPermitted(action, OperatingMode.Learn, userAskedForAction: false, out _));
    }

    [Fact]
    public void Learn_mode_refusal_explains_that_it_shows_rather_than_does()
    {
        var action = new DesktopAction(DesktopActionKind.LeftClick, 10, 10, Label: "Export");

        Assert.False(_engine.IsPermitted(action, OperatingMode.Learn, userAskedForAction: true, out var reason));
        Assert.Contains("shows you the step", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_ordinary_click_runs_in_assist_mode()
    {
        var action = new DesktopAction(DesktopActionKind.LeftClick, 10, 10, Label: "Export");

        Assert.True(_engine.IsPermitted(action, OperatingMode.Assist, userAskedForAction: false, out _));
    }
}
