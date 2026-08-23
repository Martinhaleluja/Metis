using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The gate decides whether someone can open Metis at all, so its edges are
/// worth pinning down. The ones that matter are the two ways a refresh can
/// fail: refused by a server that answered, which means signed out, and never
/// answered at all, which means nothing about the session.
/// </summary>
public sealed class StartupAuthGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static StartupAuthDecision Decide(
        bool backendConfigured = true,
        bool hasStoredSession = true,
        bool refreshSucceeded = false,
        bool backendReachable = true,
        DateTimeOffset? lastAuthenticatedUtc = null) =>
        StartupAuthGate.Decide(
            backendConfigured,
            hasStoredSession,
            refreshSucceeded,
            backendReachable,
            lastAuthenticatedUtc,
            Now);

    [Fact]
    public void A_renewed_session_opens_metis()
    {
        Assert.Equal(StartupAuthDecision.Allow, Decide(refreshSucceeded: true));
    }

    [Fact]
    public void A_first_run_with_no_saved_session_is_held()
    {
        Assert.Equal(
            StartupAuthDecision.HoldForSignIn,
            Decide(hasStoredSession: false));
    }

    [Fact]
    public void A_build_with_no_backend_is_never_held()
    {
        // There would be no sign-in that could succeed, so holding would be a
        // door with no key.
        Assert.Equal(
            StartupAuthDecision.Allow,
            Decide(backendConfigured: false, hasStoredSession: false));
    }

    [Fact]
    public void A_server_that_answers_and_refuses_the_token_signs_the_user_out()
    {
        Assert.Equal(
            StartupAuthDecision.HoldForSignIn,
            Decide(backendReachable: true, lastAuthenticatedUtc: Now.AddMinutes(-5)));
    }

    [Fact]
    public void An_unreachable_backend_does_not_sign_anyone_out()
    {
        // The train-tunnel case, and the paused-free-project case.
        Assert.Equal(
            StartupAuthDecision.Allow,
            Decide(backendReachable: false, lastAuthenticatedUtc: Now.AddDays(-3)));
    }

    [Fact]
    public void The_offline_grace_period_does_eventually_run_out()
    {
        Assert.Equal(
            StartupAuthDecision.HoldForSignIn,
            Decide(
                backendReachable: false,
                lastAuthenticatedUtc: Now - StartupAuthGate.OfflineGrace - TimeSpan.FromDays(1)));
    }

    [Fact]
    public void The_last_day_of_the_grace_period_still_counts_as_inside_it()
    {
        Assert.Equal(
            StartupAuthDecision.Allow,
            Decide(
                backendReachable: false,
                lastAuthenticatedUtc: Now - StartupAuthGate.OfflineGrace));
    }

    [Fact]
    public void Offline_with_no_recorded_date_is_let_through()
    {
        // Signed in before Metis recorded the date. All that proves is that the
        // user signed in once, which is what is being asked.
        Assert.Equal(
            StartupAuthDecision.Allow,
            Decide(backendReachable: false, lastAuthenticatedUtc: null));
    }

    [Fact]
    public void A_clock_set_backwards_does_not_expire_a_session()
    {
        Assert.Equal(
            StartupAuthDecision.Allow,
            Decide(backendReachable: false, lastAuthenticatedUtc: Now.AddDays(5)));
    }

    [Fact]
    public void Being_offline_never_rescues_someone_who_never_signed_in()
    {
        Assert.Equal(
            StartupAuthDecision.HoldForSignIn,
            Decide(hasStoredSession: false, backendReachable: false));
    }
}
