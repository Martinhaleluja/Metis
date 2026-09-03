using System;
using System.Collections.Generic;
using System.Text;

namespace Metis.Core.Services;

/// <summary>
/// What Metis knows about itself, for the times a person needs to say
/// "it isn't working" to somebody who can help.
///
/// A support email that reads "it doesn't work" costs two round trips before
/// anyone can begin: which version, which Windows, could it reach the server.
/// This gathers those answers so they are already in the message.
///
/// The rule about what may go in is the same one <see cref="SecretRedaction"/>
/// enforces, applied earlier: nothing here reads a key, a conversation, a
/// screenshot, or the access token. The account id is included when somebody is
/// signed in because it is what lets support find their subscription, and it
/// grants nothing on its own. Every value is passed through redaction on the way
/// out regardless, because the cheapest place to catch a mistake is after it.
/// </summary>
public static class SupportDiagnostics
{
    /// <summary>Where support mail goes. Written down once.</summary>
    public const string SupportEmail = "support@metis.software";

    /// <summary>
    /// Everything worth telling support, as ordered lines.
    ///
    /// Taken as arguments rather than read from a runtime so this stays a pure
    /// function that can be tested without standing up an application.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        string appVersion,
        string windowsVersion,
        string gatewayStatus,
        bool signedIn,
        string? plan,
        string? accountId,
        string crashReporting)
    {
        var lines = new List<string>
        {
            $"Metis: {appVersion}",
            $"Windows: {windowsVersion}",
            $"Gateway: {gatewayStatus}",
            $"Signed in: {(signedIn ? "yes" : "no")}",
        };

        // Only when there is one. A line reading "Plan: null" looks like a
        // fault in the thing they are already writing to complain about.
        if (signedIn && !string.IsNullOrWhiteSpace(plan))
        {
            lines.Add($"Plan: {plan}");
        }

        if (signedIn && !string.IsNullOrWhiteSpace(accountId))
        {
            lines.Add($"Account: {accountId}");
        }

        lines.Add($"Crash reporting: {crashReporting}");

        for (var i = 0; i < lines.Count; i++)
        {
            lines[i] = SecretRedaction.Apply(lines[i]);
        }

        return lines;
    }

    /// <summary>The same thing as one block, for the clipboard.</summary>
    public static string Block(IReadOnlyList<string> lines) => string.Join(Environment.NewLine, lines);

    /// <summary>
    /// A <c>mailto:</c> with the diagnostics already written in.
    ///
    /// The block is fenced and announced rather than hidden at the bottom,
    /// because a person is about to send this to a stranger and is entitled to
    /// read what they are sending and delete it if they would rather.
    /// </summary>
    public static string Mailto(string subject, string intro, IReadOnlyList<string> lines)
    {
        var body = new StringBuilder()
            .AppendLine(intro)
            .AppendLine()
            .AppendLine()
            .AppendLine()
            .AppendLine("--- Please keep the lines below. They tell us where to look. ---")
            .AppendLine()
            .Append(Block(lines))
            .ToString();

        return $"mailto:{SupportEmail}?subject={Escape(subject)}&body={Escape(body)}";
    }

    /// <summary>
    /// Percent-encoding for a mailto. <see cref="Uri.EscapeDataString"/> encodes
    /// a space as <c>%20</c> rather than <c>+</c>, which is what mail clients
    /// expect here — a <c>+</c> would arrive literally in the subject line.
    /// </summary>
    private static string Escape(string value) => Uri.EscapeDataString(value);

    /// <summary>How the gateway looked, in words a person can repeat back.</summary>
    public static string DescribeGateway(bool reachable, bool waking) =>
        reachable ? "reachable"
        : waking ? "waking up (this is normal after a quiet period)"
        : "could not be reached";
}
