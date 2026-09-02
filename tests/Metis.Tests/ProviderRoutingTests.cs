using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// Where a turn goes, and therefore who pays for it.
///
/// Bringing your own API key is now part of Pro, with no grandfathering. That
/// is a decision about pricing, but it lands here as a decision about routing,
/// and the routing is what these tests hold still.
///
/// Two things have to be true at once and they pull in opposite directions.
/// While billing is off, nothing about plans may reach anybody: a person who has
/// been running Metis on their own key since before any of this existed must
/// carry on exactly as before, or a Tuesday afternoon deploy quietly breaks
/// them. Once billing is on, a Free or Plus account must not be able to answer
/// on a key of its own however the settings file is edited. The gate that
/// separates those two worlds is one parameter, and these tests exercise both
/// sides of it.
/// </summary>
public sealed class ProviderRoutingTests
{
    /// <summary>
    /// Reads the way the caller does: allowed is what the entitlement says,
    /// which is true for everyone until billing goes live.
    /// </summary>
    private static ProviderRoute Route(
        string provider,
        bool hasKey,
        bool allowed = true,
        bool signedIn = true,
        bool gateway = true) =>
        ProviderRouting.Decide(provider, hasKey, allowed, signedIn, gateway);

    [Theory]
    [InlineData("Ollama")]
    [InlineData("OpenClaw")]
    public void A_local_model_never_needs_an_account_or_a_key(string provider) =>
        Assert.Equal(
            ProviderRoute.LocalOnly,
            Route(provider, hasKey: false, signedIn: false));

    /// <summary>
    /// A local model is outside the plan question entirely. Nothing about
    /// entitlements may reach it: it costs Metis nothing, needs no account, and
    /// never touches the network, so there is nothing there to sell.
    /// </summary>
    [Fact]
    public void A_local_model_is_untouched_by_the_Pro_gate() =>
        Assert.Equal(
            ProviderRoute.LocalOnly,
            Route("Ollama", hasKey: false, allowed: false, signedIn: false));

    // ===================== while billing is off =====================

    /// <summary>
    /// The promise that has to survive shipping this. Their key, their
    /// provider, their bill — signed in or not, whether or not there is a
    /// gateway sitting right there offering to do it instead.
    /// </summary>
    [Theory]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    [InlineData("OpenAI", false)]
    [InlineData("Claude", true)]
    [InlineData("OpenRouter", false)]
    public void Their_own_key_still_wins_while_billing_is_off(string provider, bool signedIn) =>
        Assert.Equal(
            ProviderRoute.DirectByok,
            Route(provider, hasKey: true, allowed: true, signedIn: signedIn));

    // ====================== once billing is on ======================

    /// <summary>
    /// The gate itself. A signed-in account without the entitlement does not go
    /// direct, however good the key in its settings file is — and lands on the
    /// gateway, where its plan's allowance applies, rather than being refused
    /// outright. Being moved onto the included AI is a much better morning than
    /// being told no.
    /// </summary>
    [Fact]
    public void A_key_without_the_entitlement_goes_through_the_gateway() =>
        Assert.Equal(
            ProviderRoute.MetisGateway,
            Route("Gemini", hasKey: true, allowed: false));

    /// <summary>
    /// The same account signed out has nothing left: no plan to draw on and no
    /// permission to use its own key. This is the case that must be a refusal
    /// rather than a silent direct call, because a silent direct call is the
    /// gate not existing.
    /// </summary>
    [Fact]
    public void A_key_without_the_entitlement_and_no_account_is_refused() =>
        Assert.Equal(
            ProviderRoute.RefuseNeedsKeyOrPlan,
            Route("Gemini", hasKey: true, allowed: false, signedIn: false));

    /// <summary>
    /// A Pro account keeps going direct even with a gateway available. It must
    /// never be quietly moved onto Metis's own AI: that would meter a request
    /// the user is already paying their provider for, and spend Metis's money
    /// to do it.
    /// </summary>
    [Fact]
    public void Pro_still_goes_direct_rather_than_through_the_gateway() =>
        Assert.Equal(
            ProviderRoute.DirectByok,
            Route("Gemini", hasKey: true, allowed: true));

    // ============================ the rest ============================

    [Fact]
    public void Signed_in_with_no_key_uses_the_gateway() =>
        Assert.Equal(ProviderRoute.MetisGateway, Route("Gemini", hasKey: false));

    [Fact]
    public void Signed_out_with_no_key_has_nothing_to_offer() =>
        Assert.Equal(
            ProviderRoute.RefuseNeedsKeyOrPlan,
            Route("Gemini", hasKey: false, signedIn: false));

    /// <summary>
    /// A build with the gateway blanked out is a real configuration — a fully
    /// self-hosted copy — rather than a broken one. It simply never offers the
    /// managed route.
    /// </summary>
    [Fact]
    public void A_build_with_no_gateway_still_serves_local_and_byok()
    {
        Assert.Equal(
            ProviderRoute.LocalOnly,
            Route("Ollama", hasKey: false, gateway: false));

        Assert.Equal(
            ProviderRoute.DirectByok,
            Route("Gemini", hasKey: true, gateway: false));

        Assert.Equal(
            ProviderRoute.RefuseNeedsKeyOrPlan,
            Route("Gemini", hasKey: false, gateway: false));
    }

    /// <summary>
    /// A self-hosted build whose user is not entitled to their own key has
    /// nowhere at all to send the turn, and has to say so rather than fail
    /// somewhere further down.
    /// </summary>
    [Fact]
    public void A_build_with_no_gateway_and_no_entitlement_is_refused() =>
        Assert.Equal(
            ProviderRoute.RefuseNeedsKeyOrPlan,
            Route("Gemini", hasKey: true, allowed: false, gateway: false));

    // ========================== the refusals ==========================

    /// <summary>
    /// A refusal has to name every way out, not just the one that suits Metis.
    /// Someone who already holds an OpenAI key should not be sold a
    /// subscription to solve a problem their key already solves.
    /// </summary>
    [Fact]
    public void A_refusal_offers_more_than_a_subscription()
    {
        var signedOut = ProviderRouting.ExplainRefusal(signedIn: false);

        Assert.Contains("API key", signedOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local model", signedOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign in", signedOut, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The exception, and the reason ExplainRefusal takes the flag at all.
    /// Telling somebody to add an API key when adding one is exactly what their
    /// plan will not let them do is the most frustrating thing an error message
    /// can do — and this user most likely has a key sitting in Setup already.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_refusal_does_not_offer_a_key_that_is_no_longer_allowed(bool signedIn)
    {
        var message = ProviderRouting.ExplainRefusal(signedIn, ownKeyIsAllowed: false);

        Assert.Contains("Pro", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Add your own API key", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local model", message, StringComparison.OrdinalIgnoreCase);
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
