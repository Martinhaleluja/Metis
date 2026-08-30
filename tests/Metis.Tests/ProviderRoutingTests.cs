using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Where a turn goes, and therefore who pays for it.
///
/// The case that matters most is the one that is easiest to break by accident:
/// a person who has been running Metis on their own API key, with no account,
/// since before any of this existed. Nothing about plans may reach them. If a
/// change to the rules makes any of the "own key" cases below start routing
/// through the gateway, that person's requests silently begin being metered
/// against a plan they never bought, and stop working entirely the day billing
/// is switched on.
/// </summary>
public sealed class ProviderRoutingTests
{
    [Theory]
    [InlineData("Ollama")]
    [InlineData("OpenClaw")]
    public void A_local_model_never_needs_an_account_or_a_key(string provider)
    {
        var route = ProviderRouting.Decide(
            provider,
            hasOwnKeyForConfiguredProvider: false,
            signedIn: false,
            gatewayConfigured: true);

        Assert.Equal(ProviderRoute.LocalOnly, route);
    }

    /// <summary>
    /// The rule the whole file exists for. Their key, their provider, their
    /// bill — signed in or not, on any plan, whether or not there is a gateway
    /// sitting right there offering to do it instead.
    /// </summary>
    [Theory]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    [InlineData("OpenAI", false)]
    [InlineData("Claude", true)]
    [InlineData("OpenRouter", false)]
    public void Their_own_key_always_wins(string provider, bool signedIn)
    {
        var route = ProviderRouting.Decide(
            provider,
            hasOwnKeyForConfiguredProvider: true,
            signedIn,
            gatewayConfigured: true);

        Assert.Equal(ProviderRoute.DirectByok, route);
    }

    [Fact]
    public void Signed_in_with_no_key_uses_the_gateway()
    {
        var route = ProviderRouting.Decide(
            "Gemini",
            hasOwnKeyForConfiguredProvider: false,
            signedIn: true,
            gatewayConfigured: true);

        Assert.Equal(ProviderRoute.MetisGateway, route);
    }

    [Fact]
    public void Signed_out_with_no_key_has_nothing_to_offer()
    {
        var route = ProviderRouting.Decide(
            "Gemini",
            hasOwnKeyForConfiguredProvider: false,
            signedIn: false,
            gatewayConfigured: true);

        Assert.Equal(ProviderRoute.RefuseNeedsKeyOrPlan, route);
    }

    /// <summary>
    /// A build with the gateway blanked out is a real configuration — a fully
    /// self-hosted copy — rather than a broken one. It simply never offers the
    /// managed route, and everyone runs on their own key exactly as before.
    /// </summary>
    [Fact]
    public void A_build_with_no_gateway_still_serves_local_and_byok()
    {
        Assert.Equal(
            ProviderRoute.LocalOnly,
            ProviderRouting.Decide("Ollama", false, signedIn: true, gatewayConfigured: false));

        Assert.Equal(
            ProviderRoute.DirectByok,
            ProviderRouting.Decide("Gemini", true, signedIn: true, gatewayConfigured: false));

        Assert.Equal(
            ProviderRoute.RefuseNeedsKeyOrPlan,
            ProviderRouting.Decide("Gemini", false, signedIn: true, gatewayConfigured: false));
    }

    /// <summary>
    /// A refusal has to name every way out, not just the one that suits Metis.
    /// Someone who already holds an OpenAI key should not be sold a subscription
    /// to solve a problem their key already solves.
    /// </summary>
    [Fact]
    public void A_refusal_offers_more_than_a_subscription()
    {
        var signedOut = ProviderRouting.ExplainRefusal(signedIn: false);

        Assert.Contains("API key", signedOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local model", signedOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign in", signedOut, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ollama", true)]
    [InlineData("OpenClaw", true)]
    [InlineData("Gemini", false)]
    [InlineData("Metis", false)]
    [InlineData(null, false)]
    public void Local_providers_are_recognised_whatever_the_casing(string? provider, bool expected) =>
        Assert.Equal(expected, ProviderRouting.IsLocalProvider(provider));
}
