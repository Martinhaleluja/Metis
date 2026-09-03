using Metis.Core.Services;
using Xunit;

namespace Metis.Tests;

/// <summary>
/// A support email is a message a person sends to a stranger. These pin what
/// may be in one, and — more importantly — what may not.
/// </summary>
public sealed class SupportDiagnosticsTests
{
    private static System.Collections.Generic.IReadOnlyList<string> SignedIn(
        string? plan = "Max",
        string? accountId = "8c907ad8-77c4-41e2-a9b4-67b17ab0d5f4") =>
        SupportDiagnostics.Lines(
            appVersion: "3.15.0",
            windowsVersion: "Microsoft Windows NT 10.0.26200.0",
            gatewayStatus: "reachable",
            signedIn: true,
            plan: plan,
            accountId: accountId,
            crashReporting: "Off. Nothing is sent anywhere.");

    [Fact]
    public void A_signed_in_report_carries_what_support_needs()
    {
        var block = SupportDiagnostics.Block(SignedIn());

        Assert.Contains("Metis: 3.15.0", block);
        Assert.Contains("10.0.26200", block);
        Assert.Contains("Gateway: reachable", block);
        Assert.Contains("Plan: Max", block);
        Assert.Contains("Account: 8c907ad8", block);
    }

    [Fact]
    public void A_signed_out_report_omits_the_lines_rather_than_saying_null()
    {
        var block = SupportDiagnostics.Block(SupportDiagnostics.Lines(
            appVersion: "3.15.0",
            windowsVersion: "Microsoft Windows NT 10.0.26200.0",
            gatewayStatus: "could not be reached",
            signedIn: false,
            plan: null,
            accountId: null,
            crashReporting: "Off. Nothing is sent anywhere."));

        Assert.Contains("Signed in: no", block);
        Assert.DoesNotContain("Plan:", block);
        Assert.DoesNotContain("Account:", block);
        Assert.DoesNotContain("null", block);
    }

    [Fact]
    public void A_plan_is_not_reported_for_somebody_who_is_signed_out()
    {
        // Guards against a caller passing a stale plan alongside signedIn:false.
        var block = SupportDiagnostics.Block(SupportDiagnostics.Lines(
            "3.15.0", "Windows", "reachable",
            signedIn: false, plan: "Max", accountId: "abc",
            crashReporting: "Off."));

        Assert.DoesNotContain("Max", block);
        Assert.DoesNotContain("abc", block);
    }

    [Fact]
    public void A_secret_that_reaches_this_far_is_still_redacted()
    {
        // Nothing should ever pass a token in, but the diagnostics are the last
        // place to catch it before a person mails it to somebody.
        var block = SupportDiagnostics.Block(SupportDiagnostics.Lines(
            appVersion: "3.15.0",
            windowsVersion: "Windows",
            gatewayStatus: "refused: Bearer sk-proj-A1b2C3d4E5f6G7h8I9j0K1l2M3n4",
            signedIn: true,
            plan: "Max",
            accountId: "8c907ad8",
            crashReporting: "Off."));

        Assert.DoesNotContain("sk-proj-", block);
        Assert.Contains(SecretRedaction.Placeholder, block);
    }

    [Fact]
    public void The_mailto_is_addressed_and_encoded()
    {
        var url = SupportDiagnostics.Mailto("Metis bug report", "What happened:", SignedIn());

        Assert.StartsWith($"mailto:{SupportDiagnostics.SupportEmail}?", url);
        Assert.Contains("subject=Metis%20bug%20report", url);

        // A space must not become '+' here. Mail clients take mailto bodies
        // literally, so a '+' arrives as a '+' in the subject line.
        Assert.DoesNotContain("+", url);
    }

    [Fact]
    public void The_diagnostics_are_announced_rather_than_hidden()
    {
        // The person is about to send this to a stranger. They are entitled to
        // see the block and delete it if they would rather.
        var url = SupportDiagnostics.Mailto("Metis support request", "How can we help?", SignedIn());

        Assert.Contains(Uri.EscapeDataString("Please keep the lines below"), url);
    }

    [Theory]
    [InlineData(true, false, "reachable")]
    [InlineData(false, true, "waking up")]
    [InlineData(false, false, "could not be reached")]
    public void The_gateway_is_described_in_words_a_person_can_repeat(
        bool reachable, bool waking, string expected)
    {
        Assert.Contains(expected, SupportDiagnostics.DescribeGateway(reachable, waking));
    }
}
