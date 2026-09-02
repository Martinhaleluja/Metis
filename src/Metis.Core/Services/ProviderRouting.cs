using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>Where a turn's reasoning request should actually go.</summary>
public enum ProviderRoute
{
    /// <summary>
    /// A model running on this machine. Never touches the network, never needs
    /// an account, and is never metered.
    /// </summary>
    LocalOnly,

    /// <summary>
    /// Straight to the provider, on the user's own API key. Metis pays nothing,
    /// so nothing about a plan applies.
    /// </summary>
    DirectByok,

    /// <summary>
    /// Through the Metis gateway, on Metis's key. This is the only route where
    /// plans, allowances and cost protection mean anything.
    /// </summary>
    MetisGateway,

    /// <summary>
    /// There is no way to answer: no local model, no key of their own, and no
    /// account to draw an allowance from.
    /// </summary>
    RefuseNeedsKeyOrPlan
}

/// <summary>
/// Decides which of those four a turn is, and nothing else.
///
/// It is a pure function with no dependencies for the same reason
/// <see cref="StartupAuthGate"/> is: this is a decision that can spend someone's
/// money or lock them out of the product, and a decision like that should be
/// readable on its own and testable without a window.
///
/// The rule that matters most is the second one, and it has changed. Bringing
/// your own API key is now part of the top plan — Max, at the time of writing,
/// though nothing here names it: <c>Entitlements</c> decides which plan grants
/// <c>CustomAiProvider</c> and <c>PlanCatalogue</c> is asked what that plan is
/// called. That is a deliberate decision, taken with its cost understood: Metis
/// has always worked on a personal key with no account at all, a great many
/// people use it that way, and none of them are being grandfathered.
///
/// What has not changed is the shape of the guard. The gate is on
/// <paramref name="ownKeyIsAllowed"/>, which the caller derives from the
/// entitlement — and the entitlement is false for nobody until billing is
/// switched on. So the day this shipped, every existing user carried on exactly
/// as before; the day billing goes live is the day the plans start to mean
/// something, which is the only day on which taking a paid feature away from
/// somebody is defensible. Wiring the gate to a deploy instead would have
/// broken every one of those users on a Tuesday afternoon for no reason at all.
/// </summary>
public static class ProviderRouting
{
    /// <summary>
    /// The providers that run on the user's own machine. They need no key and no
    /// account, and they are the reason Metis can be used with nothing leaving
    /// the computer at all.
    /// </summary>
    public static bool IsLocalProvider(string? provider) =>
        provider?.Trim().ToLowerInvariant() is "ollama" or "openclaw";

    /// <summary>The provider id the gateway answers as.</summary>
    public const string GatewayProviderId = "Metis";

    /// <param name="ownKeyIsAllowed">
    /// Whether this account may use a key of its own — in practice
    /// <c>Entitlements.Has(account, MetisFeature.CustomAiProvider, billingIsLive)</c>,
    /// which is true for everybody while billing is off and limited to the plan
    /// that includes it once billing is on. Passed in rather than looked up so
    /// this stays a pure function, and
    /// deliberately not defaulted: a caller that forgets it should not silently
    /// get the permissive answer.
    /// </param>
    public static ProviderRoute Decide(
        string configuredProvider,
        bool hasOwnKeyForConfiguredProvider,
        bool ownKeyIsAllowed,
        bool signedIn,
        bool gatewayConfigured)
    {
        // 1. A local model. No key, no account, no network. Whatever else is
        //    true of the person running it, this answer never changes.
        if (IsLocalProvider(configuredProvider))
        {
            return ProviderRoute.LocalOnly;
        }

        // 2. Their own key, if their plan includes using one. Metis is not
        //    paying for this request, so it is never metered and never counted
        //    against an allowance — the question is only whether this account is
        //    allowed to make it at all.
        //
        //    Still above every other account check, so a Pro user with a key is
        //    never quietly routed through Metis's own AI and billed for
        //    inference they were paying their provider for directly.
        if (hasOwnKeyForConfiguredProvider && ownKeyIsAllowed)
        {
            return ProviderRoute.DirectByok;
        }

        // 3. No usable key and no account. There is nothing to draw on.
        if (!signedIn)
        {
            return ProviderRoute.RefuseNeedsKeyOrPlan;
        }

        // 4. Signed in, no key, and there is a gateway to ask.
        if (gatewayConfigured)
        {
            return ProviderRoute.MetisGateway;
        }

        // 5. Signed in, but this build has no gateway to talk to — a local or
        //    self-hosted build. Nothing to fall back on.
        return ProviderRoute.RefuseNeedsKeyOrPlan;
    }

    /// <summary>
    /// What to tell someone whose turn cannot be answered at all.
    ///
    /// It names all three ways forward rather than the one that happens to suit
    /// Metis, because a person who already has an OpenAI key should not be sold
    /// a subscription to solve a problem their key already solves.
    /// </summary>
    public static string ExplainRefusal(bool signedIn, bool ownKeyIsAllowed = true)
    {
        // Somebody whose key has stopped being usable is not told to add one.
        // Offering the fix that is no longer available is the most frustrating
        // thing an error message can do, and this user very likely has a key
        // sitting in Setup already.
        if (!ownKeyIsAllowed)
        {
            // Asked rather than written down. These two sentences said Pro for
            // months after bringing your own key moved to Max, which sent people
            // to buy the wrong plan — the one thing a refusal message must never
            // do. PlanCatalogue answers from the same table Entitlements
            // enforces, so it cannot fall out of step with it again.
            var plan = PlanCatalogue.NameOfPlanWith(MetisFeature.CustomAiProvider);

            return signedIn
                ? $"Using your own API key is part of Metis {plan}. Upgrade to keep using it, or switch to a local model."
                : $"Sign in to use Metis's own AI, or use a local model. Your own API key is part of Metis {plan}.";
        }

        return signedIn
            ? "Metis has no way to answer right now. Add your own API key in Setup, or use a local model."
            : "Add your own API key in Setup, use a local model, or sign in to use Metis's own AI.";
    }
}
