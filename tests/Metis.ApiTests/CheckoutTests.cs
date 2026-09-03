using System.Text.Json;
using Metis.Api;
using Metis.Api.Billing;
using Metis.Core.Models;

namespace Metis.ApiTests;

/// <summary>
/// Starting a checkout: the decisions made before any money moves.
///
/// Everything here is the part that runs without a network, which is the part
/// worth pinning down. A mistake in the outbound call is a checkout that fails
/// visibly; a mistake in these is a customer charged for the wrong plan, charged
/// twice for one they hold, or — worst — a subscription bound to somebody else's
/// account because an id came from the wrong place.
/// </summary>
public sealed class CheckoutTests
{
    private const string User = "55555555-5555-5555-5555-555555555555";

    private static GatewayConfig Config(
        string? token = "polar_oat_test_token",
        string? server = null,
        string? pro = "prod_pro_123",
        string? max = "prod_max_456",
        string? site = "https://metis.example") =>
        new(
            "https://project.supabase.co",
            "service-key",
            MetisEnvironment.Production,
            null, null, null, null, null, null,
            Array.Empty<string>(),
            null,
            null,
            PolarAccessToken: token,
            PolarServer: server,
            PolarProductPro: pro,
            PolarProductMax: max,
            SiteUrl: site);

    // ---------------------------- Which plan ----------------------------

    /// <summary>
    /// ParsePlan resolves anything it does not recognise to Free, so a
    /// misspelling, an empty body and a deliberate "free" all arrive as the same
    /// value — and all three mean there is nothing anyone could be charged for.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("free")]
    [InlineData("Free")]
    [InlineData("gold")]
    [InlineData("'; drop table subscriptions; --")]
    public void A_plan_nobody_can_buy_is_refused(string? requested)
    {
        var decision = Checkout.Decide(Config(), PlanTier.Free, requested);

        Assert.False(decision.Allowed);
        Assert.Equal(400, decision.StatusCode);
        Assert.Equal("request", decision.Kind);
        Assert.Null(decision.ProductId);
    }

    [Theory]
    [InlineData("pro", PlanTier.Pro, "prod_pro_123")]
    [InlineData("max", PlanTier.Max, "prod_max_456")]
    [InlineData("MAX", PlanTier.Max, "prod_max_456")]
    public void Each_plan_is_sold_as_its_own_product(string requested, PlanTier expected, string product)
    {
        var decision = Checkout.Decide(Config(), PlanTier.Free, requested);

        Assert.True(decision.Allowed);
        Assert.Equal(expected, decision.Plan);
        Assert.Equal(product, decision.ProductId);
    }

    /// <summary>
    /// "plus" is the old name for the middle plan and still resolves to Pro, so
    /// a stale button on a cached page sells the right thing rather than nothing.
    /// </summary>
    [Fact]
    public void The_retired_name_for_the_middle_plan_still_sells_it() =>
        Assert.Equal("prod_pro_123", Checkout.Decide(Config(), PlanTier.Free, "plus").ProductId);

    // -------------------------- Who is buying ---------------------------

    /// <summary>
    /// The processor would happily create a second subscription beside the
    /// first, and the customer would be paying twice for one plan with nothing
    /// in the product having visibly gone wrong.
    /// </summary>
    [Theory]
    [InlineData(PlanTier.Pro, "pro")]
    [InlineData(PlanTier.Max, "max")]
    [InlineData(PlanTier.Max, "pro")]
    public void A_plan_already_held_is_not_sold_again(PlanTier current, string requested)
    {
        var decision = Checkout.Decide(Config(), current, requested);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal("plan", decision.Kind);
    }

    [Theory]
    [InlineData(PlanTier.Free, "pro")]
    [InlineData(PlanTier.Free, "max")]
    [InlineData(PlanTier.Pro, "max")]
    public void Moving_up_the_ladder_is_allowed(PlanTier current, string requested) =>
        Assert.True(Checkout.Decide(Config(), current, requested).Allowed);

    // ------------------------- Whether it can sell ------------------------

    /// <summary>
    /// No token means no shop, and the answer says so without naming a
    /// processor. This is the webhook endpoint's reasoning applied to the other
    /// direction: any status or sentence that distinguishes "not configured"
    /// from "no such route" tells whoever is probing what this build knows.
    /// </summary>
    [Fact]
    public void Without_a_token_there_is_nothing_to_buy_here()
    {
        var decision = Checkout.Decide(Config(token: null), PlanTier.Free, "pro");

        Assert.False(decision.Allowed);
        Assert.Equal(404, decision.StatusCode);
        Assert.DoesNotContain("polar", decision.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A plan with no product id is refused the same way rather than falling
    /// back to the other plan's product, which would charge somebody twenty
    /// dollars for the fifty dollar plan or the reverse.
    /// </summary>
    [Fact]
    public void A_plan_with_no_product_id_is_refused_and_the_other_still_sells()
    {
        var config = Config(max: null);

        Assert.Equal(404, Checkout.Decide(config, PlanTier.Free, "max").StatusCode);
        Assert.True(Checkout.Decide(config, PlanTier.Free, "pro").Allowed);
    }

    /// <summary>
    /// Only the word "sandbox" reaches the test environment, and everything else
    /// — unset included — means the real one. The default has to fall this way
    /// round: a live customer sent to a sandbox is never charged and never
    /// generates a webhook, and nobody finds out until the money does not arrive.
    /// </summary>
    [Theory]
    [InlineData(null, "https://api.polar.sh")]
    [InlineData("", "https://api.polar.sh")]
    [InlineData("production", "https://api.polar.sh")]
    [InlineData("sandbox", "https://sandbox-api.polar.sh")]
    [InlineData("SANDBOX", "https://sandbox-api.polar.sh")]
    [InlineData("  sandbox  ", "https://sandbox-api.polar.sh")]
    public void POLAR_SERVER_chooses_which_polar_is_real(string? server, string expected) =>
        Assert.Equal(expected, Config(server: server).PolarApiBase);

    [Fact]
    public void Free_is_not_a_product() =>
        Assert.Null(Config().PolarProductFor(PlanTier.Free));

    // ------------------------ What is sent to Polar -----------------------

    /// <summary>
    /// A redirect the caller names is an open redirect: a link that genuinely
    /// begins at Metis's own gateway and ends wherever whoever sent it wanted.
    /// The only input here is configuration.
    /// </summary>
    [Fact]
    public void The_return_address_comes_from_configuration()
    {
        Assert.Equal(
            "https://metis.example/account?checkout=success",
            Checkout.SuccessUrl(Config()));

        // A value pasted out of a browser bar keeps its trailing slash. It must
        // not become a double slash in a customer's return link.
        Assert.Equal(
            "https://metis.example/account?checkout=success",
            Checkout.SuccessUrl(Config(site: "https://metis.example/")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void With_no_site_configured_there_is_no_return_address(string? site) =>
        Assert.Null(Checkout.SuccessUrl(Config(site: site)));

    /// <summary>
    /// The user id is written twice, and both copies come from the verified
    /// token. Metadata is what the specification asks for; the external customer
    /// id is the copy that survives if checkout metadata is not carried onto the
    /// subscription object later.
    /// </summary>
    [Fact]
    public void The_session_names_the_account_in_both_places()
    {
        var body = Parse(Checkout.BuildSessionRequest(
            "prod_pro_123", User, PlanTier.Pro, "someone@example.com", "https://metis.example/account?checkout=success"));

        Assert.Equal("prod_pro_123", body.GetProperty("products")[0].GetString());
        Assert.Equal(User, body.GetProperty("external_customer_id").GetString());
        Assert.Equal(User, body.GetProperty("metadata").GetProperty("metis_user_id").GetString());
        Assert.Equal("pro", body.GetProperty("metadata").GetProperty("plan").GetString());
        Assert.Equal("someone@example.com", body.GetProperty("customer_email").GetString());
        Assert.Equal(
            "https://metis.example/account?checkout=success",
            body.GetProperty("success_url").GetString());
    }

    /// <summary>
    /// The plan travels in the form the webhook's PlanFromMetadata reads back,
    /// which is the lower-case name and not the enum's own casing.
    /// </summary>
    [Fact]
    public void The_plan_travels_in_the_form_the_webhook_reads_back()
    {
        var body = Parse(Checkout.BuildSessionRequest("p", User, PlanTier.Max, null, null));

        Assert.Equal("max", body.GetProperty("metadata").GetProperty("plan").GetString());
    }

    /// <summary>
    /// Both optional fields are left out rather than sent empty. An address
    /// nobody could read is a prefill that does not happen, and no return
    /// address means Polar shows its own confirmation page — neither is a reason
    /// to fail a purchase, and neither should arrive as a blank value the
    /// processor has to interpret.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void What_is_not_known_is_left_out(string? email, string? successUrl)
    {
        var body = Parse(Checkout.BuildSessionRequest("p", User, PlanTier.Pro, email, successUrl));

        Assert.False(body.TryGetProperty("customer_email", out _));
        Assert.False(body.TryGetProperty("success_url", out _));

        // What is always known is still there: without these the payment lands
        // on no account at all.
        Assert.Equal(User, body.GetProperty("external_customer_id").GetString());
        Assert.Equal(User, body.GetProperty("metadata").GetProperty("metis_user_id").GetString());
    }

    // ------------------------ What comes back from it ----------------------

    [Fact]
    public void The_checkout_url_is_read_out_of_the_reply() =>
        Assert.Equal(
            "https://polar.sh/checkout/c_123",
            Checkout.ReadCheckoutUrl("""{"id":"c_123","url":"https://polar.sh/checkout/c_123"}"""));

    /// <summary>
    /// A 200 carrying no URL is not a success. There is nowhere to send the
    /// customer, and reporting it as one would leave somebody looking at a
    /// button that does nothing and no record of why.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"id":"c_123"}""")]
    [InlineData("""{"url":""}""")]
    [InlineData("""{"url":null}""")]
    [InlineData("""{"url":42}""")]
    public void A_reply_with_no_url_is_not_a_success(string? body) =>
        Assert.Null(Checkout.ReadCheckoutUrl(body));

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
