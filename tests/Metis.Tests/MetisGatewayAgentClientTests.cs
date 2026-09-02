using System.Net;
using System.Text;
using System.Text.Json;
using Metis.AI.Agents;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// What the desktop app does with the gateway's refusals when an agent runs on
/// Metis's own AI.
///
/// The mapping matters more here than on an ordinary turn. An agent runs
/// unattended, so nobody is watching the moment it fails; whatever sentence
/// this produces is what the user reads later, out of context, next to a task
/// that stopped. "403" and "402" mean opposite things to that person — one says
/// buy something, the other says wait until the 1st — and the two are one line
/// apart in a switch.
/// </summary>
public sealed class MetisGatewayAgentClientTests
{
    /// <summary>
    /// A stand-in for the gateway. Answers once, with whatever it was handed.
    /// </summary>
    private sealed class Canned(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static readonly AgentGatewayMessage[] OneMessage =
        [new("user", "Rename the file.")];

    private static async Task<ReasoningProviderException> Refusal(
        HttpStatusCode status, string body = "{}")
    {
        using var handler = new Canned(status, body);
        using var http = new HttpClient(handler);
        using var client = new MetisGatewayAgentClient(
            new Uri("https://gateway.example/"), http);

        return await Assert.ThrowsAsync<ReasoningProviderException>(
            () => client.CompleteStepAsync(
                "token", "system", OneMessage, "gemini-2.5-flash", 0.1));
    }

    // ============================ the two refusals ============================

    /// <summary>
    /// No agents on this plan. This one does not fix itself with time, so it
    /// must not be reported as a quota — a user told to wait for a reset that
    /// never comes will wait indefinitely.
    /// </summary>
    [Fact]
    public async Task A_403_is_a_plan_limit_and_carries_the_gateway_sentence()
    {
        var error = await Refusal(
            HttpStatusCode.Forbidden,
            """{"error":"Background agents are part of Metis Plus.","kind":"plan"}""");

        Assert.Equal(ReasoningProviderErrorKind.PlanLimited, error.Kind);
        Assert.Equal("Background agents are part of Metis Plus.", error.Message);
    }

    /// <summary>
    /// Out of steps for the month. This one does fix itself, and the sentence
    /// says when.
    /// </summary>
    [Fact]
    public async Task A_402_is_a_quota_and_carries_the_gateway_sentence()
    {
        var error = await Refusal(
            HttpStatusCode.PaymentRequired,
            """{"error":"You have used this month's included agent steps. They reset on the 1st.","kind":"quota"}""");

        Assert.Equal(ReasoningProviderErrorKind.QuotaOrRateLimit, error.Kind);
        Assert.Contains("reset on the 1st", error.Message);
    }

    /// <summary>
    /// The two are never conflated, whatever else changes about the mapping.
    /// </summary>
    [Fact]
    public async Task Not_having_agents_and_having_run_out_are_different_kinds()
    {
        var plan = await Refusal(HttpStatusCode.Forbidden);
        var quota = await Refusal(HttpStatusCode.PaymentRequired);

        Assert.NotEqual(plan.Kind, quota.Kind);
    }

    // ============================== the rest ==============================

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ReasoningProviderErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Conflict, ReasoningProviderErrorKind.Authentication)]
    [InlineData(HttpStatusCode.BadRequest, ReasoningProviderErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.TooManyRequests, ReasoningProviderErrorKind.QuotaOrRateLimit)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ReasoningProviderErrorKind.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ReasoningProviderErrorKind.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway, ReasoningProviderErrorKind.ServiceUnavailable)]
    public async Task Each_status_maps_to_the_kind_the_user_needs(
        HttpStatusCode status, ReasoningProviderErrorKind expected) =>
        Assert.Equal(expected, (await Refusal(status)).Kind);

    /// <summary>
    /// An account with no row on the server is a sign-in problem, not a
    /// permission one, and the fix is a sentence the user can follow rather
    /// than the gateway's own wording — which for this case is about a database
    /// row and would mean nothing to them.
    /// </summary>
    [Fact]
    public async Task A_missing_account_row_overrides_the_gateway_sentence()
    {
        var error = await Refusal(
            HttpStatusCode.Conflict,
            """{"error":"No account_status row for user."}""");

        Assert.DoesNotContain("account_status", error.Message);
        Assert.Contains("Sign out", error.Message);
    }

    /// <summary>
    /// A body that is not the gateway's shape — an HTML error page from
    /// something in front of it, most likely — still produces a sentence rather
    /// than a parse failure or the raw page.
    /// </summary>
    [Fact]
    public async Task An_unreadable_body_still_produces_a_sentence()
    {
        var error = await Refusal(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>");

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.DoesNotContain("<html>", error.Message);
    }

    // =========================== before the network ===========================

    /// <summary>
    /// Refused locally when there is no session, rather than sent and refused.
    /// An agent with no token would otherwise make one doomed round trip per
    /// step, for as many steps as it had.
    /// </summary>
    [Fact]
    public async Task Running_without_a_session_never_reaches_the_network()
    {
        using var handler = new Canned(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);
        using var client = new MetisGatewayAgentClient(new Uri("https://gateway.example/"), http);

        var error = await Assert.ThrowsAsync<ReasoningProviderException>(
            () => client.CompleteStepAsync(" ", "system", OneMessage, null, 0.1));

        Assert.Equal(ReasoningProviderErrorKind.Authentication, error.Kind);
        Assert.Null(handler.LastRequest);
    }

    // ============================== the happy path ==============================

    [Fact]
    public async Task A_successful_step_returns_the_text()
    {
        using var handler = new Canned(
            HttpStatusCode.OK, """{"text":"{\"thought\":\"done\"}"}""");
        using var http = new HttpClient(handler);
        using var client = new MetisGatewayAgentClient(new Uri("https://gateway.example/"), http);

        var reply = await client.CompleteStepAsync(
            "token", "system", OneMessage, "gemini-2.5-flash", 0.1);

        Assert.Equal("""{"thought":"done"}""", reply);
    }

    /// <summary>
    /// An empty answer is an error rather than an empty string. An agent handed
    /// "" would try to parse it as its next action, fail, and record a
    /// confusing parse error instead of the real one.
    /// </summary>
    [Fact]
    public async Task An_empty_answer_is_an_error()
    {
        using var handler = new Canned(HttpStatusCode.OK, """{"text":"   "}""");
        using var http = new HttpClient(handler);
        using var client = new MetisGatewayAgentClient(new Uri("https://gateway.example/"), http);

        var error = await Assert.ThrowsAsync<ReasoningProviderException>(
            () => client.CompleteStepAsync("token", "system", OneMessage, null, 0.1));

        Assert.Equal(ReasoningProviderErrorKind.EmptyResponse, error.Kind);
    }

    /// <summary>
    /// The request carries the session as a bearer token and goes to the
    /// agent-step route, not the ordinary assist one. That distinction is the
    /// entire reason the agent allowance can be charged separately from the
    /// conversation allowance.
    /// </summary>
    [Fact]
    public async Task The_request_is_a_bearer_token_on_the_agent_step_route()
    {
        using var handler = new Canned(HttpStatusCode.OK, """{"text":"ok"}""");
        using var http = new HttpClient(handler);
        using var client = new MetisGatewayAgentClient(new Uri("https://gateway.example/"), http);

        await client.CompleteStepAsync("tok_123", "be careful", OneMessage, "gemini-2.5-flash", 0.1);

        Assert.EndsWith("/v1/agent-step", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("tok_123", handler.LastRequest.Headers.Authorization.Parameter);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("be careful", sent.RootElement.GetProperty("system").GetString());
        Assert.Equal("user", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }
}
