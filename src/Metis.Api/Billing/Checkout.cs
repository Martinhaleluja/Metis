using System.Text.Json;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Api.Billing;

/// <summary>
/// What a request to start a checkout came to: either a product to sell, or a
/// refusal with the status and the sentence that belong to it.
///
/// The shape mirrors <c>ManagedDecision</c> deliberately. Every refusal here has
/// a different status and a different thing to say — "that is not a plan", "you
/// already have this", "Metis cannot take money on this deployment" — and
/// collapsing them into a bool would lose the difference between a mistake the
/// caller can correct and a fact about the server they cannot.
/// </summary>
public sealed record CheckoutDecision(
    bool Allowed,
    int StatusCode,
    string? Kind,
    string? Message,
    PlanTier Plan,
    string? ProductId)
{
    public static CheckoutDecision Sell(PlanTier plan, string productId) =>
        new(true, 200, null, null, plan, productId);

    public static CheckoutDecision Refused(int status, string kind, string message) =>
        new(false, status, kind, message, PlanTier.Free, null);
}

/// <summary>
/// The parts of starting a checkout that can be decided without a network: which
/// plan was asked for, whether this account may buy it, which product that is,
/// and what the request to the processor should say.
///
/// It is a static class with no dependencies for the same reason
/// <c>ManagedAccess</c> is one: this is the code that decides where somebody's
/// money goes and whose plan changes as a result, and that is worth being able to
/// read — and test — without standing up a server or holding a real API token.
/// </summary>
public static class Checkout
{
    /// <summary>
    /// Whether this account may buy this plan, and what it is sold as.
    ///
    /// The order of the three refusals is chosen rather than incidental. The
    /// first two are facts about the caller's own request and their own account,
    /// which they already know; the third is the only one that says anything
    /// about how this deployment is configured, so it is reached last and by
    /// the fewest callers.
    /// </summary>
    public static CheckoutDecision Decide(GatewayConfig config, PlanTier currentPlan, string? requestedPlan)
    {
        ArgumentNullException.ThrowIfNull(config);

        var plan = Entitlements.ParsePlan(requestedPlan);

        // ParsePlan resolves anything it does not recognise to Free, so a
        // misspelling, a missing field and a deliberate "free" all arrive here as
        // the same value — and all three mean the same thing, which is that there
        // is nothing on this request anyone could be charged for.
        if (plan == PlanTier.Free)
        {
            return CheckoutDecision.Refused(400, "request", "Choose the Pro or the Max plan.");
        }

        // Already holds it, or holds something larger. Polar would happily create
        // a second subscription alongside the first, and the customer would be
        // paying twice for one plan without anything in the product having gone
        // visibly wrong.
        //
        // Compared ordinally, which is what MetisAccount.IsAtLeast does and is
        // safe for the reason the PlanTier declaration itself gives: the order of
        // that enum is load-bearing and a tier inserted out of it would make a
        // smaller plan test as larger.
        if (currentPlan >= plan)
        {
            return CheckoutDecision.Refused(403, "plan",
                currentPlan == plan
                    ? $"This account is already on Metis {plan}."
                    : $"This account is already on Metis {currentPlan}, which includes everything in {plan}.");
        }

        var product = config.PolarProductFor(plan);
        if (string.IsNullOrWhiteSpace(config.PolarAccessToken) || product is null)
        {
            // 404, and worded so it names no processor. This is the same
            // reasoning the webhook endpoint follows: a 501, or a message saying
            // which token is missing, tells whoever is probing exactly which
            // processors this build knows about and which of them are live.
            //
            // A missing product id lands here with a missing token because from
            // outside they are the same fact — this deployment cannot sell that
            // plan — and telling the two apart would only ever help someone
            // mapping the configuration.
            return CheckoutDecision.Refused(404, "unavailable", "Metis cannot take payments here yet.");
        }

        return CheckoutDecision.Sell(plan, product);
    }

    /// <summary>
    /// Where the customer is sent once they have paid, derived from the
    /// gateway's own configuration.
    ///
    /// Never from the request, and this is the more important half of the
    /// endpoint's security after the user id. A redirect the caller names is an
    /// open redirect: a link that genuinely begins at Metis's gateway and ends
    /// wherever whoever sent it wanted, carrying the trust of the first domain
    /// onto the second. That it happens to sit next to a payment form makes it
    /// worse rather than better.
    ///
    /// Null when METIS_SITE_URL is unset, in which case the field is left out of
    /// the request entirely and Polar shows its own confirmation page. The
    /// purchase still completes and the webhook still arrives; the customer is
    /// simply not walked back to their account afterwards.
    /// </summary>
    public static string? SuccessUrl(GatewayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var site = config.SiteUrl;
        return string.IsNullOrWhiteSpace(site)
            ? null
            : $"{site.TrimEnd('/')}/account?checkout=success";
    }

    /// <summary>
    /// The body of the request that creates the session.
    ///
    /// <paramref name="userId"/> is written into two places on purpose.
    /// <c>metadata</c> is what the specification asks for and what the webhook
    /// reads first; <c>external_customer_id</c> is stored by Polar on the
    /// customer record instead, which is the copy that survives if checkout
    /// metadata is not carried onto the subscription object later. Both are the
    /// Supabase user id from the verified token and neither is ever taken from
    /// the request body — an id the caller could name is a way to move somebody
    /// else's plan.
    /// </summary>
    public static string BuildSessionRequest(
        string productId,
        string userId,
        PlanTier plan,
        string? customerEmail,
        string? successUrl)
    {
        var body = new Dictionary<string, object>
        {
            ["products"] = new[] { productId },
            ["external_customer_id"] = userId,
            ["metadata"] = new Dictionary<string, string>
            {
                ["metis_user_id"] = userId,
                ["plan"] = plan.ToString().ToLowerInvariant()
            }
        };

        if (!string.IsNullOrWhiteSpace(successUrl))
        {
            body["success_url"] = successUrl;
        }

        // Prefill only. An address the customer changes on Polar's own form
        // changes nothing about which Metis account the subscription lands on,
        // which is exactly the property that makes it safe to send at all.
        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            body["customer_email"] = customerEmail;
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// The hosted checkout URL out of the processor's reply, or null when the
    /// reply did not carry one.
    ///
    /// A 200 with no URL is not a success. There would be nowhere to send the
    /// customer, and answering as though it had worked would leave somebody
    /// looking at a button that does nothing and no record of why.
    /// </summary>
    public static string? ReadCheckoutUrl(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var url = root.ValueKind == JsonValueKind.Object
                      && root.TryGetProperty("url", out var value)
                      && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
