using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.AI;

/// <summary>
/// Asks the Metis gateway to answer a turn, on Metis's own provider key.
///
/// This is the only provider in this assembly that does not hold a credential
/// for the model it is talking to. Its <c>credential</c> is the user's Supabase
/// access token — proof of who they are, not permission to spend — which is
/// exactly what <see cref="IReasoningProvider"/> already describes when it says
/// credentials are supplied at call time so implementations never persist them.
///
/// Everything about cost lives on the other side. This class sends the turn,
/// reads the reply, and turns the gateway's refusals into the same error kinds
/// every other provider raises, so nothing above it has to know that a managed
/// turn is any different from a bring-your-own-key one.
///
/// One thing it deliberately does not do: fall back. When the gateway says the
/// plan is too small or the month's allowance is spent, the answer is not to
/// quietly try the user's own key instead. That would spend their money because
/// Metis ran out of its own, which nobody agreed to. The refusal kinds below are
/// chosen so the runtime's fallback logic can tell that case apart.
/// </summary>
public sealed class MetisGatewayReasoningProvider : IReasoningProvider, IDisposable
{
    /// <summary>
    /// The id this provider reports on every response and every error.
    ///
    /// Public rather than internal because the runtime's fallback rule has to
    /// name it: a quota refusal from the gateway means something completely
    /// different from a quota refusal from a provider on the user's own key, and
    /// telling them apart is what stops Metis spending someone else's money when
    /// it runs out of its own.
    /// </summary>
    public const string ProviderId = "metis";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseAddress;
    private bool _disposed;

    public MetisGatewayReasoningProvider(Uri baseAddress, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _baseAddress = baseAddress;
        _ownsClient = httpClient is null;

        // Longer than the other providers allow. A managed turn can pay a cold
        // start on the hosting platform on top of the model's own time, and a
        // timeout that fires during a wake-up looks to the user exactly like a
        // service that does not work.
        _httpClient = httpClient ?? MetisHttp.CreateClient(TimeSpan.FromSeconds(120));
    }

    public ReasoningProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Metis (included with your plan)",
        ReasoningAuthenticationKind.ApiKey,
        ReasoningProviderCapabilities.Text |
        ReasoningProviderCapabilities.Vision |
        ReasoningProviderCapabilities.StructuredPlans |
        ReasoningProviderCapabilities.RemoteEndpoint);

    public Uri Endpoint => _baseAddress;

    /// <summary>
    /// The allowance reported by the last successful turn, so the account panel
    /// can show what is left without asking again.
    ///
    /// Held on the provider rather than returned through
    /// <see cref="ReasoningResponse"/> because it is not part of the answer: it
    /// is a fact about the account that happened to arrive alongside one.
    /// </summary>
    public AssistAllowance? LastAllowance { get; private set; }

    public async Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        var accessToken = ValidateAccessToken(credential);
        var streaming = onTextDelta is not null;
        var requestId = Guid.NewGuid().ToString("d");

        var envelope = AssistRequest.FromGeminiRequest(
            request,
            requestId,
            provider: null,
            model: string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            feature: request.Activation == ActivationKind.Inspect ? "inspect" : "chat",
            stream: streaming);

        using var content = BuildMultipart(envelope, request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "v1/assist"))
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient, httpRequest, ProviderId, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateApiException(response.StatusCode, errorBody);
        }

        ReadAllowanceHeaders(response);

        if (!streaming)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var reply = JsonSerializer.Deserialize<AssistResponse>(body, JsonOptions);
            if (reply is null)
            {
                throw ReasoningProviderSupport.Error(
                    ProviderId,
                    ReasoningProviderErrorKind.EmptyResponse,
                    "Metis's AI service returned nothing. Try again.");
            }

            LastAllowance = reply.Allowance ?? LastAllowance;
            return WithUsage(
                ReasoningProviderSupport.ParsePlanResponse(ProviderId, reply.Model, reply.Text, request),
                reply.Usage);
        }

        var answer = new StreamingPlanText(onTextDelta);
        AssistUsage? usage = null;
        string? streamedErrorKind = null;
        string? streamedErrorMessage = null;

        await ReasoningProviderSupport.ReadEventStreamAsync(
            response,
            element =>
            {
                switch (ReasoningProviderSupport.ReadString(element, "type"))
                {
                    case "delta":
                        answer.Append(ReasoningProviderSupport.ReadString(element, "text"));
                        break;

                    case "usage":
                        usage = element.TryGetProperty("usage", out var reported)
                            ? reported.Deserialize<AssistUsage>(JsonOptions)
                            : null;
                        if (element.TryGetProperty("allowance", out var allowance))
                        {
                            LastAllowance = allowance.Deserialize<AssistAllowance>(JsonOptions) ?? LastAllowance;
                        }

                        break;

                    case "error":
                        // A failure that arrives mid-stream, after the headers
                        // already said 200. Recorded rather than thrown here so
                        // the reader finishes cleanly and the partial answer is
                        // still available to the parser below.
                        streamedErrorKind = ReasoningProviderSupport.ReadString(element, "kind");
                        streamedErrorMessage = ReasoningProviderSupport.ReadString(element, "message");
                        break;
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (streamedErrorMessage is not null && !answer.HasPublished)
        {
            throw ReasoningProviderSupport.Error(
                ProviderId, KindFor(streamedErrorKind), streamedErrorMessage);
        }

        return WithUsage(
            ReasoningProviderSupport.ParsePlanResponse(
                ProviderId,
                string.IsNullOrWhiteSpace(model) ? "metis" : model,
                answer.Raw,
                request),
            usage);
    }

    /// <summary>
    /// The models this account's plan may ask for, as the gateway reports them.
    ///
    /// It reads them from <c>/v1/me</c> rather than from a provider's model list,
    /// because on this route the question is not "what does the provider offer"
    /// but "what has this plan bought".
    /// </summary>
    public async Task<IReadOnlyList<ReasoningModelInfo>> ListModelsAsync(
        string? credential,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var accessToken = ValidateAccessToken(credential);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, "v1/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient, request, ProviderId, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        var me = JsonSerializer.Deserialize<MeResponse>(body, JsonOptions);
        if (me is null)
        {
            return [];
        }

        var capabilities = ReasoningProviderCapabilities.Text |
                           ReasoningProviderCapabilities.StructuredPlans |
                           ReasoningProviderCapabilities.RemoteEndpoint;

        // Vision is offered only when the plan actually includes it on Metis's
        // key. Listing a vision model this account cannot send an image to would
        // put the refusal at the end of a turn instead of before it.
        if (me.Features.Contains(nameof(MetisFeature.ManagedScreenVision), StringComparer.Ordinal))
        {
            capabilities |= ReasoningProviderCapabilities.Vision;
        }

        return me.Limits.ManagedModels
            .Select(model => new ReasoningModelInfo(model, model, capabilities))
            .ToArray();
    }

    public async Task<ProviderTestResult> TestModelAsync(
        string? credential,
        string model,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await GenerateAsync(
                    credential,
                    model,
                    new GeminiRequest("Reply with the single word ready."),
                    onTextDelta: null,
                    cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            return new ProviderTestResult(
                result.Model, true, "Metis answered on your plan.", stopwatch.Elapsed);
        }
        catch (ReasoningProviderException exception)
        {
            stopwatch.Stop();
            return new ProviderTestResult(model, false, exception.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Builds the multipart body: the turn as JSON, and the screenshot and audio
    /// as their own parts.
    ///
    /// Not base64 inside the JSON. Thirteen mebibytes of PNG becomes about
    /// seventeen once encoded and lands in a string as roughly thirty-four
    /// megabytes before anything has decoded it — paid for once here and again
    /// on the server. Separate parts also let the gateway stop reading an
    /// oversized capture partway through rather than after all of it arrives.
    /// </summary>
    private static MultipartFormDataContent BuildMultipart(AssistRequest envelope, GeminiRequest request)
    {
        var content = new MultipartFormDataContent();

        var json = new StringContent(
            JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8, "application/json");
        content.Add(json, "request");

        if (request.ScreenshotBytes is { Length: > 0 })
        {
            var image = new ByteArrayContent(request.ScreenshotBytes);
            image.Headers.ContentType = new MediaTypeHeaderValue(
                AssistantPromptKernel.NormalizeImageMimeType(request.ScreenshotMimeType));
            content.Add(image, "screenshot", "screen.png");
        }

        if (request.RecordedAudioWav is { Length: > 0 })
        {
            var audio = new ByteArrayContent(request.RecordedAudioWav);
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audio, "audio", "voice.wav");
        }

        return content;
    }

    private void ReadAllowanceHeaders(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Metis-Allowance-Used", out var used)
            || !response.Headers.TryGetValues("X-Metis-Allowance-Limit", out var limit)
            || !response.Headers.TryGetValues("X-Metis-Allowance-Resets", out var resets))
        {
            return;
        }

        if (decimal.TryParse(used.FirstOrDefault(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var usedValue)
            && decimal.TryParse(limit.FirstOrDefault(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var limitValue)
            && DateTimeOffset.TryParse(resets.FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var resetsValue))
        {
            LastAllowance = new AssistAllowance(usedValue, limitValue, resetsValue);
        }
    }

    private static ReasoningResponse WithUsage(ReasoningResponse response, AssistUsage? usage) =>
        usage is null ? response : response with { Usage = usage.ToReport() };

    /// <summary>
    /// Turns the gateway's status codes into the same error kinds every other
    /// provider raises.
    ///
    /// The one worth reading carefully is 403. The obvious mapping is
    /// <see cref="ReasoningProviderErrorKind.Permission"/>, and it is wrong: it
    /// makes the interface tell the user their credential is bad, and a user
    /// whose credential is fine will go and replace a working key looking for a
    /// fault that is not there. Their key is fine. Their plan is small. Those
    /// deserve different sentences, which is why PlanLimited exists.
    /// </summary>
    private ReasoningProviderException CreateApiException(HttpStatusCode status, string body)
    {
        var (kind, message) = ReadGatewayError(body);

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
                message ?? "That is not included in your Metis plan.",
                (int)status),

            HttpStatusCode.PaymentRequired => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                message ?? "You have used this month's included AI.",
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
                message ?? "Metis's AI service could not read that request.",
                (int)status),

            HttpStatusCode.ServiceUnavailable => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                message ?? "Metis's included AI is unavailable right now. Your own API key still works.",
                (int)status),

            _ => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                message ?? "Metis's AI service could not answer. Try again shortly.",
                (int)status)
        };
    }

    /// <summary>
    /// Reads the gateway's own <c>{error, kind}</c> body.
    ///
    /// The body is deliberately trusted for its message here, unlike a
    /// provider's error text, because it is written by Metis for the user rather
    /// than by a third party for a developer — and because the gateway is
    /// careful never to put a provider's raw error, an account name, or a key
    /// into it.
    /// </summary>
    private static (string? Kind, string? Message) ReadGatewayError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return (
                ReasoningProviderSupport.ReadString(document.RootElement, "kind"),
                ReasoningProviderSupport.ReadString(document.RootElement, "error")
                ?? ReasoningProviderSupport.ReadString(document.RootElement, "detail"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static ReasoningProviderErrorKind KindFor(string? kind) => kind switch
    {
        "plan" => ReasoningProviderErrorKind.PlanLimited,
        "quota" => ReasoningProviderErrorKind.QuotaOrRateLimit,
        "degraded" => ReasoningProviderErrorKind.ServiceUnavailable,
        "provider" => ReasoningProviderErrorKind.ServiceUnavailable,
        _ => ReasoningProviderErrorKind.Unknown
    };

    private static string ValidateAccessToken(string? credential)
    {
        var token = credential?.Trim();
        return string.IsNullOrEmpty(token)
            ? throw ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                "Sign in to use Metis's own AI, or add your own API key in Setup.")
            : token;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
