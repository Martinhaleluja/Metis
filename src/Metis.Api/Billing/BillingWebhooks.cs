using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Api.Billing;

/// <summary>
/// A subscription event, in Metis's own terms rather than any processor's.
///
/// Which processor Metis will use has not been decided. Everything downstream of
/// this record is written once and works with either, so making the decision is
/// a matter of setting a secret and creating three products — not of changing
/// any of this.
/// </summary>
public sealed record BillingEvent(
    string Provider,
    string EventId,
    string EventType,
    string? ExternalCustomerId,
    string? ExternalSubscriptionId,

    /// <summary>
    /// Which Metis account this is about.
    ///
    /// It comes from what the gateway itself set when it created the checkout —
    /// the metadata, or the external customer id — and never from matching an
    /// email address. Email matching looks obvious and is an account takeover:
    /// anyone who can buy a subscription with someone else's address in the
    /// billing form could otherwise change that person's plan, or be handed
    /// theirs.
    /// </summary>
    string? MetisUserId,

    PlanTier Plan,
    string Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd)
{
    /// <summary>
    /// Whether this event says anything about a subscription. Product and
    /// organisation events are verified and stored like any other, and then
    /// deliberately do nothing.
    /// </summary>
    public bool ChangesEntitlement =>
        ExternalSubscriptionId is not null
        && MetisUserId is not null
        && EventType.StartsWith("subscription.", StringComparison.Ordinal);
}

/// <summary>
/// Verifies that a webhook really came from the processor, and reads it.
///
/// Two implementations exist and both are inert without their secret, so the
/// choice of processor is a deployment decision rather than a code change.
/// </summary>
public interface IBillingWebhookVerifier
{
    string Provider { get; }

    /// <summary>Whether this verifier is configured at all.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Checks the signature over the raw request bytes.
    ///
    /// It takes bytes rather than a parsed object on purpose: the signature is
    /// over exactly what was sent, and round-tripping the body through a JSON
    /// deserialiser and back changes whitespace, key order and number formatting
    /// enough to break it. Reading the raw body first is the single most common
    /// mistake in webhook handling.
    /// </summary>
    bool TryVerify(ReadOnlySpan<byte> rawBody, IHeaderDictionary headers, DateTimeOffset now, out string reason);

    BillingEvent? Parse(ReadOnlySpan<byte> rawBody);
}

/// <summary>
/// Shared signature mechanics. Both processors sign an HMAC-SHA256 over a
/// timestamp and the body; they differ in how the pieces are named, encoded and
/// joined.
/// </summary>
internal static class WebhookCrypto
{
    /// <summary>
    /// How far out of step a webhook's timestamp may be.
    ///
    /// Without this the signature alone would make a captured request valid
    /// forever, so anyone who once saw one could replay it — including a
    /// "subscription active" event for an account that has since cancelled.
    /// </summary>
    internal static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    internal static bool Equal(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) =>
        CryptographicOperations.FixedTimeEquals(a, b);

    internal static byte[] Hmac(byte[] secret, ReadOnlySpan<byte> message)
    {
        using var hmac = new HMACSHA256(secret);
        return hmac.ComputeHash(message.ToArray());
    }

    internal static bool WithinTolerance(long unixSeconds, DateTimeOffset now) =>
        (now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).Duration() <= Tolerance;

    internal static PlanTier PlanFromMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in metadata.EnumerateObject())
            {
                var cleanName = prop.Name.Trim().ToLowerInvariant();
                if (cleanName is "plan" or "plan_id" or "metis_plan" && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var raw = prop.Value.GetString();
                    var trimmed = raw?.Replace("metis_", string.Empty, StringComparison.OrdinalIgnoreCase);
                    var parsed = Entitlements.ParsePlan(trimmed);
                    if (parsed != PlanTier.Free)
                    {
                        return parsed;
                    }
                }
            }
        }

        return PlanTier.Free;
    }

    internal static string? StringOrNull(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static DateTimeOffset? DateOrNull(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null
            : parent.TryGetProperty(name, out var epoch) && epoch.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(epoch.GetInt64())
                : null;
}

/// <summary>
/// Polar, which follows the Standard Webhooks specification: a <c>webhook-id</c>,
/// a <c>webhook-timestamp</c>, and a <c>webhook-signature</c> carrying one or
/// more space-separated <c>v1,&lt;base64&gt;</c> values signed over
/// <c>id.timestamp.body</c>.
/// </summary>
public sealed class PolarWebhookVerifier(string? secret) : IBillingWebhookVerifier
{
    public string Provider => "polar";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(secret);

    public bool TryVerify(ReadOnlySpan<byte> rawBody, IHeaderDictionary headers, DateTimeOffset now, out string reason)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrWhiteSpace(secret))
        {
            reason = "No Polar webhook secret is configured.";
            return false;
        }

        var id = headers["webhook-id"].ToString();
        if (string.IsNullOrEmpty(id)) id = headers["svix-id"].ToString();

        var timestamp = headers["webhook-timestamp"].ToString();
        if (string.IsNullOrEmpty(timestamp)) timestamp = headers["svix-timestamp"].ToString();

        var signatures = headers["webhook-signature"].ToString();
        if (string.IsNullOrEmpty(signatures)) signatures = headers["svix-signature"].ToString();

        if (id.Length == 0 || timestamp.Length == 0 || signatures.Length == 0)
        {
            reason = "Missing Standard Webhooks headers.";
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || !WebhookCrypto.WithinTolerance(seconds, now))
        {
            reason = "The webhook timestamp is outside the accepted window.";
            return false;
        }

        // Polar's secret is base64, optionally prefixed "whsec_". Standard Webhooks (Svix)
        // strips trailing '=' padding characters, so restore padding if needed.
        var material = secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret[6..] : secret;
        var padded = (material.Length % 4) switch
        {
            2 => material + "==",
            3 => material + "=",
            _ => material
        };
        byte[] key;
        try
        {
            key = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            key = Encoding.UTF8.GetBytes(material);
        }

        var message = new List<byte>(rawBody.Length + id.Length + timestamp.Length + 2);
        message.AddRange(Encoding.UTF8.GetBytes(id));
        message.Add((byte)'.');
        message.AddRange(Encoding.UTF8.GetBytes(timestamp));
        message.Add((byte)'.');
        message.AddRange(rawBody.ToArray());
        var signed = WebhookCrypto.Hmac(key, message.ToArray());

        foreach (var candidate in signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = candidate.IndexOf(',');
            if (comma < 0)
            {
                continue;
            }

            var sigPart = candidate[(comma + 1)..];
            var sigPadded = (sigPart.Length % 4) switch
            {
                2 => sigPart + "==",
                3 => sigPart + "=",
                _ => sigPart
            };

            try
            {
                var sigBytes = Convert.FromBase64String(sigPadded.Replace('-', '+').Replace('_', '/'));
                if (WebhookCrypto.Equal(signed, sigBytes))
                {
                    reason = string.Empty;
                    return true;
                }
            }
            catch (FormatException)
            {
                // A malformed candidate is simply not a match.
            }
        }

        reason = $"No signature matched. Computed: {Convert.ToBase64String(signed)}. Header had: {signatures}";
        return false;
    }

    public BillingEvent? Parse(ReadOnlySpan<byte> rawBody)
    {
        using var document = JsonDocument.Parse(rawBody.ToArray());
        var root = document.RootElement;

        var type = WebhookCrypto.StringOrNull(root, "type");
        if (type is null || !root.TryGetProperty("data", out var data))
        {
            return null;
        }

        var metadata = data.TryGetProperty("metadata", out var meta) ? meta : default;
        var plan = WebhookCrypto.PlanFromMetadata(metadata);
        if (plan == PlanTier.Free && data.TryGetProperty("product", out var product))
        {
            var productMeta = product.TryGetProperty("metadata", out var pMeta) ? pMeta : default;
            plan = WebhookCrypto.PlanFromMetadata(productMeta);
        }
        if (plan == PlanTier.Free)
        {
            var productId = WebhookCrypto.StringOrNull(data, "product_id")
                ?? (data.TryGetProperty("product", out var prod) ? WebhookCrypto.StringOrNull(prod, "id") : null);
            if (!string.IsNullOrEmpty(productId))
            {
                var proId = Environment.GetEnvironmentVariable("POLAR_PRODUCT_PRO");
                var maxId = Environment.GetEnvironmentVariable("POLAR_PRODUCT_MAX");
                if (string.Equals(productId, maxId, StringComparison.OrdinalIgnoreCase))
                {
                    plan = PlanTier.Max;
                }
                else if (string.Equals(productId, proId, StringComparison.OrdinalIgnoreCase))
                {
                    plan = PlanTier.Pro;
                }
            }
        }

        return new BillingEvent(
            "polar",
            WebhookCrypto.StringOrNull(data, "id") ?? Guid.NewGuid().ToString("n"),
            type,
            data.TryGetProperty("customer", out var customer)
                ? WebhookCrypto.StringOrNull(customer, "id")
                : WebhookCrypto.StringOrNull(data, "customer_id"),
            WebhookCrypto.StringOrNull(data, "id"),
            ReadMetisUserId(data, metadata),
            plan,
            WebhookCrypto.StringOrNull(data, "status") ?? "unknown",
            WebhookCrypto.DateOrNull(data, "current_period_end"),
            data.TryGetProperty("cancel_at_period_end", out var cancel)
                && cancel.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// Which Metis account this event is about, from the first of three places
    /// it may appear.
    ///
    /// The chain exists because only the first place is one Metis controls.
    /// <c>metadata.metis_user_id</c> is set on the <em>checkout</em>, and whether
    /// Polar copies checkout metadata onto the subscription object it emits
    /// afterwards is Polar's behaviour rather than ours — it may change, and it
    /// is not a thing to bet a payment on. The <c>external_customer_id</c> sent
    /// alongside it is stored on the <em>customer</em> record instead, which
    /// Polar includes on the subscription, so it survives where the metadata
    /// might not. <c>customer_external_id</c> is the same value again, flattened,
    /// which some payloads carry rather than nesting the customer.
    ///
    /// Reading only the first would make the quietest failure in the whole
    /// system: with no id the event changes no entitlement, the handler stores it
    /// and answers 200, the processor is satisfied and never retries — and the
    /// customer has paid, seen a receipt, and been given nothing. Nobody would
    /// find out until they complained.
    ///
    /// All three sources are things the gateway wrote at checkout, so none of
    /// this weakens the rule the type's own comment states: the account is never
    /// resolved from anything the buyer typed into the payment form.
    /// </summary>
    private static string? ReadMetisUserId(JsonElement data, JsonElement metadata)
    {
        // An empty string counts as absent at every step rather than as an
        // answer. A present-but-blank field would otherwise stop the chain and
        // be carried into apply_subscription as the account to change, which is
        // a worse failure than the one this chain exists to prevent.
        if (metadata.ValueKind == JsonValueKind.Object
            && NotBlank(WebhookCrypto.StringOrNull(metadata, "metis_user_id")) is { } fromMetadata)
        {
            return fromMetadata;
        }

        if (data.TryGetProperty("customer", out var customer)
            && customer.ValueKind == JsonValueKind.Object
            && NotBlank(WebhookCrypto.StringOrNull(customer, "external_id")) is { } fromCustomer)
        {
            return fromCustomer;
        }

        return NotBlank(WebhookCrypto.StringOrNull(data, "customer_external_id"));
    }

    private static string? NotBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Stripe, which sends <c>Stripe-Signature: t=&lt;unix&gt;,v1=&lt;hex&gt;</c>
/// signed over <c>timestamp.body</c> with the raw secret as the HMAC key.
/// </summary>
public sealed class StripeWebhookVerifier(string? secret) : IBillingWebhookVerifier
{
    public string Provider => "stripe";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(secret);

    public bool TryVerify(ReadOnlySpan<byte> rawBody, IHeaderDictionary headers, DateTimeOffset now, out string reason)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrWhiteSpace(secret))
        {
            reason = "No Stripe webhook secret is configured.";
            return false;
        }

        var header = headers["Stripe-Signature"].ToString();
        if (header.Length == 0)
        {
            reason = "Missing Stripe-Signature header.";
            return false;
        }

        string? timestamp = null;
        var candidates = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = part.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var name = part[..equals];
            var value = part[(equals + 1)..];
            if (name == "t")
            {
                timestamp = value;
            }
            else if (name == "v1")
            {
                candidates.Add(value);
            }
        }

        if (timestamp is null || candidates.Count == 0)
        {
            reason = "The Stripe-Signature header was not in the expected form.";
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || !WebhookCrypto.WithinTolerance(seconds, now))
        {
            reason = "The webhook timestamp is outside the accepted window.";
            return false;
        }

        var message = new List<byte>(rawBody.Length + timestamp.Length + 1);
        message.AddRange(Encoding.UTF8.GetBytes(timestamp));
        message.Add((byte)'.');
        message.AddRange(rawBody.ToArray());
        var signed = WebhookCrypto.Hmac(Encoding.UTF8.GetBytes(secret), message.ToArray());

        foreach (var candidate in candidates)
        {
            try
            {
                if (WebhookCrypto.Equal(signed, Convert.FromHexString(candidate)))
                {
                    reason = string.Empty;
                    return true;
                }
            }
            catch (FormatException)
            {
                // Not hex, therefore not a match.
            }
        }

        reason = "No signature matched.";
        return false;
    }

    public BillingEvent? Parse(ReadOnlySpan<byte> rawBody)
    {
        using var document = JsonDocument.Parse(rawBody.ToArray());
        var root = document.RootElement;

        var type = WebhookCrypto.StringOrNull(root, "type");
        if (type is null
            || !root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("object", out var subject))
        {
            return null;
        }

        var metadata = subject.TryGetProperty("metadata", out var meta) ? meta : default;

        // Stripe names its subscription events customer.subscription.*; they are
        // normalised to Metis's own subscription.* so everything downstream sees
        // one vocabulary.
        var normalised = type.StartsWith("customer.subscription.", StringComparison.Ordinal)
            ? "subscription." + type["customer.subscription.".Length..]
            : type;

        return new BillingEvent(
            "stripe",
            WebhookCrypto.StringOrNull(root, "id") ?? Guid.NewGuid().ToString("n"),
            normalised,
            WebhookCrypto.StringOrNull(subject, "customer"),
            WebhookCrypto.StringOrNull(subject, "id"),
            metadata.ValueKind == JsonValueKind.Object ? WebhookCrypto.StringOrNull(metadata, "metis_user_id") : null,
            WebhookCrypto.PlanFromMetadata(metadata),
            WebhookCrypto.StringOrNull(subject, "status") ?? "unknown",
            WebhookCrypto.DateOrNull(subject, "current_period_end"),
            subject.TryGetProperty("cancel_at_period_end", out var cancel)
                && cancel.ValueKind == JsonValueKind.True);
    }
}
