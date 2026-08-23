namespace Metis.Core.Services;

/// <summary>What startup should do about the account before showing anything.</summary>
public enum StartupAuthDecision
{
    /// <summary>Let the user through to Metis.</summary>
    Allow,

    /// <summary>Show the sign-in panel and nothing else.</summary>
    HoldForSignIn
}

/// <summary>
/// Whether a copy of Metis may open without signing in.
///
/// This is a pure rule rather than a chain of conditions inside startup because
/// it is the one decision in the application that can lock a user out of their
/// own machine, and a rule that can do that should be readable on its own and
/// testable without a window.
///
/// The case it exists to protect is the awkward one: a tester who signed in
/// last week, is on a train with no signal, and opens Metis. Nothing about that
/// person has changed, so holding them at a sign-in form they physically cannot
/// complete would be punishing them for the network. The backend being
/// unreachable is not evidence that anyone was signed out — and on the free
/// Supabase plan a project pauses by itself after a week of quiet, so an
/// unreachable backend is a thing that will actually happen.
///
/// A server that answers and refuses the token is different. That is a real
/// answer, and it means the session is genuinely over.
/// </summary>
public static class StartupAuthGate
{
    /// <summary>
    /// How long a saved session keeps working while the backend cannot be
    /// reached. Long enough that no honest user meets it — a fortnight offline
    /// is a holiday, not an anomaly — but not unbounded, so a machine that is
    /// abandoned eventually asks who is using it.
    /// </summary>
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(30);

    /// <param name="backendConfigured">
    /// Whether there is any backend to authenticate against. A build with none
    /// is never held: there is no sign-in that could succeed.
    /// </param>
    /// <param name="hasStoredSession">Whether a refresh token was found.</param>
    /// <param name="refreshSucceeded">Whether that token bought a live session.</param>
    /// <param name="backendReachable">
    /// Whether the server answered at all. A refusal and a timeout mean opposite
    /// things, and telling them apart is the whole point of this rule.
    /// </param>
    /// <param name="lastAuthenticatedUtc">When the session last actually worked.</param>
    public static StartupAuthDecision Decide(
        bool backendConfigured,
        bool hasStoredSession,
        bool refreshSucceeded,
        bool backendReachable,
        DateTimeOffset? lastAuthenticatedUtc,
        DateTimeOffset nowUtc,
        TimeSpan? grace = null)
    {
        // Nothing to sign in to. Holding here would be a locked door with no key
        // cut for it.
        if (!backendConfigured)
        {
            return StartupAuthDecision.Allow;
        }

        if (refreshSucceeded)
        {
            return StartupAuthDecision.Allow;
        }

        // Never signed in on this machine. This is the first run the gate is
        // actually for, and it is the only case where the user is asked to do
        // something they can definitely do.
        if (!hasStoredSession)
        {
            return StartupAuthDecision.HoldForSignIn;
        }

        // The server answered and would not renew the session. Signed out.
        if (backendReachable)
        {
            return StartupAuthDecision.HoldForSignIn;
        }

        // Offline with a saved session. Trust it for a bounded while.
        //
        // A missing timestamp is treated as still inside the window rather than
        // outside it: it means this copy signed in before Metis started
        // recording the date, so the only thing it proves is that the user did
        // once sign in — which is exactly what is being asked.
        if (lastAuthenticatedUtc is null)
        {
            return StartupAuthDecision.Allow;
        }

        var age = nowUtc - lastAuthenticatedUtc.Value;
        var window = grace ?? OfflineGrace;

        // A clock that has been set backwards leaves a negative age. That is not
        // an expired session, so it is not treated as one.
        return age <= window
            ? StartupAuthDecision.Allow
            : StartupAuthDecision.HoldForSignIn;
    }
}
