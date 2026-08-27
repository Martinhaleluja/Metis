using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.AI;

/// <summary>Anthropic Messages API reasoning provider.</summary>
public sealed class ClaudeReasoningProvider : IReasoningProvider, IDisposable
{
    private const string ProviderId = "claude";
    private const string ApiRoot = "https://api.anthropic.com/v1/";
    private const string ApiVersion = "2023-06-01";
    private static readonly byte[] DiagnosticPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public ClaudeReasoningProvider(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? MetisHttp.CreateClient(TimeSpan.FromSeconds(75));
    }

    public ReasoningProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Anthropic Claude",
        ReasoningAuthenticationKind.ApiKey,
        ReasoningProviderCapabilities.Text |
        ReasoningProviderCapabilities.Vision |
        ReasoningProviderCapabilities.ModelDiscovery |
        ReasoningProviderCapabilities.StructuredPlans |
        ReasoningProviderCapabilities.RemoteEndpoint);

    public Uri Endpoint { get; } = new(ApiRoot, UriKind.Absolute);

    public async Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var apiKey = ValidateApiKey(credential);
        var normalizedModel = ValidateModel(model);
        var prompt = ReasoningProviderSupport.BuildUserPrompt(request);
        var content = new List<object>();
        if (request.ScreenshotBytes is { Length: > 0 })
        {
            content.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = ReasoningProviderSupport.NormalizeImageMimeType(request.ScreenshotMimeType),
                    data = Convert.ToBase64String(request.ScreenshotBytes)
                }
            });
        }

        content.Add(new { type = "text", text = prompt });
        var payload = JsonSerializer.Serialize(new
        {
            model = normalizedModel,
            max_tokens = ReasoningProviderSupport.MaxPlanTokens,
            temperature = 0.1,

            // Sent as a cacheable block rather than a bare string. The teaching
            // rules are the same eight and a half kilobytes on every turn, and
            // re-reading them from scratch each time was work the user waited
            // for and paid for.
            system = new[]
            {
                new
                {
                    type = "text",
                    text = ReasoningProviderSupport.BuildSystemInstruction(request),
                    cache_control = new { type = "ephemeral" }
                }
            },
            messages = new[] { new { role = "user", content } },
            stream = onTextDelta is not null
        }, JsonOptions);

        using var httpRequest = CreateRequest(HttpMethod.Post, "messages", apiKey);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient,
                httpRequest,
                ProviderId,
                cancellationToken)
            .ConfigureAwait(false);

        if (onTextDelta is null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, body, normalizedModel, apiKey);
            }

            return ReasoningProviderSupport.ParsePlanResponse(
                ProviderId, normalizedModel, ReadMessageText(body), request);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateApiException(response.StatusCode, errorBody, normalizedModel, apiKey);
        }

        var answer = new StreamingPlanText(onTextDelta);
        await ReasoningProviderSupport.ReadEventStreamAsync(
            response,
            element =>
            {
                if (ReasoningProviderSupport.ReadString(element, "type") != "content_block_delta" ||
                    !element.TryGetProperty("delta", out var delta))
                {
                    return;
                }

                answer.Append(ReasoningProviderSupport.ReadString(delta, "text"));
            },
            cancellationToken).ConfigureAwait(false);

        return ReasoningProviderSupport.ParsePlanResponse(ProviderId, normalizedModel, answer.Raw, request);
    }

    public async Task<IReadOnlyList<ReasoningModelInfo>> ListModelsAsync(
        string? credential,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var apiKey = ValidateApiKey(credential);
        using var request = CreateRequest(HttpMethod.Get, "models?limit=1000", apiKey);
        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient,
                request,
                ProviderId,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, null, apiKey);
        }

        using var document = ReasoningProviderSupport.ParseJson(ProviderId, body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ReasoningModelInfo>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var displayName = ReadString(item, "display_name") ?? id;
            var capabilities = ReasoningProviderCapabilities.Text |
                               ReasoningProviderCapabilities.StructuredPlans |
                               ReasoningProviderCapabilities.RemoteEndpoint;
            if (SupportsImageInput(item))
            {
                capabilities |= ReasoningProviderCapabilities.Vision;
            }

            results.Add(new ReasoningModelInfo(id, displayName, capabilities));
        }

        return results
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
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
                    new GeminiRequest(
                        "This is a Metis connection diagnostic. Inspect the attached one-pixel image and set spoken_text to OK with no actions.",
                        DiagnosticPng),
                    onTextDelta: null,
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            return new ProviderTestResult(
                result.Model,
                true,
                $"Claude model {result.Model} works for text and screen input ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(
                string.IsNullOrWhiteSpace(model) ? "Claude" : model.Trim(),
                false,
                exception.Message,
                stopwatch.Elapsed);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri(Endpoint, relativeUrl));
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Metis-Desktop/1.0");
        return request;
    }

    private static string ReadMessageText(string body)
    {
        using var document = ReasoningProviderSupport.ParseJson(ProviderId, body);
        if (!document.RootElement.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            content.EnumerateArray()
                .Where(item => string.Equals(ReadString(item, "type"), "text", StringComparison.OrdinalIgnoreCase))
                .Select(item => ReadString(item, "text"))
                .Where(text => !string.IsNullOrWhiteSpace(text)))
            .Trim();
    }

    private static bool SupportsImageInput(JsonElement item)
    {
        if (!item.TryGetProperty("capabilities", out var capabilities) ||
            capabilities.ValueKind != JsonValueKind.Object ||
            !capabilities.TryGetProperty("image_input", out var imageInput) ||
            imageInput.ValueKind != JsonValueKind.Object ||
            !imageInput.TryGetProperty("supported", out var supported))
        {
            // Older Models API responses did not expose capability metadata; Claude's
            // generally available Messages models accept image blocks.
            return true;
        }

        return supported.ValueKind == JsonValueKind.True;
    }

    private static ReasoningProviderException CreateApiException(
        HttpStatusCode statusCode,
        string body,
        string? model,
        string apiKey)
    {
        var detail = ReasoningProviderSupport.ReadErrorDetail(body, apiKey);
        var status = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                "Claude rejected the API key. Save a current Anthropic Console API key in Setup.",
                status),
            HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                $"Claude's rate limit or account credits were exhausted. Check Anthropic Console usage and retry. {detail}",
                status),
            HttpStatusCode.Forbidden => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Permission,
                $"Claude denied this request. Check the key's workspace and model permissions. {detail}",
                status),
            HttpStatusCode.NotFound => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ModelUnavailable,
                $"Claude model '{model ?? "requested"}' is unavailable to this API key. Refresh models in Setup. {detail}",
                status),
            HttpStatusCode.BadRequest => ReasoningProviderSupport.Error(
                ProviderId,
                detail.Contains("model", StringComparison.OrdinalIgnoreCase)
                    ? ReasoningProviderErrorKind.ModelUnavailable
                    : ReasoningProviderErrorKind.InvalidRequest,
                $"Claude rejected Metis's request or model settings. {detail}",
                status),
            HttpStatusCode.RequestEntityTooLarge => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.InvalidRequest,
                "Claude rejected the screen capture because it is too large. Capture a smaller window and retry.",
                status),
            _ when status >= 500 => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                $"Claude is temporarily unavailable (HTTP {status}). Try again shortly. {detail}",
                status),
            _ => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Unknown,
                $"Claude returned HTTP {status}. {detail}",
                status)
        };
    }

    private static string ValidateApiKey(string? credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            throw ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                "No Claude API key is saved. Open Setup and add an Anthropic Console API key.");
        }

        return credential.Trim();
    }

    private static string ValidateModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ModelUnavailable,
                "Choose a Claude model in Setup.");
        }

        return model.Trim();
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
