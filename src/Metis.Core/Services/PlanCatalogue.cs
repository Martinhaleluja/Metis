using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// One plan, as a customer is told about it.
/// </summary>
/// <param name="Tier">The tier this describes.</param>
/// <param name="Name">What it is called. Shown everywhere; never abbreviated.</param>
/// <param name="PriceUsd">Whole dollars a month. Formatted by <see cref="PriceLabel"/>.</param>
/// <param name="Summary">
/// What you get, in one sentence, in the units the plan is metered in. This is
/// the sentence the pricing page, the app's plan switcher and the upgrade
/// prompts all use, so it has to be true rather than flattering.
/// </param>
public sealed record PlanOffer(
    PlanTier Tier,
    string Name,
    int PriceUsd,
    string Summary,
    PlanLimits Limits)
{
    public string PriceLabel => PriceUsd == 0 ? "Free" : $"${PriceUsd}";

    public string Cadence => PriceUsd == 0 ? "forever" : "/month";
}

/// <summary>
/// The plans, written down once on the client side.
///
/// The server is still the authority: <c>plan_limits</c> in Postgres is what
/// the gateway actually enforces, and an entitlement snapshot from
/// <c>/v1/me</c> always wins over anything here. This exists for the two
/// moments where that snapshot is not available and the interface still has to
/// say something — before the first successful call, and while offline — and so
/// that the numbers a person is shown come from one place rather than being
/// retyped into every panel that mentions them.
///
/// Keep these in step with the <c>plan_limits</c> rows and with
/// <c>website/src/lib/plans.ts</c>. A price that lives in three files is a price
/// that will one day disagree with itself, and the copy a customer sees is
/// whichever one you forgot.
///
/// On the naming: these were Free, Plus and Pro. The middle plan is now called
/// Pro and the top one Max, so the word "pro" means different things either
/// side of that change. Nothing here parses a stored value —
/// <see cref="Entitlements.ParsePlan"/> is the only thing allowed to do that.
/// </summary>
public static class PlanCatalogue
{
    /// <summary>
    /// Free's allowances.
    ///
    /// Fifty answers is deliberately a real trial rather than a demonstration:
    /// enough to find out over a couple of weeks whether Metis is worth paying
    /// for. Dictation is generous because transcription is cheap and because
    /// speaking to Metis is the thing that makes it feel different — metering
    /// it tightly would hide the product behind its own paywall. Ten agent
    /// messages is enough to watch an agent do one job start to finish, and not
    /// enough to fund an unattended overnight run on Metis's money.
    /// </summary>
    public static PlanOffer Free { get; } = new(
        PlanTier.Free,
        "Free",
        0,
        "50 talk messages a month, plenty of dictation, and 10 agent messages.",
        new PlanLimits(
            MonthlyBudgetUsd: 1.00m,
            MaxScreenshotBytes: 1_048_576,
            RequestsPerMinute: 3,
            BurstRequests: 6,
            MaxAgentStepsPerMonth: 10,
            MaxAgentStepsPerTask: 20,
            MemoryEntriesMax: 50,
            ManagedModels: ["gemini-2.5-flash-lite"],
            MaxTurnsPerMonth: 50,
            MaxDictationMinutesPerMonth: 300));

    /// <summary>
    /// Pro. No count on talking or dictating; the ceiling is money instead.
    ///
    /// Agents keep a count on every plan, including this one, because they are
    /// the one place where a single action turns into dozens of paid calls
    /// without another. A budget alone notices a runaway agent only after the
    /// month is gone.
    /// </summary>
    public static PlanOffer Pro { get; } = new(
        PlanTier.Pro,
        "Pro",
        20,
        "Talk and dictate as much as you like, and 400 agent messages a month.",
        new PlanLimits(
            MonthlyBudgetUsd: 9.00m,
            MaxScreenshotBytes: 8_388_608,
            RequestsPerMinute: 20,
            BurstRequests: 40,
            MaxAgentStepsPerMonth: 400,
            MaxAgentStepsPerTask: 60,
            MemoryEntriesMax: 2_000,
            ManagedModels: ["gemini-2.5-flash-lite", "gemini-2.5-flash"],
            MaxTurnsPerMonth: 0,
            MaxDictationMinutesPerMonth: 0));

    /// <summary>
    /// Max. Everything in Pro, five times the agent allowance, and the only
    /// plan that may answer on a provider key of your own.
    ///
    /// Worth being straight about what the price buys, because "pay us $50 to
    /// use your own key" is a fair thing to be suspicious of: Max is Pro's
    /// included AI plus the option to bypass it entirely for the work where you
    /// would rather choose the model and pay your provider directly.
    /// </summary>
    public static PlanOffer Max { get; } = new(
        PlanTier.Max,
        "Max",
        50,
        "Everything in Pro, 2,000 agent messages, and your own AI account.",
        new PlanLimits(
            MonthlyBudgetUsd: 22.00m,
            MaxScreenshotBytes: 8_388_608,
            RequestsPerMinute: 30,
            BurstRequests: 60,
            MaxAgentStepsPerMonth: 2_000,
            MaxAgentStepsPerTask: 120,
            MemoryEntriesMax: 10_000,
            ManagedModels: ["gemini-2.5-flash-lite", "gemini-2.5-flash", "gemini-2.5-pro"],
            MaxTurnsPerMonth: 0,
            MaxDictationMinutesPerMonth: 0));

    /// <summary>Cheapest first, which is the order every plan grid draws them in.</summary>
    public static IReadOnlyList<PlanOffer> All { get; } = [Free, Pro, Max];

    public static PlanOffer For(PlanTier tier) => tier switch
    {
        PlanTier.Max => Max,
        PlanTier.Pro => Pro,
        _ => Free
    };

    /// <summary>
    /// The allowances to assume for a tier when the server has not been
    /// reached. Never used in place of a snapshot that did arrive.
    /// </summary>
    public static PlanLimits LimitsFor(PlanTier tier) => For(tier).Limits;

    /// <summary>
    /// The next plan up, or null at the top. What an upgrade prompt should
    /// offer, so no panel has to hard-code "upgrade to Pro" and then be wrong
    /// for the people already on it.
    /// </summary>
    public static PlanOffer? NextAfter(PlanTier tier) => tier switch
    {
        PlanTier.Free => Pro,
        PlanTier.Pro => Max,
        _ => null
    };
}
