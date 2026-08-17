using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Api;
using Metis.Core.Models;
using Metis.Core.Services;

// The Metis AI gateway.
//
// It exists for one reason above all others: provider API keys must not live in
// a program that runs on someone else's computer. Everything else it does —
// metering, plan enforcement, provider choice — follows from having put the
// keys somewhere the user cannot read them.

var builder = WebApplication.CreateBuilder(args);
var config = GatewayConfig.FromEnvironment();

builder.Services.AddSingleton(config);
builder.Services.AddHttpClient<SupabaseGateway>();
builder.Services.AddHttpClient("providers", client => client.Timeout = TimeSpan.FromSeconds(120));

var app = builder.Build();

// Liveness only. Deliberately says nothing about configuration: a health check
// is reachable by anyone, so it must not become a way to learn which providers
// are configured or which environment this is.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

/// Resolves the caller from the Authorization header, or explains the refusal.
async Task<(MetisAccount? Account, IResult? Failure)> AuthenticateAsync(
    HttpRequest request,
    SupabaseGateway supabase,
    CancellationToken cancellationToken)
{
    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return (null, Results.Unauthorized());
    }

    var userId = await supabase.ResolveUserIdAsync(header[7..].Trim(), cancellationToken);
    if (userId is null)
    {
        return (null, Results.Unauthorized());
    }

    var account = await supabase.LoadAccountAsync(userId, cancellationToken);

    // Authenticated but with no account row is a broken state rather than an
    // unauthorised one, and is worth telling apart when reading logs.
    return account is null
        ? (null, Results.Problem("No account record exists for this user.", statusCode: 409))
        : (account, null);
}

// What the client is allowed to show. The client has the same rules compiled
// in; this is the copy that agrees with the database.
app.MapGet("/v1/me", async (
    HttpRequest request,
    SupabaseGateway supabase,
    CancellationToken cancellationToken) =>
{
    var (account, failure) = await AuthenticateAsync(request, supabase, cancellationToken);
    if (failure is not null)
    {
        return failure;
    }

    var features = Enum.GetValues<MetisFeature>()
        .Where(feature => Entitlements.Has(account!, feature))
        .Select(feature => feature.ToString())
        .ToArray();

    return Results.Ok(new
    {
        userId = account!.UserId,
        role = account.Role.ToString().ToLowerInvariant(),
        plan = account.Plan.ToString().ToLowerInvariant(),
        environment = account.Environment.ToString().ToLowerInvariant(),
        emailVerified = account.EmailVerified,
        features
    });
});

// The gateway proper.
app.MapPost("/v1/chat", async (
    HttpRequest request,
    SupabaseGateway supabase,
    IHttpClientFactory clients,
    GatewayConfig gatewayConfig,
    CancellationToken cancellationToken) =>
{
    var (account, failure) = await AuthenticateAsync(request, supabase, cancellationToken);
    if (failure is not null)
    {
        return failure;
    }

    ChatRequest? body;
    try
    {
        body = await request.ReadFromJsonAsync<ChatRequest>(cancellationToken);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "The request body was not readable JSON." });
    }

    if (body is null || string.IsNullOrWhiteSpace(body.Prompt))
    {
        return Results.BadRequest(new { error = "A prompt is required." });
    }

    var requestId = Guid.NewGuid().ToString("n");
    var stopwatch = Stopwatch.StartNew();

    // A provider is offered by the gateway if, and only if, its key is
    // configured here. Google is the one Metis pays for; the rest stay in the
    // switch so that setting ANTHROPIC_API_KEY or OPENAI_API_KEY later is all
    // it takes to offer them, with no code change. Until then they are refused
    // with the reason, not silently missing.
    var providerKey = body.Provider?.Trim().ToLowerInvariant() ?? "google";
    var apiKey = gatewayConfig.KeyFor(providerKey);

    if (apiKey is null)
    {
        return Results.Json(
            new
            {
                error = $"Metis does not currently supply '{providerKey}'.",
                managed = gatewayConfig.ManagedProviders,
                hint = "Connect your own key for this provider on a Pro account, or use a managed one."
            },
            statusCode: 400);
    }

    var http = clients.CreateClient("providers");
    string status = "ok";
    int? inputTokens = null;
    int? outputTokens = null;

    try
    {
        // Only Google is wired. The others are deliberately explicit gaps
        // rather than a half-working switch: a provider that silently returns
        // nothing is worse than one that says it is missing.
        if (providerKey != "google")
        {
            return Results.Problem($"The '{providerKey}' adapter is not implemented yet.", statusCode: 501);
        }

        var model = string.IsNullOrWhiteSpace(body.Model) ? "gemini-2.0-flash" : body.Model;
        var payload = JsonSerializer.Serialize(new
        {
            systemInstruction = string.IsNullOrWhiteSpace(body.System)
                ? null
                : new { parts = new[] { new { text = body.System } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = body.Prompt } } }
            },
            generationConfig = new { temperature = 0.1, maxOutputTokens = 2048 }
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        // The key goes in a header rather than the query string, so it cannot
        // end up in a proxy log or an error page along with the URL.
        using var upstream = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        upstream.Headers.Add("x-goog-api-key", apiKey);

        using var response = await http.SendAsync(upstream, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            status = $"provider_{(int)response.StatusCode}";

            // The provider's own error text can name the account or the key, so
            // the caller gets the status and nothing else.
            return Results.Problem("The AI provider refused the request.", statusCode: 502);
        }

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty("usageMetadata", out var usage))
        {
            inputTokens = usage.TryGetProperty("promptTokenCount", out var input) ? input.GetInt32() : null;
            outputTokens = usage.TryGetProperty("candidatesTokenCount", out var output) ? output.GetInt32() : null;
        }

        return Results.Content(text, "application/json");
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        status = "error";
        return Results.Problem("The gateway could not reach the AI provider.", statusCode: 502);
    }
    finally
    {
        stopwatch.Stop();
        await supabase.RecordUsageAsync(
            account!.UserId,
            requestId,
            providerKey,
            body?.Model,
            body?.Feature ?? "chat",
            inputTokens,
            outputTokens,
            stopwatch.ElapsedMilliseconds,
            status,
            CancellationToken.None);
    }
});

// Connecting a personal provider key. Pro and staff only, enforced here rather
// than in the client.
app.MapPost("/v1/connections", async (
    HttpRequest request,
    SupabaseGateway supabase,
    CancellationToken cancellationToken) =>
{
    var (account, failure) = await AuthenticateAsync(request, supabase, cancellationToken);
    if (failure is not null)
    {
        return failure;
    }

    if (!Entitlements.Has(account!, MetisFeature.CustomAiProvider))
    {
        return Results.Json(
            new { error = Entitlements.Explain(account!, MetisFeature.CustomAiProvider) },
            statusCode: 403);
    }

    return Results.Problem(
        "Storing a personal provider key is not implemented yet: it needs the Supabase Vault write path.",
        statusCode: 501);
});

app.Run();

/// <summary>What a client asks the gateway for. No screen content is accepted here yet.</summary>
public sealed record ChatRequest(
    string Prompt,
    string? System = null,
    string? Provider = null,
    string? Model = null,
    string? Feature = null);
