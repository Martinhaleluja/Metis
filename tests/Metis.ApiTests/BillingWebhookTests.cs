using System.Security.Cryptography;
using System.Text;
using Metis.Api.Billing;
using Metis.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Metis.ApiTests;

/// <summary>
/// Webhook verification, for both processors, before either has been chosen.
///
/// The point of testing an inert subsystem is that the day it stops being inert
/// is the day money starts moving through it, and that is a poor moment to
/// discover the signature check was wrong. Both implementations are exercised
/// here with real HMACs so that switching one on is a matter of setting a
/// secret.
/// </summary>
public sealed class BillingWebhookTests
{
    private const string StripeSecret = "whsec_test_secret_value";
    private static readonly byte[] PolarKey = Encoding.UTF8.GetBytes("polar-test-signing-key-0123456789");

    private static string PolarSecret => "whsec_" + Convert.ToBase64String(PolarKey);

    // ------------------------------- Stripe -------------------------------

    [Fact]
    public void A_genuine_Stripe_signature_verifies()
    {
        var body = Encoding.UTF8.GetBytes("""{"id":"evt_1","type":"customer.subscription.updated"}""");
        var headers = StripeHeaders(body, DateTimeOffset.UtcNow);

        Assert.True(new StripeWebhookVerifier(StripeSecret)
            .TryVerify(body, headers, DateTimeOffset.UtcNow, out _));
    }

    /// <summary>
    /// One flipped byte. This is what a body rewritten by a JSON round trip
    /// looks like to the verifier, which is why the endpoint reads raw bytes
    /// before anything is deserialised.
    /// </summary>
    [Fact]
    public void A_Stripe_body_changed_by_one_byte_fails()
    {
        var body = Encoding.UTF8.GetBytes("""{"id":"evt_1","type":"customer.subscription.updated"}""");
        var headers = StripeHeaders(body, DateTimeOffset.UtcNow);
        body[^2] = (byte)'X';

        Assert.False(new StripeWebhookVerifier(StripeSecret)
            .TryVerify(body, headers, DateTimeOffset.UtcNow, out var reason));
        Assert.Contains("signature", reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Without a timestamp check, a captured request stays valid forever, so
    /// anyone who once saw one could replay it — including an "active" event for
    /// a subscription that has since been cancelled.
    /// </summary>
    [Fact]
    public void A_replayed_Stripe_webhook_is_out_of_time()
    {
        var body = Encoding.UTF8.GetBytes("""{"id":"evt_1","type":"customer.subscription.updated"}""");
        var old = DateTimeOffset.UtcNow - TimeSpan.FromHours(2);
        var headers = StripeHeaders(body, old);

        Assert.False(new StripeWebhookVerifier(StripeSecret)
            .TryVerify(body, headers, DateTimeOffset.UtcNow, out var reason));
        Assert.Contains("window", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stripe_subscription_events_are_normalised_to_Metis_s_own_names()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"id":"evt_9","type":"customer.subscription.updated","data":{"object":{
              "id":"sub_123","customer":"cus_9","status":"active","cancel_at_period_end":false,
              "current_period_end":1800000000,
              "metadata":{"metis_user_id":"11111111-1111-1111-1111-111111111111","plan":"pro"}}}}
            """);

        var parsed = new StripeWebhookVerifier(StripeSecret).Parse(body)!;

        Assert.Equal("subscription.updated", parsed.EventType);
        Assert.Equal("sub_123", parsed.ExternalSubscriptionId);
        Assert.Equal("cus_9", parsed.ExternalCustomerId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", parsed.MetisUserId);
        Assert.Equal(PlanTier.Pro, parsed.Plan);
        Assert.Equal("active", parsed.Status);
        Assert.True(parsed.ChangesEntitlement);
    }

    // -------------------------------- Polar -------------------------------

    [Fact]
    public void A_genuine_Polar_signature_verifies()
    {
        var body = Encoding.UTF8.GetBytes("""{"type":"subscription.active","data":{"id":"s_1"}}""");
        var headers = PolarHeaders(body, "msg_1", DateTimeOffset.UtcNow);

        Assert.True(new PolarWebhookVerifier(PolarSecret)
            .TryVerify(body, headers, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void A_Polar_body_changed_by_one_byte_fails()
    {
        var body = Encoding.UTF8.GetBytes("""{"type":"subscription.active","data":{"id":"s_1"}}""");
        var headers = PolarHeaders(body, "msg_1", DateTimeOffset.UtcNow);
        body[^3] = (byte)'X';

        Assert.False(new PolarWebhookVerifier(PolarSecret)
            .TryVerify(body, headers, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void Polar_reads_the_plan_from_product_metadata()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.active","data":{
              "id":"s_1","status":"active","cancel_at_period_end":true,
              "customer":{"id":"c_1"},
              "current_period_end":"2027-01-01T00:00:00Z",
              "metadata":{"metis_user_id":"22222222-2222-2222-2222-222222222222","plan_id":"metis_plus"}}}
            """);

        var parsed = new PolarWebhookVerifier(PolarSecret).Parse(body)!;

        Assert.Equal(PlanTier.Pro, parsed.Plan);
        Assert.Equal("22222222-2222-2222-2222-222222222222", parsed.MetisUserId);
        Assert.True(parsed.CancelAtPeriodEnd);
        Assert.True(parsed.ChangesEntitlement);
    }

    /// <summary>
    /// The quietest failure in the whole system, and the fallback that prevents
    /// it.
    ///
    /// Metis sets <c>metis_user_id</c> on the <em>checkout</em>. Whether Polar
    /// copies checkout metadata onto the subscription object it emits afterwards
    /// is Polar's behaviour rather than ours. If it does not, an event with no id
    /// changes no entitlement, is stored, and is answered 200 — the processor is
    /// satisfied and never retries, and a customer who has paid and holds a
    /// receipt has been given nothing.
    ///
    /// The external customer id sent at the same checkout is stored on the
    /// customer record, which arrives on the subscription, so it survives where
    /// the metadata did not. It is still a value Metis wrote and never anything
    /// the buyer typed.
    /// </summary>
    [Fact]
    public void Polar_falls_back_to_the_customer_record_when_metadata_is_not_carried_over()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.active","data":{
              "id":"s_3","status":"active",
              "customer":{"id":"c_3","external_id":"66666666-6666-6666-6666-666666666666"},
              "metadata":{}}}
            """);

        var parsed = new PolarWebhookVerifier(PolarSecret).Parse(body)!;

        Assert.Equal("66666666-6666-6666-6666-666666666666", parsed.MetisUserId);
        Assert.True(parsed.ChangesEntitlement);
    }

    /// <summary>The same value flattened, which some payloads carry instead.</summary>
    [Fact]
    public void Polar_reads_a_flattened_external_customer_id_too()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.updated","data":{
              "id":"s_4","status":"active","customer_id":"c_4",
              "customer_external_id":"77777777-7777-7777-7777-777777777777"}}
            """);

        var parsed = new PolarWebhookVerifier(PolarSecret).Parse(body)!;

        Assert.Equal("77777777-7777-7777-7777-777777777777", parsed.MetisUserId);
        Assert.True(parsed.ChangesEntitlement);
    }

    /// <summary>
    /// Metadata still wins where it is present, so an event carrying both is
    /// read the way the specification describes rather than the way the fallback
    /// happens to work.
    /// </summary>
    [Fact]
    public void Metadata_is_still_preferred_over_the_customer_record()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.active","data":{
              "id":"s_5","status":"active",
              "customer":{"id":"c_5","external_id":"88888888-8888-8888-8888-888888888888"},
              "metadata":{"metis_user_id":"99999999-9999-9999-9999-999999999999"}}}
            """);

        Assert.Equal(
            "99999999-9999-9999-9999-999999999999",
            new PolarWebhookVerifier(PolarSecret).Parse(body)!.MetisUserId);
    }

    /// <summary>
    /// A field that is present and blank is not an answer. Stopping the chain on
    /// one would carry an empty string into apply_subscription as the account to
    /// change, which is worse than the failure the chain exists to prevent.
    /// </summary>
    [Fact]
    public void A_blank_id_is_treated_as_absent_and_the_chain_continues()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.active","data":{
              "id":"s_6","status":"active",
              "customer":{"id":"c_6","external_id":"  "},
              "customer_external_id":"12121212-1212-1212-1212-121212121212",
              "metadata":{"metis_user_id":""}}}
            """);

        Assert.Equal(
            "12121212-1212-1212-1212-121212121212",
            new PolarWebhookVerifier(PolarSecret).Parse(body)!.MetisUserId);
    }

    /// <summary>
    /// And with none of the three, nothing happens to anyone's plan. The
    /// fallback widens where the id may be found; it does not weaken the rule
    /// that an event without one changes nothing.
    /// </summary>
    [Fact]
    public void A_Polar_event_naming_no_account_anywhere_changes_nothing()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.active","data":{
              "id":"s_7","status":"active","customer":{"id":"c_7"},
              "metadata":{"email":"someone@example.com","plan":"max"}}}
            """);

        var parsed = new PolarWebhookVerifier(PolarSecret).Parse(body)!;

        Assert.Null(parsed.MetisUserId);
        Assert.False(parsed.ChangesEntitlement);
    }

    // ------------------------------- Shared -------------------------------

    /// <summary>
    /// An unconfigured verifier reports itself unconfigured rather than half
    /// working. The endpoint answers 404 for one, which is deliberate: any other
    /// status tells a prober which processors this deployment knows about.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unconfigured_verifier_says_so(string? secret)
    {
        Assert.False(new PolarWebhookVerifier(secret).IsConfigured);
        Assert.False(new StripeWebhookVerifier(secret).IsConfigured);
        Assert.False(new StripeWebhookVerifier(secret)
            .TryVerify([], new HeaderDictionary(), DateTimeOffset.UtcNow, out _));
    }

    /// <summary>
    /// Which Metis account a subscription belongs to comes from checkout
    /// metadata and nowhere else. Matching on email address looks obvious and is
    /// an account takeover: anyone able to put someone else's address into a
    /// billing form could change that person's plan, or be handed theirs.
    /// </summary>
    [Fact]
    public void An_event_with_no_metis_user_id_changes_nothing()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"id":"evt_2","type":"customer.subscription.updated","data":{"object":{
              "id":"sub_1","customer":"cus_1","status":"active",
              "metadata":{"email":"someone@example.com","plan":"pro"}}}}
            """);

        var parsed = new StripeWebhookVerifier(StripeSecret).Parse(body)!;

        Assert.Null(parsed.MetisUserId);
        Assert.False(parsed.ChangesEntitlement);
    }

    /// <summary>
    /// A product or organisation event is verified and stored like any other,
    /// and then deliberately does nothing to anyone's plan.
    /// </summary>
    [Fact]
    public void A_non_subscription_event_changes_nothing()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"product.updated","data":{"id":"p_1",
              "metadata":{"metis_user_id":"33333333-3333-3333-3333-333333333333","plan":"pro"}}}
            """);

        Assert.False(new PolarWebhookVerifier(PolarSecret).Parse(body)!.ChangesEntitlement);
    }

    /// <summary>
    /// Metadata that names no plan resolves to Free rather than to a guess, on
    /// the same principle Entitlements.ParsePlan follows.
    /// </summary>
    [Fact]
    public void Metadata_with_no_plan_is_free()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"type":"subscription.active","data":{"id":"s_2","status":"active",
              "metadata":{"metis_user_id":"44444444-4444-4444-4444-444444444444"}}}
            """);

        Assert.Equal(PlanTier.Free, new PolarWebhookVerifier(PolarSecret).Parse(body)!.Plan);
    }

    // ------------------------------ Fixtures ------------------------------

    private static HeaderDictionary StripeHeaders(byte[] body, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString();
        var signed = Sign(Encoding.UTF8.GetBytes(StripeSecret),
            Encoding.UTF8.GetBytes(timestamp + ".").Concat(body).ToArray());

        return new HeaderDictionary
        {
            ["Stripe-Signature"] = $"t={timestamp},v1={Convert.ToHexString(signed).ToLowerInvariant()}"
        };
    }

    private static HeaderDictionary PolarHeaders(byte[] body, string id, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString();
        var message = Encoding.UTF8.GetBytes($"{id}.{timestamp}.").Concat(body).ToArray();
        var signed = Sign(PolarKey, message);

        return new HeaderDictionary
        {
            ["webhook-id"] = id,
            ["webhook-timestamp"] = timestamp,
            ["webhook-signature"] = "v1," + Convert.ToBase64String(signed)
        };
    }

    private static byte[] Sign(byte[] key, byte[] message)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(message);
    }
}
