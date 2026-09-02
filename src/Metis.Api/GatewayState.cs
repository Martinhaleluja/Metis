using Metis.Core.Models;

namespace Metis.Api;

/// <summary>
/// The operating rules and the price list, kept current in the background.
///
/// The point of this class is that changing what Metis charges, allows, or
/// spends is a row update rather than a deployment. That only works if
/// something re-reads the rows; putting the read in front of every request
/// would work too, and would add two Supabase round trips to every turn to
/// notice a change that happens a few times a year.
///
/// Sixty seconds is the compromise, and it is chosen for the worst case rather
/// than the common one: the cost-protection switch is an emergency brake, and
/// an emergency brake that takes a quarter of an hour to engage is not one.
/// </summary>
public sealed class GatewayState(SupabaseGateway supabase, ILogger<GatewayState> log) : IHostedService, IDisposable
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(60);

    private Timer? _timer;
    private volatile GatewayRules _rules = GatewayRules.Unknown;
    private volatile ModelPriceBook _prices = ModelPriceBook.Empty;

    /// <summary>
    /// True once the rules have been read at least once.
    ///
    /// Until then the gateway is running on <see cref="GatewayRules.Unknown"/>,
    /// which refuses to spend anything. Managed requests are therefore held off
    /// rather than served against guessed limits: a gateway that cannot read its
    /// own budget must not start drawing on it.
    /// </summary>
    public bool Ready { get; private set; }

    public GatewayRules Rules => _rules;

    public ModelPriceBook Prices => _prices;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        _timer = new Timer(
            async _ => await RefreshAsync(CancellationToken.None),
            state: null,
            RefreshEvery,
            RefreshEvery);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rules = await supabase.LoadRulesAsync(cancellationToken);
            if (rules is not null)
            {
                var wasProtecting = _rules.Protection;
                _rules = rules;
                Ready = true;

                if (wasProtecting != rules.Protection)
                {
                    log.LogWarning(
                        "Cost protection changed to {Mode}. Managed AI is affected; bring-your-own-key is not.",
                        rules.Protection);
                }
            }

            var prices = await supabase.LoadModelPricesAsync(cancellationToken);
            if (prices.Count > 0)
            {
                _prices = new ModelPriceBook(prices);
            }
        }
        catch (Exception exception)
        {
            // A failed refresh keeps the last known good rules rather than
            // reverting to Unknown. Supabase being briefly unreachable is not a
            // reason to stop serving people whose plans have not changed.
            log.LogError(exception, "Could not refresh gateway rules. Keeping the previous ones.");
        }
    }
}

/// <summary>
/// Whether a managed request may proceed, and why not when it may not.
///
/// A record rather than a bool because every refusal here has a different HTTP
/// status and a different sentence for the user, and collapsing them loses the
/// distinction between "your plan is too small", "you have used this month's
/// allowance" and "Metis has paused its own AI" — three things that feel the
/// same to a program and completely different to a person.
/// </summary>
public sealed record ManagedDecision(bool Allowed, int StatusCode, string? Kind, string? Message)
{
    public static ManagedDecision Ok { get; } = new(true, 200, null, null);

    public static ManagedDecision PlanLimited(string message) =>
        new(false, StatusCodes.Status403Forbidden, "plan", message);

    public static ManagedDecision OutOfAllowance(string message) =>
        new(false, StatusCodes.Status402PaymentRequired, "quota", message);

    public static ManagedDecision Paused(string message) =>
        new(false, StatusCodes.Status503ServiceUnavailable, "degraded", message);
}

/// <summary>
/// Decides whether a managed turn may run, given the plan, the allowance, and
/// the state of the cost-protection switch.
///
/// It is a separate static class with no dependencies so it can be tested
/// without a server, on the same principle as StartupAuthGate and
/// ProviderRouting: the decisions that cost money or lock people out are the
/// ones worth being able to read on their own.
/// </summary>
public static class ManagedAccess
{
    public static ManagedDecision Decide(
        MetisAccount account,
        PlanLimits limits,
        UsageSnapshot usage,
        GatewayRules rules,
        bool requestHasScreenshot,
        bool isAgentStep,
        int screenshotBytes,
        bool isDictation = false)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(rules);

        // Staff pass through cost protection, because whoever pulled the lever
        // needs to be able to see what is happening while it is pulled.
        if (!account.IsStaff && rules.Protection == CostProtection.Refuse)
        {
            return ManagedDecision.Paused(
                string.IsNullOrWhiteSpace(rules.ProtectionNote)
                    ? "Metis's included AI is paused right now. Your own API key still works."
                    : rules.ProtectionNote);
        }

        if (requestHasScreenshot && screenshotBytes > limits.MaxScreenshotBytes)
        {
            return limits.MaxScreenshotBytes == 0
                ? ManagedDecision.PlanLimited(
                    "Metis reading your screen on its own AI is part of Pro. Your own API key still can.")
                : ManagedDecision.PlanLimited(
                    "That screen capture is larger than this plan allows. Metis will send a smaller one next time.");
        }

        if (isAgentStep && usage.AgentSteps >= limits.MaxAgentStepsPerMonth)
        {
            return ManagedDecision.OutOfAllowance(
                $"You have used this month's {limits.MaxAgentStepsPerMonth} agent messages. "
                + "They reset on the 1st.");
        }

        // Dictation has its own allowance and is checked before the turn cap,
        // because speaking a note is not a talk message and must not be refused
        // by one running out. The two are separate lines on the pricing page
        // and are separate counters underneath.
        if (isDictation
            && limits.MaxDictationMinutesPerMonth > 0
            && usage.DictationMinutes >= limits.MaxDictationMinutesPerMonth)
        {
            return ManagedDecision.OutOfAllowance(
                $"You have used this month's {limits.MaxDictationMinutesPerMonth} minutes of "
                + "dictation. They reset on the 1st.");
        }

        // The talk cap, where a plan has one. Free is bounded by a count as well
        // as by money because a count is the thing a person can picture: "fifty
        // talk messages a month" means something, and "one dollar of inference"
        // does not.
        //
        // Deliberately not applied to dictation or to agent steps: request_count
        // in the database excludes both, so a person who has been dictating or
        // running agents has not quietly spent their answers as well.
        if (!isAgentStep && !isDictation
            && limits.MaxTurnsPerMonth > 0
            && usage.RequestCount >= limits.MaxTurnsPerMonth)
        {
            return ManagedDecision.OutOfAllowance(
                $"You have used this month's {limits.MaxTurnsPerMonth} talk messages. "
                + "They reset on the 1st.");
        }

        if (usage.SpendUsd >= limits.MonthlyBudgetUsd)
        {
            return ManagedDecision.OutOfAllowance(
                "You have used this month's included AI. It resets on the 1st — or add your own API key in Setup to keep going now.");
        }

        return ManagedDecision.Ok;
    }

    /// <summary>
    /// Which model a managed turn should actually use.
    ///
    /// The plan's list is the allow-list, not a suggestion: a client asking for
    /// a model outside it gets the plan's first choice rather than an error,
    /// because being quietly moved to a cheaper model is a much better outcome
    /// for the user than a refusal, and the plan never promised the expensive
    /// one.
    ///
    /// plan_limits.managed_models is authored cheapest-first, which is why
    /// taking the first entry is the same thing as taking the cheapest. That
    /// ordering is a convention rather than something the column enforces, and
    /// it is worth keeping when the lists are edited.
    /// </summary>
    public static string ChooseModel(string? requested, PlanLimits limits, GatewayRules rules, bool isStaff)
    {
        var allowed = limits.ManagedModels
            .Where(model => isStaff || !rules.PausedModels.Contains(model, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (allowed.Length == 0)
        {
            // Every model this plan may use is paused. The last resort is the
            // cheapest thing Metis serves at all, named here rather than left
            // to a null that would become the provider's own default.
            return "gemini-2.5-flash-lite";
        }

        if (!isStaff && rules.Protection == CostProtection.Degrade)
        {
            return allowed[0];
        }

        return !string.IsNullOrWhiteSpace(requested)
               && allowed.Contains(requested, StringComparer.OrdinalIgnoreCase)
            ? requested
            : allowed[0];
    }
}
