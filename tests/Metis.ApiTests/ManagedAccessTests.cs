using Metis.Api;
using Metis.Core.Models;

namespace Metis.ApiTests;

/// <summary>
/// Whether a managed turn may run: the check that stands between Metis and an
/// unbounded AI bill.
/// </summary>
public sealed class ManagedAccessTests
{
    private static MetisAccount Account(PlanTier plan = PlanTier.Pro, UserRole role = UserRole.User) =>
        new("u_1", role, plan, MetisEnvironment.Production);

    private static PlanLimits Limits(
        decimal budget = 6m, int screenshot = 4_194_304, int agentSteps = 600, int turns = 0) =>
        new(budget, screenshot, 12, 20, agentSteps, 30, 500,
            ["gemini-2.5-flash-lite", "gemini-2.5-flash"], turns);

    private static GatewayRules Rules(
        CostProtection protection = CostProtection.Off,
        string? note = null,
        params string[] paused) =>
        new(BillingIsLive: true, protection, note, paused, new Dictionary<PlanTier, PlanLimits>());

    private static UsageSnapshot Spent(decimal usd, int agentSteps = 0) =>
        new(usd, 10, agentSteps, DateTimeOffset.UtcNow);

    [Fact]
    public void An_ordinary_turn_inside_the_budget_is_allowed() =>
        Assert.True(ManagedAccess.Decide(
            Account(), Limits(), Spent(1.20m), Rules(), false, false, 0).Allowed);

    [Fact]
    public void A_spent_allowance_is_402_and_names_what_still_works()
    {
        var decision = ManagedAccess.Decide(
            Account(), Limits(budget: 6m), Spent(6m), Rules(), false, false, 0);

        Assert.False(decision.Allowed);
        Assert.Equal(402, decision.StatusCode);
        Assert.Equal("quota", decision.Kind);
        Assert.Contains("own API key", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Free gets a smaller capture, not no capture. Denying screen vision
    /// outright made the free plan a screenshot of the product rather than a
    /// trial of it — somebody who cannot watch Metis do the one thing Metis is
    /// for has not tried Metis.
    /// </summary>
    [Fact]
    public void Free_can_send_a_small_screenshot()
    {
        var decision = ManagedAccess.Decide(
            Account(PlanTier.Free), Limits(screenshot: 1_048_576), Spent(0m), Rules(),
            requestHasScreenshot: true, isAgentStep: false, screenshotBytes: 500_000);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Free_cannot_send_a_large_one()
    {
        var decision = ManagedAccess.Decide(
            Account(PlanTier.Free), Limits(screenshot: 1_048_576), Spent(0m), Rules(),
            requestHasScreenshot: true, isAgentStep: false, screenshotBytes: 3_000_000);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal("plan", decision.Kind);
    }

    /// <summary>
    /// A plan with no capture allowance at all still refuses, and still says
    /// what does work. The configuration is no longer used, but the branch is.
    /// </summary>
    [Fact]
    public void A_plan_with_no_capture_allowance_names_what_still_works()
    {
        var decision = ManagedAccess.Decide(
            Account(PlanTier.Free), Limits(screenshot: 0), Spent(0m), Rules(),
            requestHasScreenshot: true, isAgentStep: false, screenshotBytes: 500_000);

        Assert.False(decision.Allowed);
        Assert.Contains("own API key", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------ Turn cap ------------------------------

    /// <summary>
    /// A count is the limit a person can picture. "A hundred and twenty
    /// questions a month" means something; "one dollar of inference" does not.
    /// </summary>
    [Fact]
    public void Free_runs_out_of_questions_before_it_runs_out_of_money()
    {
        var decision = ManagedAccess.Decide(
            Account(PlanTier.Free), Limits(budget: 1m, turns: 120),
            new UsageSnapshot(0.02m, 120, 0, DateTimeOffset.UtcNow),
            Rules(), false, false, 0);

        Assert.False(decision.Allowed);
        Assert.Equal(402, decision.StatusCode);
        Assert.Contains("120", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_with_no_turn_cap_is_bounded_only_by_money() =>
        Assert.True(ManagedAccess.Decide(
            Account(PlanTier.Pro), Limits(budget: 6m, turns: 0),
            new UsageSnapshot(1m, 9_000, 0, DateTimeOffset.UtcNow),
            Rules(), false, false, 0).Allowed);

    [Fact]
    public void An_oversized_screenshot_is_refused_but_differently()
    {
        var decision = ManagedAccess.Decide(
            Account(), Limits(screenshot: 1_000), Spent(0m), Rules(),
            requestHasScreenshot: true, isAgentStep: false, screenshotBytes: 2_000);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.DoesNotContain("part of Pro", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One stuck agent is thirty requests. A dollar budget alone would let it
    /// spend a month's allowance in ten minutes, which is why the step count is
    /// a separate limit rather than a derived one.
    /// </summary>
    [Fact]
    public void Agent_steps_run_out_before_the_dollars_do()
    {
        var decision = ManagedAccess.Decide(
            Account(), Limits(agentSteps: 600), Spent(0.10m, agentSteps: 600), Rules(),
            requestHasScreenshot: false, isAgentStep: true, screenshotBytes: 0);

        Assert.False(decision.Allowed);
        Assert.Equal(402, decision.StatusCode);
        Assert.Contains("agent messages", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_ordinary_turn_is_unaffected_by_a_spent_agent_allowance() =>
        Assert.True(ManagedAccess.Decide(
            Account(), Limits(agentSteps: 600), Spent(0.10m, agentSteps: 9_000), Rules(),
            requestHasScreenshot: false, isAgentStep: false, screenshotBytes: 0).Allowed);

    [Fact]
    public void Cost_protection_refuses_with_the_operator_s_own_words()
    {
        var decision = ManagedAccess.Decide(
            Account(), Limits(), Spent(0m),
            Rules(CostProtection.Refuse, "Back in an hour."),
            false, false, 0);

        Assert.False(decision.Allowed);
        Assert.Equal(503, decision.StatusCode);
        Assert.Equal("Back in an hour.", decision.Message);
    }

    /// <summary>
    /// Whoever pulled the emergency lever has to be able to see what is
    /// happening while it is pulled.
    /// </summary>
    [Fact]
    public void Staff_pass_through_cost_protection() =>
        Assert.True(ManagedAccess.Decide(
            Account(PlanTier.Free, UserRole.Founder), Limits(), Spent(0m),
            Rules(CostProtection.Refuse), false, false, 0).Allowed);

    // ---------------------------- Model choice ----------------------------

    [Fact]
    public void A_model_the_plan_allows_is_honoured() =>
        Assert.Equal(
            "gemini-2.5-flash",
            ManagedAccess.ChooseModel("gemini-2.5-flash", Limits(), Rules(), isStaff: false));

    /// <summary>
    /// Asking for something the plan does not include is answered with the
    /// plan's own first choice rather than an error. Being quietly moved to a
    /// cheaper model is a far better outcome for the user than a refusal, and
    /// the plan never promised the expensive one.
    /// </summary>
    [Fact]
    public void A_model_outside_the_plan_falls_to_the_cheapest_it_allows() =>
        Assert.Equal(
            "gemini-2.5-flash-lite",
            ManagedAccess.ChooseModel("gemini-2.5-pro", Limits(), Rules(), isStaff: false));

    [Fact]
    public void Degrade_forces_the_cheapest_model_whatever_was_asked_for() =>
        Assert.Equal(
            "gemini-2.5-flash-lite",
            ManagedAccess.ChooseModel("gemini-2.5-flash", Limits(), Rules(CostProtection.Degrade), isStaff: false));

    [Fact]
    public void A_paused_model_is_skipped() =>
        Assert.Equal(
            "gemini-2.5-flash",
            ManagedAccess.ChooseModel(
                "gemini-2.5-flash-lite", Limits(), Rules(paused: "gemini-2.5-flash-lite"), isStaff: false));

    /// <summary>
    /// With every model paused there is still something to answer with, rather
    /// than a null that would become the provider's own default and cost
    /// whatever that happens to be.
    /// </summary>
    [Fact]
    public void With_everything_paused_there_is_still_a_last_resort() =>
        Assert.False(string.IsNullOrWhiteSpace(ManagedAccess.ChooseModel(
            null, Limits(), Rules(paused: ["gemini-2.5-flash-lite", "gemini-2.5-flash"]), isStaff: false)));

    /// <summary>
    /// A gateway that has never read its own rules must not start spending
    /// against limits it is guessing at. Unknown means every allowance is zero.
    /// </summary>
    [Fact]
    public void CandidateModels_places_requested_model_first_followed_by_fallbacks()
    {
        var candidates = ManagedAccess.CandidateModels("gemini-2.5-flash", Limits(), Rules(), isStaff: false);

        Assert.Equal("gemini-2.5-flash", candidates[0]);
        Assert.Contains("gemini-2.5-flash-lite", candidates);
    }

    [Fact]
    public void CandidateModels_excludes_paused_models()
    {
        var candidates = ManagedAccess.CandidateModels(
            "gemini-2.5-flash", Limits(), Rules(paused: "gemini-2.5-flash-lite"), isStaff: false);

        Assert.Single(candidates);
        Assert.Equal("gemini-2.5-flash", candidates[0]);
    }

    [Fact]
    public void CandidateModels_falls_to_emergency_model_when_all_paused()
    {
        var candidates = ManagedAccess.CandidateModels(
            null, Limits(), Rules(paused: ["gemini-2.5-flash-lite", "gemini-2.5-flash"]), isStaff: false);

        Assert.NotEmpty(candidates);
        Assert.Equal("gemini-3.1-flash-lite", candidates[0]);
    }
}
