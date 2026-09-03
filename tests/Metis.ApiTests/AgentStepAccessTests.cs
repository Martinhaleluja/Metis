using Metis.Api;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.ApiTests;

/// <summary>
/// What an agent step is allowed to cost, and who is allowed to run one.
///
/// Agents are the one thing in Metis where a single user action turns into
/// dozens of paid AI calls without another action. A thirty-step task is thirty
/// requests, and an agent that misreads its own progress will happily go round
/// its loop until something stops it — so a dollar budget alone is the wrong
/// instrument: by the time spend notices, the month is gone. The step count is
/// the instrument that notices in time.
///
/// The two refusals are deliberately different codes, because they mean
/// genuinely different things to the person reading them. Running out of steps
/// is 402 and resolves itself on the 1st. Not having agents at all is 403 and
/// never resolves itself, and telling that user to come back next month would
/// be a lie.
/// </summary>
public sealed class AgentStepAccessTests
{
    private static MetisAccount Account(PlanTier plan = PlanTier.Pro, UserRole role = UserRole.User) =>
        new("u_1", role, plan, MetisEnvironment.Production);

    private static PlanLimits Limits(
        decimal budget = 6m, int screenshot = 4_194_304, int agentSteps = 600, int turns = 0) =>
        new(budget, screenshot, 12, 20, agentSteps, 30, 500,
            ["gemini-2.5-flash-lite", "gemini-2.5-flash"], turns);

    private static GatewayRules Rules(bool billingIsLive = true) =>
        new(billingIsLive, CostProtection.Off, null, [], new Dictionary<PlanTier, PlanLimits>());

    private static GatewayRules Paused() =>
        new(BillingIsLive: true, CostProtection.Refuse, null, [],
            new Dictionary<PlanTier, PlanLimits>());

    private static UsageSnapshot Used(int agentSteps, decimal usd = 0m, int requests = 0) =>
        new(usd, requests, agentSteps, DateTimeOffset.UtcNow);

    private static ManagedDecision Step(
        MetisAccount account, PlanLimits limits, UsageSnapshot usage, GatewayRules? rules = null) =>
        ManagedAccess.Decide(
            account, limits, usage, rules ?? Rules(),
            requestHasScreenshot: false, isAgentStep: true, screenshotBytes: 0);

    [Fact]
    public void A_step_inside_the_allowance_runs() =>
        Assert.True(Step(Account(), Limits(agentSteps: 600), Used(599)).Allowed);

    /// <summary>
    /// The boundary is the interesting case, and it is exclusive: the six
    /// hundredth step is the last one that runs, so at a count of 600 the
    /// allowance is spent. An off-by-one here is a plan that silently sells one
    /// step more or fewer than the pricing page says.
    /// </summary>
    [Fact]
    public void The_allowance_runs_out_at_the_number_on_the_pricing_page()
    {
        var decision = Step(Account(), Limits(agentSteps: 600), Used(600));

        Assert.False(decision.Allowed);
        Assert.Equal(402, decision.StatusCode);
        Assert.Equal("quota", decision.Kind);
        Assert.Contains("agent messages", decision.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reset", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Free's agent allowance is zero, so its very first step is refused. This
    /// is the case that used to be unreachable: the desktop client never called
    /// the gateway for agent work at all, so the column was decoration.
    /// </summary>
    [Fact]
    public void A_plan_with_no_agent_allowance_refuses_the_first_step()
    {
        var decision = Step(Account(PlanTier.Free), Limits(budget: 1m, agentSteps: 0), Used(0));

        Assert.False(decision.Allowed);
        Assert.Equal(402, decision.StatusCode);
    }

    /// <summary>
    /// The step allowance is checked before the money, so a user who has both
    /// run out of steps and run out of budget is told about the steps. It is
    /// the more specific and more actionable of the two.
    /// </summary>
    [Fact]
    public void Running_out_of_steps_is_reported_ahead_of_running_out_of_money()
    {
        var decision = Step(Account(), Limits(budget: 6m, agentSteps: 600), Used(600, usd: 6m));

        Assert.Contains("agent messages", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An ordinary conversation turn is never charged against the agent
    /// allowance, however many steps have been used. This is what the
    /// isAgentStep flag is for, and getting it backwards would lock a user out
    /// of talking to Metis because an agent had been busy.
    /// </summary>
    [Fact]
    public void A_spent_agent_allowance_does_not_stop_ordinary_questions() =>
        Assert.True(ManagedAccess.Decide(
            Account(), Limits(agentSteps: 600), Used(9_000), Rules(),
            requestHasScreenshot: false, isAgentStep: false, screenshotBytes: 0).Allowed);

    // ===================== the plan gate, which is a 403 =====================

    /// <summary>
    /// Free may run agents; it just has ten messages of them a month.
    ///
    /// The capability and the amount are different questions, and this used to
    /// answer them the same way. Free is now sold with an agent allowance, so
    /// refusing the capability outright would mean the pricing page advertised
    /// something the entitlement check forbids — and the person who bought
    /// nothing would find out by watching an agent refuse to start rather than
    /// by running out.
    /// </summary>
    [Fact]
    public void Free_may_run_agents_and_is_bounded_by_the_count_instead()
    {
        Assert.True(Entitlements.Has(
            Account(PlanTier.Free), MetisFeature.AutonomousAgents, billingIsLive: true));

        // ...and the eleventh step of the month is where it stops.
        var decision = Step(Account(PlanTier.Free), Limits(agentSteps: 10), Used(10));

        Assert.False(decision.Allowed);
        Assert.Equal(402, decision.StatusCode);
    }

    [Theory]
    [InlineData(PlanTier.Free)]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Max)]
    public void Every_plan_is_entitled_to_agents(PlanTier plan) =>
        Assert.True(Entitlements.Has(
            Account(plan), MetisFeature.AutonomousAgents, billingIsLive: true));

    /// <summary>
    /// Agents that hand work to each other are Max's, and Pro must not quietly
    /// get them: it is the difference between the two paid plans on the agents
    /// line of the pricing page.
    /// </summary>
    [Fact]
    public void Advanced_agents_are_Max_only()
    {
        Assert.True(Entitlements.Has(
            Account(PlanTier.Max), MetisFeature.AdvancedAgents, billingIsLive: true));
        Assert.False(Entitlements.Has(
            Account(PlanTier.Pro), MetisFeature.AdvancedAgents, billingIsLive: true));
    }

    /// <summary>
    /// Nobody is refused agents for being on the wrong plan, on any plan, and
    /// least of all before billing is live. What separates the plans is the
    /// number of agent messages a month, which ManagedAccess enforces per step.
    /// </summary>
    [Theory]
    [InlineData(PlanTier.Free)]
    [InlineData(PlanTier.Pro)]
    [InlineData(PlanTier.Max)]
    public void Agents_are_never_refused_for_the_plan_alone(PlanTier plan)
    {
        Assert.True(Entitlements.Has(
            Account(plan), MetisFeature.AutonomousAgents, billingIsLive: false));
        Assert.True(Entitlements.Has(
            Account(plan), MetisFeature.AutonomousAgents, billingIsLive: true));
    }

    /// <summary>
    /// Cost protection outranks everything. When the lever is pulled, agents —
    /// the most expensive thing on the service — stop first, and the message
    /// points at the way out that does not cost Metis anything.
    /// </summary>
    [Fact]
    public void A_paused_service_stops_agent_steps_too()
    {
        var decision = Step(Account(PlanTier.Pro), Limits(agentSteps: 2_000), Used(0), Paused());

        Assert.False(decision.Allowed);
        Assert.Equal(503, decision.StatusCode);
    }

    /// <summary>
    /// Staff keep running while the lever is pulled, because the person
    /// diagnosing an incident needs the thing that is on fire to still work.
    /// </summary>
    [Fact]
    public void Staff_keep_running_while_the_service_is_paused() =>
        Assert.True(Step(
            Account(PlanTier.Pro, UserRole.Founder), Limits(agentSteps: 2_000), Used(0),
            Paused()).Allowed);
}
