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
/// The rule that matters most is the second one. Metis has always worked on the
/// user's own API key, with no account, and a great many people are using it
/// that way right now. If having a key of your own stopped being enough, every
/// one of those people would silently start being metered against a plan they
/// never bought — and would stop working entirely on the day billing is
/// switched on. That is the failure this file exists to make impossible, and it
/// is why the check for a personal key comes before every question about
/// accounts, plans and billing rather than after them.
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

    public static ProviderRoute Decide(
        string configuredProvider,
        bool hasOwnKeyForConfiguredProvider,
        bool signedIn,
        bool gatewayConfigured)
    {
        // 1. A local model. No key, no account, no network. Whatever else is
        //    true of the person running it, this answer never changes.
        if (IsLocalProvider(configuredProvider))
        {
            return ProviderRoute.LocalOnly;
        }

        // 2. Their own key wins, unconditionally — signed in or not, on any
        //    plan, whether or not billing is live. Metis is not paying for this
        //    request, so Metis has no business metering it or refusing it.
        //
        //    Deliberately above every account check. See the class comment.
        if (hasOwnKeyForConfiguredProvider)
        {
            return ProviderRoute.DirectByok;
        }

        // 3. No key of their own and no account. There is nothing to draw on.
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
    public static string ExplainRefusal(bool signedIn) => signedIn
        ? "Metis has no way to answer right now. Add your own API key in Setup, or use a local model."
        : "Add your own API key in Setup, use a local model, or sign in to use Metis's own AI.";
}
