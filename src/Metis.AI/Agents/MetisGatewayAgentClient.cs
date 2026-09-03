using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.AI.Agents;

/// <summary>One message of an agent's exchange, as the gateway wants it.</summary>
public sealed record AgentGatewayMessage(string Role, string Content);

/// <summary>
/// Asks the Metis gateway for an agent's next step, on Metis's own provider key.
///
/// It is the agent's counterpart to <see cref="MetisGatewayReasoningProvider"/>
/// and holds no credential for the model it is talking to: what it sends is the
/// user's Supabase access token, which proves who is asking and authorises
/// nothing on its own. Everything about cost is decided on the other side.
///
/// Two things it deliberately does not do. It does not parse the reply — an
/// agent's decision is its own JSON and <c>AgentReasoningClient</c> owns the
/// reader for it, so this returns the text exactly as it arrived. And it does
/// not fall back: when the gateway says the plan does not include agents, or
/// that the month's steps are spent, quietly running the same step on the
/// user's own key would spend their money because Metis ran out of its own.
/// The error kinds below are chosen so the caller can tell that case apart from
/// a credential that has actually gone bad.
/// </summary>
public sealed class MetisGatewayAgentClient : IDisposable
{
    /// <summary>The id this client reports on every failure it raises.</summary>
    public const string ProviderId = MetisGatewayReasoningProvider.ProviderId;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseAddress;
    private bool _disposed;

    public MetisGatewayAgentClient(Uri baseAddress, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _baseAddress = baseAddress;
        _ownsClient = httpClient is null;

        // Longer than a bring-your-own-key agent turn allows, for the same
        // reason the reasoning provider is: a managed step can pay a cold start
        // on the hosting platform on top of the model's own time, and a timeout
        // that fires during a wake-up ends the whole task on what was only a
        // slow first request.
        _httpClient = httpClient ?? MetisHttp.CreateClient(TimeSpan.FromSeconds(120));
    }

    /// <summary>
    /// Runs one step and returns the model's reply verbatim.
    /// </summary>
    /// <param name="accessToken">The Supabase access token, not a provider key.</param>
    public async Task<string> CompleteStepAsync(
        string? accessToken,
        string systemPrompt,
        IReadOnlyList<AgentGatewayMessage> messages,
        string? model,
        double temperature,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);

        var token = accessToken?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                "Sign in to run agents on Metis's own AI, or add your own API key in Setup.");
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                system = systemPrompt,
                messages = messages.Select(message => new { role = message.Role, content = message.Content }),

                // Sent as a request rather than a decision. The gateway
                // substitutes from the plan's own allow-list, so asking for
                // something the plan does not include is answered with a
                // cheaper model instead of an error.
                model,
                temperature,
                responseFormat = "json"
            },
            JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "v1/agent-step"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient, request, ProviderId, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        var reply = JsonSerializer.Deserialize<AssistResponse>(body, JsonOptions);
        if (reply is null || string.IsNullOrWhiteSpace(reply.Text))
        {
            throw ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.EmptyResponse,
                "Metis's AI service returned nothing for that agent step.");
        }

        return reply.Text;
    }

    /// <summary>
    /// Turns the gateway's status codes into the error kinds the rest of Metis
    /// already understands, exactly as MetisGatewayReasoningProvider does.
    ///
    /// The mapping worth reading twice is 403. The obvious answer is
    /// <see cref="ReasoningProviderErrorKind.Permission"/>, and it is wrong here
    /// for the same reason it is wrong there: it tells the user their
    /// credential is bad, so someone whose session is perfectly fine goes
    /// looking for a fault that does not exist. Their sign-in is fine. Their
    /// plan does not include background agents. PlanLimited is the difference.
    ///
    /// Nothing in any message below is derived from the access token, and the
    /// token is never placed in a URL, so a failure can be logged in full
    /// without leaking the session it failed on.
    /// </summary>
    private static ReasoningProviderException CreateApiException(HttpStatusCode status, string body)
    {
        var message = ReadGatewayMessage(body);

        return status switch
        {
            HttpStatusCode.Unauthorized => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                message ?? "Your Metis session has expired. Sign in again.",
                (int)status),

            HttpStatusCode.Forbidden => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.PlanLimited,
                message ?? "Background agents are not included in your Metis plan.",
                (int)status),

            HttpStatusCode.PaymentRequired => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                message ?? "You have used this month's included agent steps.",
                (int)status),

            HttpStatusCode.TooManyRequests => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                message ?? "Too many requests just now. Give it a few seconds.",
                (int)status),

            HttpStatusCode.Conflict => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                "This account is not set up yet. Sign out and back in.",
                (int)status),

            HttpStatusCode.BadRequest => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.InvalidRequest,
                message ?? "Metis's AI service could not read that agent step.",
                (int)status),

            HttpStatusCode.ServiceUnavailable => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                message ?? "Metis's included AI is unavailable right now. Your own API key still works.",
                (int)status),

            _ => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                message ?? "Metis's AI service could not answer that agent step. Try again shortly.",
                (int)status)
        };
    }

    /// <summary>
    /// Reads the gateway's own <c>{error, kind}</c> body for its sentence.
    ///
    /// Trusted for its message, unlike a third-party provider's error text,
    /// because Metis writes it for the user and is careful never to put a
    /// provider's raw error, an account name, or a key into it.
    /// </summary>
    private static string? ReadGatewayMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return ReasoningProviderSupport.ReadString(document.RootElement, "error")
                ?? ReasoningProviderSupport.ReadString(document.RootElement, "detail");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
