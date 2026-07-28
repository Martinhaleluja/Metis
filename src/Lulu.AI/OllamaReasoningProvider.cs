using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.Core.Contracts;
using Lulu.Core.Models;

namespace Lulu.AI;

/// <summary>Ollama native Chat API provider for local and HTTPS-hosted models.</summary>
public sealed class OllamaReasoningProvider : IReasoningProvider, IDisposable
{
    private const string ProviderId = "ollama";
    private const string DefaultEndpoint = "http://127.0.0.1:11434";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly int _contextTokens;
    private readonly bool _enableThinking;
    private bool _disposed;

    public OllamaReasoningProvider(
        HttpClient? httpClient = null,
        Uri? endpoint = null,
        int contextTokens = 4096,
        bool enableThinking = true)
    {
        Endpoint = ReasoningProviderSupport.NormalizeEndpoint(
            endpoint,
            DefaultEndpoint,
            "/api",
            ProviderId);
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _contextTokens = Math.Clamp(contextTokens, 2048, 4096);
        _enableThinking = enableThinking;
    }

    public ReasoningProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Ollama",
        ReasoningAuthenticationKind.OptionalBearerToken,
        ReasoningProviderCapabilities.Text |
        ReasoningProviderCapabilities.Vision |
        ReasoningProviderCapabilities.ModelDiscovery |
        ReasoningProviderCapabilities.StructuredPlans |
        ReasoningProviderCapabilities.LocalEndpoint |
        ReasoningProviderCapabilities.RemoteEndpoint);

    public Uri Endpoint { get; }

    public async Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedModel = ValidateModel(model);
        // A 2K-4K local context needs a deliberately compact accessibility
        // snapshot. Vision remains attached separately and is never cropped.
        var compactRequest = request with
        {
            AutomationContext = string.IsNullOrWhiteSpace(request.AutomationContext)
                ? null
                : ReasoningProviderSupport.Shorten(request.AutomationContext, 6_000)
        };
        var prompt = ReasoningProviderSupport.BuildUserPrompt(compactRequest);
        var messages = new List<object>
        {
            new { role = "system", content = ReasoningProviderSupport.SystemInstruction }
        };
        if (request.ScreenshotBytes is { Length: > 0 })
        {
            messages.Add(new
            {
                role = "user",
                content = prompt,
                images = new[] { Convert.ToBase64String(request.ScreenshotBytes) }
            });
        }
        else
        {
            messages.Add(new { role = "user", content = prompt });
        }

        var (statusCode, body) = await SendChatAsync(
                credential,
                normalizedModel,
                messages,
                _enableThinking,
                cancellationToken)
            .ConfigureAwait(false);

        // Ollama returns HTTP 400 when a normal instruct model receives
        // think=true. Retry transparently so changing models never forces the
        // user to download a separate thinking variant.
        if (_enableThinking && IsThinkingUnsupported(statusCode, body))
        {
            (statusCode, body) = await SendChatAsync(
                    credential,
                    normalizedModel,
                    messages,
                    enableThinking: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!IsSuccess(statusCode))
        {
            throw CreateApiException(statusCode, body, normalizedModel, credential);
        }

        var text = ReadChatText(body);
        return ReasoningProviderSupport.ParsePlanResponse(ProviderId, normalizedModel, text, request);
    }

    private async Task<(HttpStatusCode StatusCode, string Body)> SendChatAsync(
        string? credential,
        string model,
        IReadOnlyCollection<object> messages,
        bool enableThinking,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages,
            stream = false,
            think = enableThinking,
            keep_alive = "10m",
            format = ReasoningProviderSupport.AssistantPlanJsonSchema,
            options = new
            {
                temperature = 0.1,
                num_predict = 700,
                num_ctx = _contextTokens
            }
        }, JsonOptions);

        using var httpRequest = CreateRequest(HttpMethod.Post, "chat", credential);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient,
                httpRequest,
                ProviderId,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    private static bool IsThinkingUnsupported(HttpStatusCode statusCode, string body)
    {
        if (statusCode != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return body.Contains("think", StringComparison.OrdinalIgnoreCase) &&
               (body.Contains("does not support", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSuccess(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status is >= 200 and <= 299;
    }

    public async Task<IReadOnlyList<ReasoningModelInfo>> ListModelsAsync(
        string? credential,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var request = CreateRequest(HttpMethod.Get, "tags", credential);
        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient,
                request,
                ProviderId,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, null, credential);
        }

        using var document = ReasoningProviderSupport.ParseJson(ProviderId, body);
        if (!document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var capabilities = Descriptor.Capabilities & ~ReasoningProviderCapabilities.ModelDiscovery;
        return models.EnumerateArray()
            .Select(item => new
            {
                Name = ReadString(item, "name") ?? ReadString(item, "model"),
                Detail = FormatModelDetail(item)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ReasoningModelInfo(
                item.Name!,
                string.IsNullOrWhiteSpace(item.Detail) ? item.Name! : $"{item.Name} ({item.Detail})",
                capabilities))
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
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
                        "This is a Lulu connection diagnostic. Set spoken_text to OK with no actions."),
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            return new ProviderTestResult(
                result.Model,
                true,
                $"Ollama model {result.Model} is available ({stopwatch.Elapsed.TotalSeconds:0.0}s). Vision depends on the selected model.",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(
                string.IsNullOrWhiteSpace(model) ? "Ollama" : model.Trim(),
                false,
                exception.Message,
                stopwatch.Elapsed);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string? credential)
    {
        var request = new HttpRequestMessage(method, new Uri(Endpoint, relativeUrl));
        if (!string.IsNullOrWhiteSpace(credential))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Trim());
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Lulu-Desktop/1.0");
        return request;
    }

    private static string ReadChatText(string body)
    {
        using var document = ReasoningProviderSupport.ParseJson(ProviderId, body);
        var root = document.RootElement;
        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            return ReadString(message, "content")?.Trim() ?? string.Empty;
        }

        return ReadString(root, "response")?.Trim() ?? string.Empty;
    }

    private static string? FormatModelDetail(JsonElement item)
    {
        if (!item.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parameters = ReadString(details, "parameter_size");
        var quantization = ReadString(details, "quantization_level");
        return string.Join(
            ", ",
            new[] { parameters, quantization }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static ReasoningProviderException CreateApiException(
        HttpStatusCode statusCode,
        string body,
        string? model,
        string? credential)
    {
        var detail = ReasoningProviderSupport.ReadErrorDetail(body, credential);
        var status = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Authentication,
                "Ollama rejected the optional bearer token. Update the token for this hosted endpoint.",
                status),
            HttpStatusCode.Forbidden => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Permission,
                $"Ollama denied access to this model or endpoint. {detail}",
                status),
            HttpStatusCode.NotFound => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ModelUnavailable,
                $"Ollama model '{model ?? "requested"}' is not installed or available. Pull it in Ollama, then refresh models. {detail}",
                status),
            HttpStatusCode.TooManyRequests => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                $"The hosted Ollama endpoint is rate-limiting requests. Wait and retry. {detail}",
                status),
            HttpStatusCode.BadRequest => ReasoningProviderSupport.Error(
                ProviderId,
                detail.Contains("model", StringComparison.OrdinalIgnoreCase)
                    ? ReasoningProviderErrorKind.ModelUnavailable
                    : ReasoningProviderErrorKind.InvalidRequest,
                $"Ollama rejected Lulu's request or model settings. {detail}",
                status),
            _ when status >= 500 => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                $"Ollama is temporarily unavailable (HTTP {status}). Check its server log and retry. {detail}",
                status),
            _ => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Unknown,
                $"Ollama returned HTTP {status}. {detail}",
                status)
        };
    }

    private static string ValidateModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ModelUnavailable,
                "Choose an installed Ollama model in Setup.");
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
