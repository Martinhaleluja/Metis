using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Api;

/// <summary>
/// The gateway's own configuration, read once at startup.
///
/// Every secret here comes from the environment. Nothing in this list may ever
/// be committed, defaulted to a working value, or sent to a client: the whole
/// reason this service exists is that a desktop application cannot hold these.
/// </summary>
public sealed record GatewayConfig(
    string SupabaseUrl,
    string ServiceKey,
    MetisEnvironment Environment,
    string? AnthropicKey,
    string? OpenAiKey,
    string? GoogleKey,
    string? MistralKey,
    string? OpenRouterKey,
    string? EntitlementSigningKey,
    IReadOnlyList<string> AllowedOrigins,
    string? PolarWebhookSecret,
    string? StripeWebhookSecret)
{
    public static GatewayConfig FromEnvironment()
    {
        var url = Require("SUPABASE_URL");
        var key = Require("SUPABASE_SERVICE_KEY");

        return new GatewayConfig(
            url.TrimEnd('/'),
            key,
            Entitlements.ParseEnvironment(Read("METIS_ENV")),
            Read("ANTHROPIC_API_KEY"),
            Read("OPENAI_API_KEY"),
            Read("GOOGLE_API_KEY"),
            Read("MISTRAL_API_KEY"),
            Read("OPENROUTER_API_KEY"),
            Read("METIS_ENTITLEMENT_SIGNING_KEY"),
            (Read("ALLOWED_ORIGINS") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Read("POLAR_WEBHOOK_SECRET"),
            Read("STRIPE_WEBHOOK_SECRET"));
    }

    /// <summary>
    /// The key for a provider, or null when Metis does not supply it.
    ///
    /// Google is the one Metis pays for. The others are kept deliberately: the
    /// day a key is set for one of them it becomes available with no code
    /// change, and until then it is refused with a reason rather than being
    /// silently absent from a list.
    /// </summary>
    public string? KeyFor(string provider) => provider switch
    {
        "google" => Blank(GoogleKey),
        "anthropic" => Blank(AnthropicKey),
        "openai" => Blank(OpenAiKey),
        "mistral" => Blank(MistralKey),
        "openrouter" => Blank(OpenRouterKey),
        _ => null
    };

    /// <summary>
    /// Which providers this gateway can actually serve right now, as opposed to
    /// which ones the database says are intended. Configuration is the truth
    /// here, so a provider listed as managed but missing its key never appears.
    /// </summary>
    public IReadOnlyList<string> ManagedProviders =>
        new[] { "google", "anthropic", "openai", "mistral", "openrouter" }
            .Where(provider => KeyFor(provider) is not null)
            .ToArray();

    private static string? Read(string name) => Blank(System.Environment.GetEnvironmentVariable(name));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Refuses to start without a required secret rather than running and
    /// failing on the first request. A gateway that boots without its service
    /// key looks healthy and rejects every user.
    /// </summary>
    private static string Require(string name)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"{name} is not set. The gateway will not start without it.")
            : value;
    }
}

/// <summary>What the cost-protection switch is currently doing.</summary>
public enum CostProtection
{
    Off,
    Degrade,
    Refuse
}

/// <summary>
/// The operating rules, as the database currently states them: whether billing
/// is live, whether the emergency brake is on, and what each plan is allowed.
/// </summary>
public sealed record GatewayRules(
    bool BillingIsLive,
    CostProtection Protection,
    string? ProtectionNote,
    IReadOnlyList<string> PausedModels,
    IReadOnlyDictionary<PlanTier, PlanLimits> Limits)
{
    /// <summary>
    /// What to assume before the first successful read, and after a failed one.
    ///
    /// Billing off and every allowance zero. That combination is deliberate and
    /// looks contradictory at a glance: billing off means the client shows
    /// everything as available, while a zero budget means the gateway spends
    /// nothing. It is the right pair. If this service cannot read its own rules
    /// it must not start spending money against limits it is guessing at, and it
    /// must not start refusing capabilities to people it cannot prove have lost
    /// them. Refuse to pay, refuse to punish.
    /// </summary>
    public static GatewayRules Unknown { get; } = new(
        BillingIsLive: false,
        Protection: CostProtection.Off,
        ProtectionNote: null,
        PausedModels: Array.Empty<string>(),
        Limits: new Dictionary<PlanTier, PlanLimits>());

    public PlanLimits For(PlanTier plan) =>
        Limits.TryGetValue(plan, out var limits) ? limits : PlanLimits.Unknown;
}

/// <summary>What an account has spent this calendar month.</summary>
public sealed record UsageSnapshot(
    decimal SpendUsd,
    int RequestCount,
    int AgentSteps,
    DateTimeOffset PeriodStart)
{
    public static UsageSnapshot Empty { get; } = new(0m, 0, 0, DateTimeOffset.UtcNow);

    /// <summary>
    /// When this month's allowance resets. The first instant of next month, in
    /// UTC, matching the date_trunc the database counts from.
    /// </summary>
    public DateTimeOffset ResetsUtc => new DateTimeOffset(
        PeriodStart.UtcDateTime.Year, PeriodStart.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
}

/// <summary>
/// Everything the gateway needs from Supabase, over its REST interface.
///
/// Two different credentials are used here on purpose. Validating a caller uses
/// <em>their</em> access token, so Supabase decides whether it is genuine.
/// Reading their role uses the service key, which bypasses row-level security —
/// that is exactly why it must never travel to a client, and why the only
/// questions asked with it are ones this file writes.
/// </summary>
public sealed class SupabaseGateway(HttpClient http, GatewayConfig config, ILogger<SupabaseGateway> log)
{
    /// <summary>
    /// How many usage rows have failed to write since the process started.
    ///
    /// Losing a usage row must never fail a user's request, so the write below
    /// swallows its exceptions. But a metering path that is *permanently* broken
    /// is invisible under that rule, and an invisible one means the budget never
    /// fills and Metis spends without limit believing it has spent nothing. That
    /// is a financial risk rather than a reporting gap, so the swallowing stays
    /// and this counts what it swallowed.
    /// </summary>
    public long DroppedUsageWrites => Interlocked.Read(ref _droppedUsageWrites);

    private long _droppedUsageWrites;

    /// <summary>
    /// Turns a caller's access token into a user id, by asking Supabase rather
    /// than by verifying the signature here.
    ///
    /// Verifying locally would be one fewer network call, and would also mean
    /// holding the project's JWT secret and reimplementing the checks Supabase
    /// already performs — including revocation, which a signature check cannot
    /// see. The round trip is the better trade, and the authentication handler
    /// caches the result for sixty seconds so a busy session does not pay for it
    /// on every turn: a token revoked a minute ago stops working a minute ago,
    /// which is still far better than a signature check would manage.
    /// </summary>
    public async Task<string?> ResolveUserIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.SupabaseUrl}/auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("apikey", config.ServiceKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    /// <summary>
    /// The caller's account as the server understands it. This is the copy that
    /// decides what may happen; the client has its own copy and it decides only
    /// what to show.
    /// </summary>
    public async Task<MetisAccount?> LoadAccountAsync(string userId, CancellationToken cancellationToken)
    {
        var rows = await GetAsync(
            $"/rest/v1/account_status?user_id=eq.{Uri.EscapeDataString(userId)}&select=role,plan,email_verified",
            cancellationToken);

        if (rows is null || rows.Value.GetArrayLength() == 0)
        {
            return null;
        }

        var row = rows.Value[0];
        return new MetisAccount(
            userId,
            Entitlements.ParseRole(row.GetProperty("role").GetString()),
            Entitlements.ParsePlan(row.GetProperty("plan").GetString()),
            config.Environment,
            row.GetProperty("email_verified").GetBoolean());
    }

    /// <summary>
    /// The current operating rules. Read on a timer rather than per request, so
    /// pulling the cost-protection lever takes effect within a minute without
    /// putting two extra queries in front of every turn.
    /// </summary>
    public async Task<GatewayRules?> LoadRulesAsync(CancellationToken cancellationToken)
    {
        var state = await GetAsync(
            "/rest/v1/billing_state?id=eq.true&select=billing_is_live,cost_protection_mode,cost_protection_note,managed_models_paused",
            cancellationToken);
        var limitRows = await GetAsync("/rest/v1/plan_limits?select=*", cancellationToken);

        if (state is null || state.Value.GetArrayLength() == 0 || limitRows is null)
        {
            return null;
        }

        var row = state.Value[0];
        var limits = new Dictionary<PlanTier, PlanLimits>();
        foreach (var limit in limitRows.Value.EnumerateArray())
        {
            limits[Entitlements.ParsePlan(limit.GetProperty("plan").GetString())] = new PlanLimits(
                limit.GetProperty("monthly_budget_usd").GetDecimal(),
                limit.GetProperty("max_screenshot_bytes").GetInt32(),
                limit.GetProperty("requests_per_minute").GetInt32(),
                limit.GetProperty("burst_requests").GetInt32(),
                limit.GetProperty("max_agent_steps_per_month").GetInt32(),
                limit.GetProperty("max_agent_steps_per_task").GetInt32(),
                limit.GetProperty("memory_entries_max").GetInt32(),
                limit.GetProperty("managed_models").EnumerateArray()
                    .Select(model => model.GetString() ?? string.Empty)
                    .Where(model => model.Length > 0)
                    .ToArray());
        }

        return new GatewayRules(
            row.GetProperty("billing_is_live").GetBoolean(),
            ParseProtection(row.TryGetProperty("cost_protection_mode", out var mode) ? mode.GetString() : null),
            row.TryGetProperty("cost_protection_note", out var note) ? note.GetString() : null,
            row.TryGetProperty("managed_models_paused", out var paused)
                ? paused.EnumerateArray().Select(model => model.GetString() ?? string.Empty).Where(m => m.Length > 0).ToArray()
                : Array.Empty<string>(),
            limits);
    }

    /// <summary>Every model price currently on file, newest row per model wins.</summary>
    public async Task<IReadOnlyList<ModelPrice>> LoadModelPricesAsync(CancellationToken cancellationToken)
    {
        var rows = await GetAsync(
            "/rest/v1/model_prices?select=provider,model,input_usd_per_mtok,output_usd_per_mtok,effective_from&order=effective_from.desc",
            cancellationToken);

        if (rows is null)
        {
            return Array.Empty<ModelPrice>();
        }

        return rows.Value.EnumerateArray()
            .Select(row => new ModelPrice(
                row.GetProperty("provider").GetString() ?? string.Empty,
                row.GetProperty("model").GetString() ?? string.Empty,
                row.GetProperty("input_usd_per_mtok").GetDecimal(),
                row.GetProperty("output_usd_per_mtok").GetDecimal(),
                row.GetProperty("effective_from").GetDateTimeOffset()))
            .ToArray();
    }

    /// <summary>What this account has spent this calendar month.</summary>
    public async Task<UsageSnapshot> LoadUsageAsync(string userId, CancellationToken cancellationToken)
    {
        var rows = await PostRpcAsync("usage_this_period", new { target = userId }, cancellationToken);
        if (rows is null || rows.Value.ValueKind != JsonValueKind.Array || rows.Value.GetArrayLength() == 0)
        {
            return UsageSnapshot.Empty;
        }

        var row = rows.Value[0];
        return new UsageSnapshot(
            row.GetProperty("spend_usd").GetDecimal(),
            row.GetProperty("request_count").GetInt32(),
            row.GetProperty("agent_steps").GetInt32(),
            row.GetProperty("period_start").GetDateTimeOffset());
    }

    /// <summary>
    /// Records what a request cost. Deliberately carries no prompt, no
    /// screenshot and no response — this answers "how much, how often, how
    /// slow" and is not a record of what anyone had on their screen.
    /// </summary>
    public async Task RecordUsageAsync(
        string userId,
        string requestId,
        string provider,
        string? model,
        string feature,
        int? inputTokens,
        int? outputTokens,
        decimal? estimatedCostUsd,
        long latencyMs,
        string status,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            user_id = userId,
            request_id = requestId,
            provider,
            model,
            feature,
            input_tokens = inputTokens,
            output_tokens = outputTokens,
            estimated_cost_usd = estimatedCostUsd,
            latency_ms = (int)latencyMs,
            status,
            environment = config.Environment.ToString().ToLowerInvariant()
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.SupabaseUrl}/rest/v1/usage_events")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _droppedUsageWrites);
                log.LogError(
                    "Usage row rejected with {Status} for request {RequestId}. The monthly budget is now under-counting.",
                    (int)response.StatusCode,
                    requestId);
            }
        }
        catch (Exception exception)
        {
            // Metering must never be the reason a user's request fails. A lost
            // usage row costs a reporting gap; a thrown exception here would
            // cost the answer they were waiting for. It is still logged and
            // counted, because a metering path that silently stopped working is
            // a budget that silently stopped being enforced.
            Interlocked.Increment(ref _droppedUsageWrites);
            log.LogError(exception, "Usage row lost for request {RequestId}.", requestId);
        }
    }

    /// <summary>Writes an audit entry. Never given anything secret to write.</summary>
    public async Task RecordAuditAsync(
        string userId,
        string action,
        object detail,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { user_id = userId, action, detail });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.SupabaseUrl}/rest/v1/audit_logs")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                log.LogError("Audit row rejected with {Status} for {Action}.", (int)response.StatusCode, action);
            }
        }
        catch (Exception exception)
        {
            log.LogError(exception, "Audit row lost for {Action}.", action);
        }
    }

    /// <summary>The providers a customer may connect their own key for.</summary>
    public async Task<IReadOnlyList<string>> LoadByoProvidersAsync(CancellationToken cancellationToken)
    {
        var rows = await GetAsync(
            "/rest/v1/ai_providers?select=key&enabled=eq.true&byo_available=eq.true",
            cancellationToken);

        return rows is null
            ? Array.Empty<string>()
            : rows.Value.EnumerateArray()
                .Select(row => row.GetProperty("key").GetString() ?? string.Empty)
                .Where(key => key.Length > 0)
                .ToArray();
    }

    /// <summary>Stores a customer's own provider key in Supabase Vault.</summary>
    public async Task<bool> StoreProviderSecretAsync(
        string userId, string provider, string secret, string hint, string? model, CancellationToken cancellationToken)
    {
        var result = await PostRpcAsync(
            "store_provider_secret",
            new { target = userId, provider_key = provider, secret, hint, model_name = model },
            cancellationToken);
        return result is not null;
    }

    /// <summary>Removes a customer's stored key, and the vault secret with it.</summary>
    public async Task<bool> ForgetProviderSecretAsync(
        string userId, string provider, CancellationToken cancellationToken)
    {
        var result = await PostRpcAsync(
            "forget_provider_secret",
            new { target = userId, provider_key = provider },
            cancellationToken);
        return result is { ValueKind: JsonValueKind.True };
    }

    /// <summary>
    /// Reads a customer's own key back so the gateway can call their provider on
    /// their credential. The most dangerous call in this file; it exists only
    /// for the Pro bring-your-own path and its result must never be logged,
    /// echoed in a response, or held longer than the request that needed it.
    /// </summary>
    public async Task<string?> ReadProviderSecretAsync(
        string userId, string provider, CancellationToken cancellationToken)
    {
        var result = await PostRpcAsync(
            "read_provider_secret",
            new { target = userId, provider_key = provider },
            cancellationToken);
        return result is { ValueKind: JsonValueKind.String } ? result.Value.GetString() : null;
    }

    /// <summary>The customer's connected providers, with hints and never keys.</summary>
    public async Task<JsonElement?> LoadConnectionsAsync(string userId, CancellationToken cancellationToken) =>
        await GetAsync(
            $"/rest/v1/user_ai_connections?user_id=eq.{Uri.EscapeDataString(userId)}&select=provider,model,key_hint,last_tested_at,last_test_ok",
            cancellationToken);

    /// <summary>
    /// Records a verified webhook, returning false when this event has already
    /// been applied. Redelivery is normal rather than exceptional: every
    /// processor retries, so the first thing an endpoint needs is a way to
    /// recognise an event it has already acted on.
    /// </summary>
    public async Task<bool> TryClaimBillingEventAsync(
        string provider, string eventId, string eventType, string rawPayload, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(
            $"/rest/v1/billing_events?provider=eq.{Uri.EscapeDataString(provider)}&event_id=eq.{Uri.EscapeDataString(eventId)}&select=processed_at",
            cancellationToken);

        if (existing is { ValueKind: JsonValueKind.Array } && existing.Value.GetArrayLength() > 0)
        {
            var processed = existing.Value[0].GetProperty("processed_at");
            return processed.ValueKind == JsonValueKind.Null;
        }

        var payload = JsonSerializer.Serialize(new
        {
            provider,
            event_id = eventId,
            event_type = eventType,
            payload = JsonSerializer.Deserialize<JsonElement>(rawPayload)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.SupabaseUrl}/rest/v1/billing_events")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);
        request.Headers.Add("Prefer", "resolution=ignore-duplicates");

        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Marks a webhook applied, or records why it could not be.</summary>
    public async Task FinishBillingEventAsync(
        string provider, string eventId, string? error, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            processed_at = error is null ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
            error
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{config.SupabaseUrl}/rest/v1/billing_events?provider=eq.{Uri.EscapeDataString(provider)}&event_id=eq.{Uri.EscapeDataString(eventId)}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            log.LogError("Could not close billing event {Provider}/{EventId}.", provider, eventId);
        }
    }

    /// <summary>Turns a verified subscription event into an entitlement.</summary>
    public async Task<PlanTier?> ApplySubscriptionAsync(
        string provider,
        string subscriptionId,
        string userId,
        PlanTier plan,
        string status,
        DateTimeOffset? periodEnd,
        bool cancelAtPeriodEnd,
        string? customerId,
        CancellationToken cancellationToken)
    {
        var result = await PostRpcAsync(
            "apply_subscription",
            new
            {
                billing_provider = provider,
                external_subscription_id = subscriptionId,
                target = userId,
                plan = plan.ToString().ToLowerInvariant(),
                status,
                period_end = periodEnd,
                cancels_at_period_end = cancelAtPeriodEnd,
                external_customer = customerId
            },
            cancellationToken);

        return result is { ValueKind: JsonValueKind.String }
            ? Entitlements.ParsePlan(result.Value.GetString())
            : null;
    }

    private static CostProtection ParseProtection(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "degrade" => CostProtection.Degrade,
        "refuse" => CostProtection.Refuse,
        _ => CostProtection.Off
    };

    private async Task<JsonElement?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, config.SupabaseUrl + path);
        Authorize(request);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning("Supabase GET {Path} returned {Status}.", Redact(path), (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private async Task<JsonElement?> PostRpcAsync(string function, object arguments, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.SupabaseUrl}/rest/v1/rpc/{function}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(arguments), Encoding.UTF8, "application/json")
        };
        Authorize(request);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The arguments are deliberately not logged. store_provider_secret
            // takes a customer's API key as an argument, and a failure that
            // prints its own inputs is how secrets end up in log aggregators.
            log.LogError("Supabase RPC {Function} returned {Status}.", function, (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Strips a user id out of a path before it is logged. A log line that names
    /// which account was being read is a log line that has to be treated as
    /// personal data forever afterwards.
    /// </summary>
    private static string Redact(string path)
    {
        var marker = path.IndexOf("user_id=eq.", StringComparison.Ordinal);
        return marker < 0 ? path : path[..(marker + 11)] + "...";
    }

    private void Authorize(HttpRequestMessage request)
    {
        request.Headers.Add("apikey", config.ServiceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ServiceKey);
    }
}

/// <summary>One row of the price list.</summary>
public sealed record ModelPrice(
    string Provider,
    string Model,
    decimal InputUsdPerMillion,
    decimal OutputUsdPerMillion,
    DateTimeOffset EffectiveFrom);

/// <summary>
/// What a turn cost, worked out from the token counts the provider reported.
///
/// The interesting decision is the fallback. A model nobody has priced could be
/// costed at zero, which is tidy and wrong: an unpriced model would then be
/// invisible to the budget and could be used without limit. So an unknown model
/// is priced at the most expensive row that provider has, and the usage row is
/// marked so it shows up in reporting as an estimate rather than a measurement.
/// Guessing high is a slightly overstated bill; guessing zero is an unbounded
/// one.
/// </summary>
public sealed class ModelPriceBook(IReadOnlyList<ModelPrice> prices)
{
    private readonly Dictionary<(string Provider, string Model), ModelPrice> _exact =
        prices
            .GroupBy(price => (price.Provider, price.Model))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(price => price.EffectiveFrom).First());

    private readonly Dictionary<string, ModelPrice> _dearest =
        prices
            .GroupBy(price => price.Provider)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(price => price.OutputUsdPerMillion).First());

    public static ModelPriceBook Empty { get; } = new(Array.Empty<ModelPrice>());

    public (decimal Cost, bool Estimated) Estimate(string provider, string? model, int inputTokens, int outputTokens)
    {
        var key = (provider, model ?? string.Empty);
        if (_exact.TryGetValue(key, out var price))
        {
            return (Compute(price, inputTokens, outputTokens), false);
        }

        if (_dearest.TryGetValue(provider, out var fallback))
        {
            return (Compute(fallback, inputTokens, outputTokens), true);
        }

        // No price at all for this provider. Returning zero here would be the
        // silent-unbounded case again, so it is reported as estimated with no
        // number, and the caller records the status that says so.
        return (0m, true);
    }

    private static decimal Compute(ModelPrice price, int inputTokens, int outputTokens) =>
        Math.Round(
            (inputTokens / 1_000_000m * price.InputUsdPerMillion)
            + (outputTokens / 1_000_000m * price.OutputUsdPerMillion),
            6,
            MidpointRounding.AwayFromZero);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{_exact.Count} priced models across {_dearest.Count} providers");
}
