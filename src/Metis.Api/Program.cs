using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Metis.Api;
using Metis.Api.Billing;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

// The Metis AI gateway.
//
// It exists for one reason above all others: provider API keys must not live in
// a program that runs on someone else's computer. Everything else it does —
// metering, plan enforcement, provider choice, cost protection — follows from
// having put the keys somewhere the user cannot read them.
//
// The thing to keep in mind while reading it: a request only ever arrives here
// when Metis is the one paying. A user running on their own key never reaches
// this service at all, which is what makes it safe for the budget checks below
// to be as strict as they are.

var builder = WebApplication.CreateBuilder(args);
var config = GatewayConfig.FromEnvironment();

builder.Services.AddSingleton(config);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<SupabaseGateway>();
builder.Services.AddHttpClient("providers", client => client.Timeout = TimeSpan.FromSeconds(120));

// The payment processor gets its own client rather than borrowing the provider
// one. Creating a checkout session is a small call with a person standing in
// front of it waiting to be taken to a payment form, and the two minutes that
// suit a long model answer would leave them watching a spinner for two minutes
// before finding out it had failed.
builder.Services.AddHttpClient("billing", client => client.Timeout = TimeSpan.FromSeconds(20));

builder.Services.AddSingleton<ProviderKeyValidator>();
builder.Services.AddSingleton<GatewayState>();
builder.Services.AddHostedService(services => services.GetRequiredService<GatewayState>());

// Both verifiers are registered whether or not their secret is set. An
// unconfigured one reports itself as unconfigured and the endpoint answers 404,
// which is deliberate: a 501 would tell anyone probing exactly which processors
// this deployment knows about.
builder.Services.AddSingleton<IBillingWebhookVerifier>(new PolarWebhookVerifier(config.PolarWebhookSecret));
builder.Services.AddSingleton<IBillingWebhookVerifier>(new StripeWebhookVerifier(config.StripeWebhookSecret));

// Structured logs, because these are read in Render's log viewer rather than in
// a terminal. Nothing below ever logs a prompt, an automation context (up to
// 120 KB of whatever is on the user's screen), a screenshot, or a provider key.
// It will be tempting during the first difficult debugging session, and that is
// exactly when it would happen.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services
    .AddAuthentication(SupabaseTokenHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SupabaseTokenHandler>(SupabaseTokenHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // Never AllowAnyOrigin on a service that spends money. With no origins
    // configured the browser simply cannot call it, which is the right default
    // for a gateway whose only required client is a desktop application.
    if (config.AllowedOrigins.Count > 0)
    {
        policy.WithOrigins([.. config.AllowedOrigins])
              .WithMethods("GET", "POST", "DELETE")
              .WithHeaders("Authorization", "Content-Type")
              .WithExposedHeaders("X-Metis-Allowance-Used", "X-Metis-Allowance-Limit", "X-Metis-Allowance-Resets");
    }
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "10";
        return ValueTask.CompletedTask;
    };

    // Token bucket rather than a fixed window. Asking Metis three questions in
    // ten seconds and then nothing for five minutes is what using it actually
    // looks like, and a fixed window punishes exactly that shape while letting a
    // steady script through.
    options.AddPolicy("assist", context =>
    {
        var state = context.RequestServices.GetRequiredService<GatewayState>();
        var plan = Entitlements.ParsePlan(context.User.FindFirst(SupabaseTokenHandler.PlanClaim)?.Value);
        var limits = state.Rules.For(plan);

        return RateLimitPartition.GetTokenBucketLimiter(
            context.UserId() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, limits.BurstRequests),
                TokensPerPeriod = Math.Max(1, limits.RequestsPerMinute),
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    // Connecting a provider makes an authenticated outbound call to a third
    // party with input the caller chose. Left open that is a credential testing
    // oracle, so it gets its own much tighter policy.
    options.AddPolicy("connections", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.UserId() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    // Starting a checkout is an authenticated write to a third party on Metis's
    // own account, and somebody genuinely buying something does it once or
    // twice. Loose enough that changing your mind and coming back later is
    // fine; tight enough that a signed-in script cannot fill the processor with
    // abandoned sessions or spend Metis's API allowance there.
    options.AddPolicy("checkout", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.UserId() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    // Not optional on a small instance. A 12 MiB multipart upload holds real
    // buffers, and six of them at once is an out-of-memory kill rather than a
    // slow response.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter(
            "global",
            _ => new ConcurrencyLimiterOptions { PermitLimit = 8, QueueLimit = 8 }));
});

// A hard ceiling that applies before anything has been authenticated, since the
// per-plan cap cannot be known until the caller is. The per-plan cap is enforced
// while the body is being read, so an oversized upload is refused partway rather
// than after Metis has paid to receive all of it.
//
// PORT is honoured here as well, because the platform sometimes chooses it. A
// Render service created from render.yaml routes to 10000, which is the number
// the Dockerfile and the blueprint both pin — but one created from Render's
// dashboard is handed a PORT instead, and a process that ignores it binds to a
// port nothing is routed to. The health check then fails forever while the
// service itself is perfectly healthy, which is a miserable thing to find. An
// endpoint configured in code takes precedence over ASPNETCORE_URLS, so this is
// the last word when PORT is set and changes nothing at all when it is not.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 14 * 1024 * 1024;

    if (int.TryParse(
            Environment.GetEnvironmentVariable("PORT"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var port)
        && port is > 0 and <= 65535)
    {
        kestrel.ListenAnyIP(port);
    }
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var signingKey = EntitlementSigner.TryLoadPrivateKey(config.EntitlementSigningKey);
if (signingKey is null)
{
    app.Logger.LogWarning(
        "METIS_ENTITLEMENT_SIGNING_KEY is not set. /v1/me will return unsigned snapshots, "
        + "so desktop clients will not trust a cached plan while offline.");
}

// Said once at startup rather than discovered by the first person to pay. The
// checkout works without it, so this is a warning and not a refusal — but a
// customer who has just handed over money lands on the processor's own page and
// never comes back to their account, which nobody would think to look for.
if (config.PolarAccessToken is not null && config.SiteUrl is null)
{
    app.Logger.LogWarning(
        "METIS_SITE_URL is not set while checkout is configured. Customers will finish "
        + "on the processor's confirmation page instead of being returned to their account.");
}

// Whether the key this gateway spends money with actually works.
//
// Every AI answer on every Free and Pro account goes through GOOGLE_API_KEY. If
// it is expired, restricted to the wrong project, or out of quota, every single
// turn fails -- and until this commit the only place that was visible was a
// sentence on the user's screen that did not say what had gone wrong. Nothing
// checked at boot, so a broken key looked exactly like a healthy deployment:
// /health answered 200 and the service came up clean.
//
// One cheap request at startup, so the answer is in the deploy log rather than
// in a support conversation. It never blocks startup: a gateway that cannot
// reach Google right now may be perfectly able to ten seconds later, and
// refusing to boot over it would turn a temporary fault into an outage.
_ = Task.Run(async () =>
{
    var key = config.KeyFor("google");
    if (key is null)
    {
        app.Logger.LogWarning(
            "GOOGLE_API_KEY is not set. Every managed AI turn will be refused until it is.");
        return;
    }

    try
    {
        using var probe = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        probe.Timeout = TimeSpan.FromSeconds(20);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
        request.Headers.Add("x-goog-api-key", key);

        using var response = await probe.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            app.Logger.LogInformation("Startup check: GOOGLE_API_KEY is accepted by the provider.");
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var (_, explanation) = ProviderFailures.Describe((int)response.StatusCode);
        app.Logger.LogError(
            "Startup check: GOOGLE_API_KEY was REFUSED with {Status}. Every managed AI turn will "
            + "fail this way until it is fixed. Metis will tell users: \"{Explanation}\". "
            + "Provider said: {Body}",
            (int)response.StatusCode, explanation, ProviderFailures.Truncate(body, 600));
    }
    catch (Exception exception)
    {
        // Could not reach Google at all. Worth saying, but not worth alarming
        // about: this is as likely to be a cold start as a real fault.
        app.Logger.LogWarning(
            exception, "Startup check: could not reach the AI provider to verify GOOGLE_API_KEY.");
    }
});

// Liveness only. Deliberately says nothing about configuration: a health check
// is reachable by anyone, so it must not become a way to learn which providers
// are configured or which environment this is.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ============================ Who is asking ============================

// Hands the desktop app's identity to the browser, once.
//
// Metis and the website hold separate Supabase sessions, so "Manage on Web" --
// a plain link until now -- opened whichever account the browser was already
// signed in as. On a machine where more than one account has been used that is
// routinely the wrong one, and the user sees a different plan and a different
// address than the app just showed them.
//
// The token is minted for the address behind the caller's own verified token
// and for no other, which is the only thing keeping this from being an account
// takeover endpoint.
app.MapPost("/v1/web-session", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayConfig gatewayConfig,
    CancellationToken cancellationToken) =>
{
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    var token = await supabase.CreateWebHandoffTokenAsync(account.UserId, cancellationToken);
    return token is null
        ? Problem(503, "handoff",
            "Metis could not prepare a web sign-in just now. Open the site and sign in there.")
        : Results.Ok(new { token });
})
.RequireAuthorization();

app.MapGet("/v1/me", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayState state,
    GatewayConfig gatewayConfig,
    CancellationToken cancellationToken) =>
{
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    var rules = state.Rules;
    var limits = rules.For(account.Plan);
    var usage = await supabase.LoadUsageAsync(account.UserId, cancellationToken);

    var issued = DateTimeOffset.UtcNow;
    var snapshot = new EntitlementSnapshot(
        account.UserId,
        account.Role,
        account.Plan,
        account.EmailVerified,
        rules.BillingIsLive,
        Entitlements.GrantedFeatures(account, rules.BillingIsLive),
        limits,
        issued,
        issued + EntitlementSigner.Lifetime);

    return Results.Ok(new MeResponse(
        snapshot.UserId,
        snapshot.Role.ToString().ToLowerInvariant(),
        snapshot.Plan.ToString().ToLowerInvariant(),
        gatewayConfig.Environment.ToString().ToLowerInvariant(),
        snapshot.EmailVerified,
        snapshot.BillingIsLive,
        snapshot.Granted.Select(feature => feature.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
        limits,
        new AssistAllowance(
            usage.SpendUsd, limits.MonthlyBudgetUsd, usage.ResetsUtc,
            usage.RequestCount, usage.DictationMinutes, usage.AgentSteps),
        snapshot.IssuedUtc,
        snapshot.ExpiresUtc,
        signingKey is null ? string.Empty : EntitlementSigner.Sign(snapshot, signingKey)));
}).RequireAuthorization();

// ============================== The turn ==============================

app.MapPost("/v1/assist", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayState state,
    GatewayConfig gatewayConfig,
    IHttpClientFactory clients,
    ILoggerFactory loggers,
    CancellationToken cancellationToken) =>
{
    var log = loggers.CreateLogger("Metis.Api.Assist");
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    if (!state.Ready)
    {
        // The gateway has never successfully read its own rules. Serving a
        // request now would mean spending against limits it is guessing at.
        return Problem(503, "degraded", "Metis's AI service is starting up. Try again in a moment.");
    }

    var rules = state.Rules;
    var limits = rules.For(account.Plan);

    if (!Entitlements.Has(account, MetisFeature.ManagedAiRouting, rules.BillingIsLive))
    {
        return Problem(403, "plan",
            Entitlements.Explain(account, MetisFeature.ManagedAiRouting, rules.BillingIsLive));
    }

    AssistRequest? body;
    byte[]? screenshot;
    byte[]? audio;
    try
    {
        (body, screenshot, audio) = await MultipartAssist.ReadAsync(
            context.Request,
            Math.Max(limits.MaxScreenshotBytes, account.IsStaff ? AssistantPromptKernel.MaxInlineScreenshotBytes : 0),
            cancellationToken);
    }
    catch (BadHttpRequestException exception)
    {
        return Problem(400, "request", exception.Message);
    }

    if (body is null || string.IsNullOrWhiteSpace(body.Prompt))
    {
        return Problem(400, "request", "A prompt is required.");
    }

    // The client supplies a request id so one turn can be followed from the
    // desktop log through here into usage_events. It lands in a uuid column, so
    // anything that is not a GUID is replaced rather than trusted.
    var requestId = Guid.TryParse(body.RequestId, out var supplied)
        ? supplied.ToString("d")
        : Guid.NewGuid().ToString("d");

    var isAgentStep = string.Equals(body.Feature, "agent_step", StringComparison.Ordinal);
    var usage = await supabase.LoadUsageAsync(account.UserId, cancellationToken);
    var decision = ManagedAccess.Decide(
        account, limits, usage, rules,
        requestHasScreenshot: screenshot is { Length: > 0 },
        isAgentStep,
        screenshot?.Length ?? 0);

    if (!decision.Allowed)
    {
        return Problem(decision.StatusCode, decision.Kind!, decision.Message!);
    }

    // Under degrade the screenshot is dropped rather than the request refused:
    // a text answer is worth much more than an error, and the image is where
    // nearly all of the cost is.
    if (!account.IsStaff && rules.Protection == CostProtection.Degrade)
    {
        screenshot = null;
    }

    var model = ManagedAccess.ChooseModel(body.Model, limits, rules, account.IsStaff);
    var provider = "google";
    var apiKey = gatewayConfig.KeyFor(provider);
    if (apiKey is null)
    {
        return Problem(503, "degraded", "Metis's AI service is not configured to answer right now.");
    }

    // The gateway builds the system instruction and the schema itself and
    // ignores anything the client might have wanted to say about them. A
    // client-supplied system prompt running on Metis's key would be free
    // general-purpose inference for whoever pointed a script at this endpoint,
    // and the output ceiling is the only thing bounding what a turn can cost.
    // The plan's memory allowance, applied where it can actually be applied.
    //
    // Memory itself lives in a JSON file on the user's own machine and no
    // server can police how many entries are in it. What reaches the model is a
    // different matter: recall and turn history travel as text on every managed
    // request, they are the part Metis pays for, and they are the part that
    // makes memory do anything. So the plan bounds those, here, on requests
    // Metis is buying — and never on a local model or a Pro account's own key,
    // neither of which comes through this endpoint at all.
    //
    // Trimmed rather than refused: a long-standing user should get a slightly
    // less well-informed answer, not an error that arrives because they have
    // been using the product.
    var (trimmedRecall, trimmedTurns) =
        PromptContextLimits.Apply(body.ChatRecall, body.RecentTurns, limits);

    var geminiRequest = (body with
    {
        ChatRecall = trimmedRecall,
        RecentTurns = trimmedTurns
    }).ToGeminiRequest(screenshot, audio);

    var systemInstruction = AssistantPromptKernel.BuildSystemInstruction(geminiRequest);
    var userPrompt = AssistantPromptKernel.BuildUserPrompt(geminiRequest);

    var stopwatch = Stopwatch.StartNew();
    var status = "ok";
    var inputTokens = 0;
    var thoughtTokens = 0;
    var outputTokens = 0;

    using var scope = log.BeginScope(new Dictionary<string, object>
    {
        ["requestId"] = requestId,
        ["userId"] = account.UserId,
        ["plan"] = account.Plan.ToString(),
        ["provider"] = provider,
        ["model"] = model,
        ["feature"] = body.Feature
    });

    try
    {
        var payload = GeminiPayload.Build(systemInstruction, userPrompt, screenshot, body.ScreenshotMimeType);
        var http = clients.CreateClient("providers");

        if (body.Stream)
        {
            await StreamTurnAsync(
                context, http, apiKey, model, payload, requestId,
                new AssistAllowance(
                    usage.SpendUsd, limits.MonthlyBudgetUsd, usage.ResetsUtc,
                    usage.RequestCount + 1, usage.DictationMinutes, usage.AgentSteps),
                report => (inputTokens, thoughtTokens, outputTokens) = report,
                reason => status = reason,
                cancellationToken);
            return Results.Empty;
        }

        var (text, report, failure) = await CompleteTurnAsync(
            http, apiKey, model, payload, cancellationToken);

        if (failure is not null)
        {
            var (failureStatus, providerBody) = ProviderFailures.Split(failure);
            status = failureStatus;

            // Logged here, and only here. The provider's own error text can name
            // the Google project the key belongs to and sometimes the key's own
            // prefix, so it goes to the gateway's log where an operator can read
            // it -- never to the caller, who gets the classification instead.
            log.LogError(
                "Upstream provider refused a turn for model {Model}: {Status}. Provider said: {Body}",
                model, failureStatus, providerBody);

            var (kind, message) = ProviderFailures.Describe(ProviderFailures.StatusCode(failureStatus));
            return Problem(502, kind, message);
        }

        (inputTokens, thoughtTokens, outputTokens) = report;

        var spent = usage.SpendUsd
            + state.Prices.Estimate(provider, model, inputTokens, outputTokens).Cost;

        WriteAllowanceHeaders(context, spent, limits.MonthlyBudgetUsd, usage.ResetsUtc);

        return Results.Ok(new AssistResponse(
            requestId, provider, model, text ?? string.Empty,
            new AssistUsage(inputTokens, thoughtTokens, outputTokens),
            new AssistAllowance(
                spent, limits.MonthlyBudgetUsd, usage.ResetsUtc,
                usage.RequestCount + 1, usage.DictationMinutes, usage.AgentSteps)));
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
        status = "error";
        log.LogError(exception, "The gateway could not complete a turn.");
        return Problem(502, "network", "Metis could not reach its AI provider.");
    }
    finally
    {
        stopwatch.Stop();

        var (cost, estimated) = state.Prices.Estimate(provider, model, inputTokens, outputTokens);
        await supabase.RecordUsageAsync(
            account.UserId,
            requestId,
            provider,
            model,
            body?.Feature ?? "chat",
            inputTokens,
            outputTokens,
            cost,
            stopwatch.ElapsedMilliseconds,
            estimated && status == "ok" ? "ok_priced_by_fallback" : status,
            CancellationToken.None);
    }
})
.RequireAuthorization()
.RequireRateLimiting("assist");

// =========================== The agent's turn ===========================

// An agent step is not a conversation turn, and it gets its own route because
// making it borrow /v1/assist was worse than either alternative. That route
// writes Metis's own system instruction and reads the reply as an assistant
// plan; an agent asking which tool to call next needs neither, and every one of
// its turns would have been misparsed into a plan and lost.
//
// It is also the only place the agent-step allowance can ever be charged.
// Background agents ran exclusively on the user's own key until this existed,
// so plan_limits.max_agent_steps_per_month and the isAgentStep branch in
// ManagedAccess.Decide were columns and code nothing reached.
app.MapPost("/v1/agent-step", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayState state,
    GatewayConfig gatewayConfig,
    IHttpClientFactory clients,
    ILoggerFactory loggers,
    AgentStepRequest request,
    CancellationToken cancellationToken) =>
{
    var log = loggers.CreateLogger("Metis.Api.AgentStep");
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    if (!state.Ready)
    {
        return Problem(503, "degraded", "Metis's AI service is starting up. Try again in a moment.");
    }

    var rules = state.Rules;
    var limits = rules.For(account.Plan);

    // Refused here rather than by the step allowance, and the difference
    // matters to the user: a plan that never included agents is a 403 with a
    // sentence about what Pro includes, not a 402 that reads as "come back next
    // month".
    if (!Entitlements.Has(account, MetisFeature.AutonomousAgents, rules.BillingIsLive))
    {
        return Problem(403, "plan",
            Entitlements.Explain(account, MetisFeature.AutonomousAgents, rules.BillingIsLive));
    }

    var messages = request?.Messages?
        .Where(message => !string.IsNullOrWhiteSpace(message?.Content))
        .ToArray() ?? [];

    if (messages.Length == 0)
    {
        return Problem(400, "request", "An agent step needs at least one message.");
    }

    // The only bound on what one step can cost on the input side. Unlike
    // /v1/assist, this route forwards the caller's own prompt and history —
    // that is what an agent step is — so the output ceiling alone does not say
    // what a turn is worth, and a runaway agent accumulating history forever
    // would otherwise buy an ever more expensive turn each time round its loop.
    var characters = (request!.System?.Length ?? 0) + messages.Sum(message => message.Content!.Length);
    if (characters > AgentStepRequest.MaxCharacters)
    {
        return Problem(400, "request",
            "That agent step carries more history than Metis's AI will run. Start a smaller task.");
    }

    // Generated here rather than accepted from the client, unlike /v1/assist.
    // An agent turn has no user-visible request to correlate it with on the
    // desktop side, so there is nothing for a client-supplied id to join to.
    var requestId = Guid.NewGuid().ToString("d");

    var usage = await supabase.LoadUsageAsync(account.UserId, cancellationToken);
    var decision = ManagedAccess.Decide(
        account, limits, usage, rules,
        requestHasScreenshot: false,
        isAgentStep: true,
        screenshotBytes: 0);

    if (!decision.Allowed)
    {
        return Problem(decision.StatusCode, decision.Kind!, decision.Message!);
    }

    var model = ManagedAccess.ChooseModel(request.Model, limits, rules, account.IsStaff);
    var provider = "google";
    var apiKey = gatewayConfig.KeyFor(provider);
    if (apiKey is null)
    {
        return Problem(503, "degraded", "Metis's AI service is not configured to answer right now.");
    }

    var stopwatch = Stopwatch.StartNew();
    var status = "ok";
    var inputTokens = 0;
    var thoughtTokens = 0;
    var outputTokens = 0;

    using var scope = log.BeginScope(new Dictionary<string, object>
    {
        ["requestId"] = requestId,
        ["userId"] = account.UserId,
        ["plan"] = account.Plan.ToString(),
        ["provider"] = provider,
        ["model"] = model,
        ["feature"] = "agent_step"
    });

    try
    {
        var payload = GeminiPayload.BuildAgentTurn(
            request.System, messages, request.Temperature, request.ResponseFormat);
        var http = clients.CreateClient("providers");

        // Not streamed. The agent shows nothing while a step is being decided,
        // so a stream would buy latency nobody can see and cost a second frame
        // reader to maintain.
        var (text, report, failure) = await CompleteTurnAsync(
            http, apiKey, model, payload, cancellationToken);

        if (failure is not null)
        {
            status = failure;
            return Problem(502, "provider", "The AI provider refused the request.");
        }

        (inputTokens, thoughtTokens, outputTokens) = report;

        var spent = usage.SpendUsd
            + state.Prices.Estimate(provider, model, inputTokens, outputTokens).Cost;

        WriteAllowanceHeaders(context, spent, limits.MonthlyBudgetUsd, usage.ResetsUtc);

        // The reply goes back unread. An agent's decision is its own free-form
        // JSON, not an assistant plan, and the client owns the parser that
        // knows the difference — putting a second one here would misread every
        // turn and would drift from the good one the moment either changed.
        return Results.Ok(new AssistResponse(
            requestId, provider, model, text ?? string.Empty,
            new AssistUsage(inputTokens, thoughtTokens, outputTokens),
            new AssistAllowance(
                spent, limits.MonthlyBudgetUsd, usage.ResetsUtc,
                usage.RequestCount, usage.DictationMinutes, usage.AgentSteps + 1)));
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
        status = "error";
        log.LogError(exception, "The gateway could not complete an agent step.");
        return Problem(502, "network", "Metis could not reach its AI provider.");
    }
    finally
    {
        stopwatch.Stop();

        // Recorded as agent_step whatever happened, including the failures. The
        // step allowance is read back from these rows, so a turn that is paid
        // for and lost still has to count against it.
        var (cost, estimated) = state.Prices.Estimate(provider, model, inputTokens, outputTokens);
        await supabase.RecordUsageAsync(
            account.UserId,
            requestId,
            provider,
            model,
            "agent_step",
            inputTokens,
            outputTokens,
            cost,
            stopwatch.ElapsedMilliseconds,
            estimated && status == "ok" ? "ok_priced_by_fallback" : status,
            CancellationToken.None);
    }
})
.RequireAuthorization()
.RequireRateLimiting("assist");

// ======================= Bring your own provider =======================

app.MapGet("/v1/connections", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayConfig gatewayConfig,
    CancellationToken cancellationToken) =>
{
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    var rows = await supabase.LoadConnectionsAsync(account.UserId, cancellationToken);
    return Results.Ok(new { connections = rows ?? default });
}).RequireAuthorization();

app.MapPost("/v1/connections", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayState state,
    GatewayConfig gatewayConfig,
    ProviderKeyValidator validator,
    ConnectRequest request,
    CancellationToken cancellationToken) =>
{
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    var rules = state.Rules;
    if (!Entitlements.Has(account, MetisFeature.CustomAiProvider, rules.BillingIsLive))
    {
        return Problem(403, "plan",
            Entitlements.Explain(account, MetisFeature.CustomAiProvider, rules.BillingIsLive));
    }

    var provider = request?.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
    var apiKey = request?.ApiKey?.Trim() ?? string.Empty;

    if (provider.Length == 0 || apiKey.Length == 0)
    {
        return Problem(400, "request", "A provider and an API key are required.");
    }

    var allowed = await supabase.LoadByoProvidersAsync(cancellationToken);
    if (!allowed.Contains(provider, StringComparer.OrdinalIgnoreCase))
    {
        return Problem(400, "request", "Metis cannot connect that provider yet.");
    }

    var (ok, reason) = await validator.ValidateAsync(provider, apiKey, cancellationToken);
    if (!ok)
    {
        return Problem(400, "credential", reason ?? "That key could not be verified.");
    }

    var hint = ProviderKeyValidator.Hint(apiKey);
    if (!await supabase.StoreProviderSecretAsync(
            account.UserId, provider, apiKey, hint, request?.Model, cancellationToken))
    {
        return Problem(500, "storage", "Metis could not store that connection. Nothing was saved.");
    }

    // The hint, never the key. An audit trail that records secrets is a second
    // place secrets live.
    await supabase.RecordAuditAsync(
        account.UserId,
        "connection.created",
        new { provider, key_hint = hint },
        cancellationToken);

    return Results.Ok(new
    {
        provider,
        model = request?.Model,
        keyHint = hint,
        lastTestedAt = DateTimeOffset.UtcNow
    });
})
.RequireAuthorization()
.RequireRateLimiting("connections");

app.MapDelete("/v1/connections/{provider}", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayConfig gatewayConfig,
    string provider,
    CancellationToken cancellationToken) =>
{
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    var removed = await supabase.ForgetProviderSecretAsync(
        account.UserId, provider.Trim().ToLowerInvariant(), cancellationToken);

    // Disconnecting something that was not connected is not an error worth
    // telling anyone about: the state they wanted is the state they have.
    return Results.Ok(new { provider, removed });
}).RequireAuthorization();

// ============================== Billing ==============================

// Starting a checkout, which is the half of billing that had no server side at
// all: the webhook could apply a subscription, and nothing existed that could
// create one.
//
// It is on the gateway rather than in the website's own JavaScript for two
// reasons that are really the same reason. The processor's API token would have
// to ship inside the bundle, where anyone could read it and create sessions
// against Metis's account. And the Supabase user id the subscription is bound to
// would be a value the browser chose — which is to say, a value anybody could
// choose, and therefore a way to move someone else's plan. Here the id comes off
// a token Supabase verified, and there is nothing in the request body that can
// change whose account is being bought for.
app.MapPost("/v1/checkout", async (
    HttpContext context,
    SupabaseGateway supabase,
    GatewayConfig gatewayConfig,
    IHttpClientFactory clients,
    ILoggerFactory loggers,
    CheckoutRequest request,
    CancellationToken cancellationToken) =>
{
    var log = loggers.CreateLogger("Metis.Api.Checkout");
    var account = context.Account(gatewayConfig.Environment);
    if (account is null)
    {
        return NoAccountRow();
    }

    // Deliberately no entitlement check. Everything else on this service asks
    // what the account has bought before it does anything; this is the endpoint
    // people use to buy, and gating it behind a plan would be circular. An
    // unverified address is no obstacle either — the subscription binds to the
    // user id, not to the address, so there is nothing here to take over.
    var decision = Checkout.Decide(gatewayConfig, account.Plan, request?.Plan);
    if (!decision.Allowed)
    {
        return Problem(decision.StatusCode, decision.Kind!, decision.Message!);
    }

    var email = await supabase.LoadUserEmailAsync(account.UserId, cancellationToken);
    var payload = Checkout.BuildSessionRequest(
        decision.ProductId!,
        account.UserId,
        decision.Plan,
        email,
        Checkout.SuccessUrl(gatewayConfig));

    try
    {
        var http = clients.CreateClient("billing");
        using var upstream = new HttpRequestMessage(
            HttpMethod.Post, $"{gatewayConfig.PolarApiBase}/v1/checkouts/")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        upstream.Headers.Add("Authorization", $"Bearer {gatewayConfig.PolarAccessToken}");

        using var response = await http.SendAsync(upstream, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The status and nothing else. A processor's error text names the
            // organisation and the product, and echoes back parts of what was
            // sent — including the customer's own address. It belongs in the
            // processor's dashboard rather than in Metis's logs.
            log.LogError(
                "The payment processor refused a checkout with {Status}.", (int)response.StatusCode);
            return Problem(502, "billing",
                "Metis could not start a checkout just now. Nothing has been charged.");
        }

        var url = Checkout.ReadCheckoutUrl(body);
        if (url is null)
        {
            log.LogError("The payment processor accepted a checkout and returned no URL.");
            return Problem(502, "billing",
                "Metis could not start a checkout just now. Nothing has been charged.");
        }

        // The plan and the processor, never the URL. A checkout link is a
        // bearer link to a payment form opened in this person's name.
        await supabase.RecordAuditAsync(
            account.UserId,
            "checkout.started",
            new { plan = decision.Plan.ToString().ToLowerInvariant(), provider = "polar" },
            cancellationToken);

        return Results.Ok(new { url });
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
        log.LogError(exception, "The gateway could not reach the payment processor.");
        return Problem(502, "network",
            "Metis could not reach its payment processor. Nothing has been charged.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("checkout");

app.MapPost("/v1/webhooks/{provider}", async (
    HttpContext context,
    SupabaseGateway supabase,
    IEnumerable<IBillingWebhookVerifier> verifiers,
    ILoggerFactory loggers,
    string provider,
    CancellationToken cancellationToken) =>
{
    var log = loggers.CreateLogger("Metis.Api.Webhooks");
    var verifier = verifiers.FirstOrDefault(candidate =>
        string.Equals(candidate.Provider, provider, StringComparison.OrdinalIgnoreCase));

    // 404 rather than 501 for an unconfigured processor. A 501 would tell anyone
    // probing which processors this deployment knows about and which are live.
    if (verifier is null || !verifier.IsConfigured)
    {
        return Results.NotFound();
    }

    // The raw bytes, before any JSON binding. The signature is over exactly what
    // was sent; round-tripping through a deserialiser changes whitespace and key
    // order enough to break it, and that is the classic webhook bug.
    using var buffer = new MemoryStream();
    await context.Request.Body.CopyToAsync(buffer, cancellationToken);
    var raw = buffer.ToArray();

    if (!verifier.TryVerify(raw, context.Request.Headers, DateTimeOffset.UtcNow, out var reason))
    {
        log.LogWarning("Rejected a {Provider} webhook: {Reason}", provider, reason);
        return Results.Unauthorized();
    }

    BillingEvent? billingEvent;
    try
    {
        billingEvent = verifier.Parse(raw);
    }
    catch (JsonException)
    {
        billingEvent = null;
    }

    if (billingEvent is null)
    {
        // Verified but unreadable. Returning 200 stops the processor retrying a
        // payload this build will never understand.
        log.LogWarning("A verified {Provider} webhook could not be read.", provider);
        return Results.Ok();
    }

    var claimed = await supabase.TryClaimBillingEventAsync(
        billingEvent.Provider,
        billingEvent.EventId,
        billingEvent.EventType,
        Encoding.UTF8.GetString(raw),
        cancellationToken);

    if (!claimed)
    {
        // Already applied. Redelivery is normal, not an error.
        return Results.Ok();
    }

    if (!billingEvent.ChangesEntitlement)
    {
        await supabase.FinishBillingEventAsync(
            billingEvent.Provider, billingEvent.EventId, error: null, cancellationToken);
        return Results.Ok();
    }

    try
    {
        var applied = await supabase.ApplySubscriptionAsync(
            billingEvent.Provider,
            billingEvent.ExternalSubscriptionId!,
            billingEvent.MetisUserId!,
            billingEvent.Plan,
            billingEvent.Status,
            billingEvent.CurrentPeriodEnd,
            billingEvent.CancelAtPeriodEnd,
            billingEvent.ExternalCustomerId,
            cancellationToken);

        await supabase.FinishBillingEventAsync(
            billingEvent.Provider,
            billingEvent.EventId,
            applied is null ? "apply_subscription returned nothing" : null,
            cancellationToken);
    }
    catch (Exception exception)
    {
        // Still 200. A processor retries anything that is not 2xx, and a poison
        // event would be redelivered until someone noticed. The row keeps its
        // reason and stays unprocessed so it can be drained deliberately.
        log.LogError(exception, "Could not apply {Provider} event {EventId}.", provider, billingEvent.EventId);
        await supabase.FinishBillingEventAsync(
            billingEvent.Provider, billingEvent.EventId, exception.Message, cancellationToken);
    }

    return Results.Ok();
});

app.Run();

// ============================== Helpers ==============================

/// <summary>
/// Authenticated, but there is no account row for this user.
///
/// Told apart from 401 on purpose: it means the auth user exists and the trigger
/// that seeds their account row did not, which is a fault on Metis's side and
/// reads completely differently in a log than a bad token does.
/// </summary>
static IResult NoAccountRow() =>
    Results.Problem("No account record exists for this user.", statusCode: 409);

/// <summary>
/// A refusal the client can classify without parsing prose. <c>kind</c> is what
/// the desktop app maps to an error kind; <c>message</c> is what it shows.
/// </summary>
static IResult Problem(int status, string kind, string message) =>
    Results.Json(new { error = message, kind }, statusCode: status);

static void WriteAllowanceHeaders(HttpContext context, decimal used, decimal limit, DateTimeOffset resets)
{
    context.Response.Headers["X-Metis-Allowance-Used"] = used.ToString(CultureInfo.InvariantCulture);
    context.Response.Headers["X-Metis-Allowance-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
    context.Response.Headers["X-Metis-Allowance-Resets"] = resets.ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>
/// Streams a turn as server-sent events, in Metis's own frame shape rather than
/// the provider's.
///
/// Streaming is not a nicety here. The desktop app shows the reply while it is
/// still arriving, and its whole fallback design turns on whether any text has
/// reached the screen yet. A non-streaming managed path would make every turn on
/// Free and Pro feel seconds slower than the bring-your-own-key path it
/// replaces — a visible regression on the day people are moved onto it.
/// </summary>
static async Task StreamTurnAsync(
    HttpContext context,
    HttpClient http,
    string apiKey,
    string model,
    string payload,
    string requestId,
    AssistAllowance allowance,
    Action<(int Input, int Thought, int Output)> onUsage,
    Action<string> onStatus,
    CancellationToken cancellationToken)
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    // Asks intermediaries not to buffer. Whether Render's proxy honours it has
    // to be confirmed on the first deploy; if it does not, the client falls back
    // to a non-streaming request, which it already tolerates because the delta
    // callback is optional by contract.
    context.Response.Headers["X-Accel-Buffering"] = "no";

    using var upstream = new HttpRequestMessage(
        HttpMethod.Post,
        $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse")
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };
    upstream.Headers.Add("x-goog-api-key", apiKey);

    using var response = await http.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        var upstreamStatus = (int)response.StatusCode;
        onStatus($"provider_{upstreamStatus}");

        var (kind, message) = ProviderFailures.Describe(upstreamStatus);
        await WriteFrameAsync(context, new AssistStreamFrame(
            "error", Kind: kind, Message: message), cancellationToken);
        await WriteFrameAsync(context, new AssistStreamFrame("done"), cancellationToken);
        return;
    }

    var input = 0;
    var thought = 0;
    var output = 0;

    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var reader = new StreamReader(stream, Encoding.UTF8);

    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
        {
            continue;
        }

        var json = line[5..].Trim();
        if (json.Length == 0 || json == "[DONE]")
        {
            continue;
        }

        try
        {
            using var chunk = JsonDocument.Parse(json);
            var root = chunk.RootElement;

            if (root.TryGetProperty("usageMetadata", out var meta))
            {
                input = ReadInt(meta, "promptTokenCount", input);
                thought = ReadInt(meta, "thoughtsTokenCount", thought);
                output = ReadInt(meta, "candidatesTokenCount", output);
            }

            var text = ReadCandidateText(root);
            if (!string.IsNullOrEmpty(text))
            {
                await WriteFrameAsync(context, new AssistStreamFrame("delta", text), cancellationToken);
            }
        }
        catch (JsonException)
        {
            // A malformed frame is skipped rather than ending the turn. The
            // client's reader does the same, so the two halves agree about what
            // an unreadable chunk means.
        }
    }

    onUsage((input, thought, output));
    await WriteFrameAsync(
        context,
        new AssistStreamFrame("usage", Usage: new AssistUsage(input, thought, output), Allowance: allowance),
        cancellationToken);
    await WriteFrameAsync(context, new AssistStreamFrame("done"), cancellationToken);
}

static async Task WriteFrameAsync(HttpContext context, AssistStreamFrame frame, CancellationToken cancellationToken)
{
    await context.Response.WriteAsync(
        $"data: {JsonSerializer.Serialize(frame)}\n\n", Encoding.UTF8, cancellationToken);

    // Flushed per frame. Without this the whole point of streaming is lost to
    // the response buffer.
    await context.Response.Body.FlushAsync(cancellationToken);
}

static async Task<(string? Text, (int Input, int Thought, int Output) Usage, string? Failure)> CompleteTurnAsync(
    HttpClient http,
    string apiKey,
    string model,
    string payload,
    CancellationToken cancellationToken)
{
    using var upstream = new HttpRequestMessage(
        HttpMethod.Post,
        $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    // The key goes in a header rather than the query string, so it cannot end up
    // in a proxy log or an error page along with the URL.
    upstream.Headers.Add("x-goog-api-key", apiKey);

    using var response = await http.SendAsync(upstream, cancellationToken);
    var raw = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        // The status travels in the failure string so the caller can classify
        // it, and the body travels with it so the caller can log the reason.
        // Both used to be discarded here, which is why the one sentence that
        // reached the user could not say anything.
        return (null, (0, 0, 0), $"provider_{(int)response.StatusCode}|{ProviderFailures.Truncate(raw, 600)}");
    }

    using var document = JsonDocument.Parse(raw);
    var root = document.RootElement;

    var usage = root.TryGetProperty("usageMetadata", out var meta)
        ? (ReadInt(meta, "promptTokenCount", 0), ReadInt(meta, "thoughtsTokenCount", 0), ReadInt(meta, "candidatesTokenCount", 0))
        : (0, 0, 0);

    return (ReadCandidateText(root), usage, null);
}

static int ReadInt(JsonElement element, string name, int fallback) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        ? value.GetInt32()
        : fallback;

static string? ReadCandidateText(JsonElement root)
{
    if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
    {
        return null;
    }

    var builder = new StringBuilder();
    foreach (var candidate in candidates.EnumerateArray())
    {
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            continue;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }
    }

    return builder.Length == 0 ? null : builder.ToString();
}

/// <summary>What connecting a provider looks like on the wire.</summary>
public sealed record ConnectRequest(string? Provider, string? ApiKey, string? Model);

/// <summary>
/// What starting a checkout looks like on the wire: which plan, and nothing
/// else.
///
/// The emptiness is the design. An account id, a price, a product, a return
/// address — each is a field a client might reasonably have sent, and each would
/// be a way to charge the wrong person, charge the wrong amount, or point
/// Metis's own domain at a stranger's page. Every one of them is decided on this
/// side instead: from the verified token, or from configuration.
/// </summary>
public sealed record CheckoutRequest(string? Plan);

/// <summary>
/// One turn of a background agent, on the wire.
///
/// It is deliberately not <see cref="AssistRequest"/>. That record mirrors
/// GeminiRequest — a screen capture, a pointer, a traced region, an operating
/// mode — and an agent step has none of those; it has a prompt the agent wrote
/// and the exchange so far. Sharing the record would have meant a wire format
/// where most fields are meaningless on half the routes that use it.
///
/// This one lives here rather than in Metis.Core because the client sends the
/// shape and never receives it: the reply comes back as an
/// <see cref="AssistResponse"/>, which is shared.
/// </summary>
public sealed record AgentStepRequest(
    string? System,
    IReadOnlyList<AgentStepMessage>? Messages,
    string? Model,
    double? Temperature,
    string? ResponseFormat)
{
    /// <summary>
    /// How much prompt and history one step may carry. Roughly a hundred
    /// thousand tokens of input, which is far more than a well-behaved agent
    /// sends and far less than an unbounded loop would.
    /// </summary>
    public const int MaxCharacters = 400_000;
}

/// <summary>One message in an agent's exchange. <c>role</c> is "user" or "assistant".</summary>
public sealed record AgentStepMessage(string? Role, string? Content);

/// <summary>
/// Builds the upstream request body. Kept separate so the assist endpoint reads
/// as a sequence of decisions rather than as JSON assembly.
/// </summary>
public static class GeminiPayload
{
    public static string Build(string systemInstruction, string userPrompt, byte[]? screenshot, string? mimeType)
    {
        var parts = new List<object> { new { text = userPrompt } };
        if (screenshot is { Length: > 0 })
        {
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = AssistantPromptKernel.NormalizeImageMimeType(mimeType),
                    data = Convert.ToBase64String(screenshot)
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[] { new { role = "user", parts } },
            generationConfig = new
            {
                temperature = 0.1,

                // The ceiling that bounds what a turn can cost. It is the same
                // number every provider in the desktop app uses, so a managed
                // answer is not quietly shorter than a bring-your-own-key one.
                maxOutputTokens = AssistantPromptKernel.MaxPlanTokens,
                responseMimeType = "application/json"
            }
        });
    }

    /// <summary>
    /// The same thing for an agent step: the agent's own instruction, its own
    /// exchange, and no schema.
    ///
    /// The system prompt is the caller's here, which is the one thing
    /// <see cref="Build"/> refuses to allow. It has to be — the agent's tool
    /// declarations and its output contract live in that prompt, and a step
    /// answered against Metis's assistant instruction would be a plan for a
    /// user who is not there. What keeps it from being free general-purpose
    /// inference is the pair of limits around it: the plan must include
    /// autonomous agents at all, and every call spends one of a counted monthly
    /// allowance whatever it asked for.
    /// </summary>
    public static string BuildAgentTurn(
        string? systemPrompt,
        IReadOnlyList<AgentStepMessage> messages,
        double? temperature,
        string? responseFormat)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var contents = messages.Select(message => new
        {
            // Gemini calls the model's own turns "model" where every other
            // provider calls them "assistant". The client speaks the common
            // dialect and the translation happens here, once.
            role = message.Role is not null
                   && (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                       || message.Role.Equals("model", StringComparison.OrdinalIgnoreCase))
                ? "model"
                : "user",
            parts = new[] { new { text = message.Content } }
        }).ToArray();

        var payload = new Dictionary<string, object>
        {
            ["contents"] = contents,
            ["generationConfig"] = new
            {
                // Clamped rather than trusted. Temperature is the caller's to
                // choose, but it arrives from a desktop settings file that a
                // user can edit, and a value outside the range is a 400 from
                // the provider that the agent would read as a broken step.
                temperature = Math.Clamp(temperature ?? 0.2, 0d, 2d),
                maxOutputTokens = AssistantPromptKernel.MaxPlanTokens,

                // JSON unless the caller says otherwise, because an agent's
                // decision is always JSON. Prose is accepted so the endpoint is
                // not useless to whatever the agent loop grows into next.
                responseMimeType = string.Equals(responseFormat, "text", StringComparison.OrdinalIgnoreCase)
                    ? "text/plain"
                    : "application/json"
            }
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            payload["systemInstruction"] = new { parts = new[] { new { text = systemPrompt } } };
        }

        return JsonSerializer.Serialize(payload);
    }
}

/// <summary>
/// Reads the multipart body: a JSON <c>request</c> part, an optional
/// <c>screenshot</c>, an optional <c>audio</c>.
///
/// Multipart rather than base64 inside JSON, for three reasons in order of
/// weight. Thirteen mebibytes of PNG becomes about seventeen of base64, which
/// lands in a C# string as roughly thirty-four megabytes before it has even been
/// decoded — a handful of concurrent requests from an out-of-memory kill on a
/// small instance. Multipart also lets the per-plan cap be enforced *while* the
/// bytes arrive, so an oversized upload is cut off rather than paid for in full
/// and then refused. And the provider adapter needs base64 exactly once, at the
/// point it builds its own payload, rather than throughout.
/// </summary>
public static class MultipartAssist
{
    public static async Task<(AssistRequest? Request, byte[]? Screenshot, byte[]? Audio)> ReadAsync(
        HttpRequest request,
        int maxScreenshotBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A plain JSON body is still accepted, for text-only turns and for
        // anyone testing the endpoint with curl. There is nothing to stream in
        // that case, so none of the above applies.
        if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var plain = await request.ReadFromJsonAsync<AssistRequest>(cancellationToken);
            return (plain, null, null);
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.HasValue
            || !contentType.MediaType.Value!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException("Send multipart/form-data or application/json.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
        {
            throw new BadHttpRequestException("The multipart boundary was missing.");
        }

        AssistRequest? body = null;
        byte[]? screenshot = null;
        byte[]? audio = null;

        var reader = new MultipartReader(boundary, request.Body);
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                continue;
            }

            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            switch (name)
            {
                case "request":
                    body = await JsonSerializer.DeserializeAsync<AssistRequest>(
                        section.Body, cancellationToken: cancellationToken);
                    break;

                case "screenshot":
                    screenshot = await ReadCappedAsync(section.Body, maxScreenshotBytes, cancellationToken);
                    break;

                case "audio":
                    // Voice is small next to an image, and it is capped at the
                    // same absolute ceiling rather than at the plan's screenshot
                    // allowance, which has nothing to do with it.
                    audio = await ReadCappedAsync(section.Body, 8 * 1024 * 1024, cancellationToken);
                    break;
            }
        }

        return (body, screenshot, audio);
    }

    private static async Task<byte[]> ReadCappedAsync(Stream stream, int cap, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > cap)
            {
                // Refused partway through rather than after the whole thing has
                // been received. This is the point of reading it as a stream.
                throw new BadHttpRequestException(
                    "That screen capture is larger than this plan allows.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
