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
    string? GoogleKey)
{
    public static GatewayConfig FromEnvironment()
    {
        var url = Require("SUPABASE_URL");
        var key = Require("SUPABASE_SERVICE_KEY");

        return new GatewayConfig(
            url.TrimEnd('/'),
            key,
            Entitlements.ParseEnvironment(System.Environment.GetEnvironmentVariable("METIS_ENV")),
            System.Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            System.Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            System.Environment.GetEnvironmentVariable("GOOGLE_API_KEY"));
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
        _ => null
    };

    /// <summary>
    /// Which providers this gateway can actually serve right now, as opposed to
    /// which ones the database says are intended. Configuration is the truth
    /// here, so a provider listed as managed but missing its key never appears.
    /// </summary>
    public IReadOnlyList<string> ManagedProviders =>
        new[] { "google", "anthropic", "openai" }
            .Where(provider => KeyFor(provider) is not null)
            .ToArray();

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

/// <summary>
/// Everything the gateway needs from Supabase, over its REST interface.
///
/// Two different credentials are used here on purpose. Validating a caller uses
/// <em>their</em> access token, so Supabase decides whether it is genuine.
/// Reading their role uses the service key, which bypasses row-level security —
/// that is exactly why it must never travel to a client, and why the only
/// questions asked with it are ones this file writes.
/// </summary>
public sealed class SupabaseGateway(HttpClient http, GatewayConfig config)
{
    /// <summary>
    /// Turns a caller's access token into a user id, by asking Supabase rather
    /// than by verifying the signature here.
    ///
    /// Verifying locally would be one fewer network call, and would also mean
    /// holding the project's JWT secret and reimplementing the checks Supabase
    /// already performs — including revocation, which a signature check cannot
    /// see. At this scale the round trip is the better trade: a token revoked a
    /// second ago stops working a second ago.
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
            _ = response;
        }
        catch (Exception)
        {
            // Metering must never be the reason a user's request fails. A lost
            // usage row costs a reporting gap; a thrown exception here would
            // cost the answer they were waiting for.
        }
    }

    private async Task<JsonElement?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, config.SupabaseUrl + path);
        Authorize(request);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private void Authorize(HttpRequestMessage request)
    {
        request.Headers.Add("apikey", config.ServiceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ServiceKey);
    }
}
