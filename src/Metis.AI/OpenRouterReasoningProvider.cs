using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.AI;

/// <summary>
/// OpenRouter provider, giving Metis access to many hosted models — including
/// free ones — behind a single key.
///
/// Deliberately not built on the OpenClaw provider despite both being
/// "OpenAI-compatible": OpenClaw speaks the Responses API (POST /responses,
/// input_image parts) and OpenRouter only implements Chat Completions (POST
/// /chat/completions, image_url parts). Pointing OpenClaw at OpenRouter 404s,
/// so the request and response shapes here are genuinely different.
/// </summary>
public sealed class OpenRouterReasoningProvider : IReasoningProvider, IDisposable
{
    private const string ProviderId = "openrouter";
    private const string DefaultEndpoint = "https://openrouter.ai/api";
    private static readonly byte[] DiagnosticPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public OpenRouterReasoningProvider(HttpClient? httpClient = null, Uri? endpoint = null)
    {
        Endpoint = ReasoningProviderSupport.NormalizeEndpoint(
            endpoint,
            DefaultEndpoint,
            "/v1",
            ProviderId);
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? MetisHttp.CreateClient(TimeSpan.FromSeconds(120));
    }

    public ReasoningProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "OpenRouter",
        ReasoningAuthenticationKind.ApiKey,
        ReasoningProviderCapabilities.Text |
        ReasoningProviderCapabilities.Vision |
        ReasoningProviderCapabilities.ModelDiscovery |
        ReasoningProviderCapabilities.StructuredPlans |
        ReasoningProviderCapabilities.RemoteEndpoint);

    public Uri Endpoint { get; }

    public async Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedModel = NormalizeModel(model);

        // Chat Completions multimodal shape: image_url is an object with a url,
        // not the bare string the Responses API takes.
        var content = new List<object> { new { type = "text", text = ReasoningProviderSupport.BuildUserPrompt(request) } };
        if (request.ScreenshotBytes is { Length: > 0 })
        {
            content.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{ReasoningProviderSupport.NormalizeImageMimeType(request.ScreenshotMimeType)};base64,{Convert.ToBase64String(request.ScreenshotBytes)}"
                }
            });
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = normalizedModel,
            messages = new object[]
            {
                new { role = "system", content = ReasoningProviderSupport.BuildSystemInstruction(request) },
                new { role = "user", content }
            },
            stream = onTextDelta is not null,
            max_tokens = ReasoningProviderSupport.MaxPlanTokens,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "metis_desktop_plan",
                    strict = true,
                    schema = ReasoningProviderSupport.AssistantPlanJsonSchema
                }
            }
        }, JsonOptions);

        using var httpRequest = CreateRequest(HttpMethod.Post, "chat/completions", credential);
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
                throw CreateApiException(response.StatusCode, body, normalizedModel, credential);
            }

            return ReasoningProviderSupport.ParsePlanResponse(
                ProviderId, normalizedModel, ReadResponseText(body), request);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateApiException(response.StatusCode, errorBody, normalizedModel, credential);
        }

        var answer = new StreamingPlanText(onTextDelta);
        await ReasoningProviderSupport.ReadEventStreamAsync(
            response,
            element => answer.Append(ReasoningProviderSupport.ReadChatCompletionDelta(element)),
            cancellationToken).ConfigureAwait(false);

        return ReasoningProviderSupport.ParsePlanResponse(ProviderId, normalizedModel, answer.Raw, request);
    }

    public async Task<IReadOnlyList<ReasoningModelInfo>> ListModelsAsync(
        string? credential,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var request = CreateRequest(HttpMethod.Get, "models", credential);
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
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var capabilities = Descriptor.Capabilities & ~ReasoningProviderCapabilities.ModelDiscovery;

        // Metis cannot work without vision — it reasons about a screenshot on
        // every turn — so text-only models are filtered out rather than
        // offered and left to fail at the first question.
        return data.EnumerateArray()
            .Where(SupportsImageInput)
            .Select(item => new
            {
                Id = ReadString(item, "id"),
                DisplayName = ReadString(item, "name")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => new ReasoningModelInfo(
                item.Id!,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id! : item.DisplayName!,
                capabilities))
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// OpenRouter advertises modality as architecture.input_modalities, with an
    /// older architecture.modality string on some entries. A model is kept only
    /// if one of them mentions image input.
    /// </summary>
    private static bool SupportsImageInput(JsonElement model)
    {
        if (!model.TryGetProperty("architecture", out var architecture)
            || architecture.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (architecture.TryGetProperty("input_modalities", out var modalities)
            && modalities.ValueKind == JsonValueKind.Array)
        {
            return modalities.EnumerateArray().Any(entry =>
                entry.ValueKind == JsonValueKind.String
                && string.Equals(entry.GetString(), "image", StringComparison.OrdinalIgnoreCase));
        }

        var legacy = ReadString(architecture, "modality");
        return legacy is not null
               && legacy.Contains("image", StringComparison.OrdinalIgnoreCase);
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
                $"OpenRouter model {result.Model} answered ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(
                string.IsNullOrWhiteSpace(model) ? "openrouter" : model.Trim(),
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

        // OpenRouter uses these to attribute traffic. They are optional, but
        // without them requests are filed as anonymous.
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/Martinhaleluja/Metis");
        request.Headers.TryAddWithoutValidation("X-Title", "Metis");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Metis-Desktop/1.0");
        return request;
    }

    private static string ReadResponseText(string body)
    {
        using var document = ReasoningProviderSupport.ParseJson(ProviderId, body);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var text = new List<string>();
        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (message.TryGetProperty("content", out var content))
            {
                // Content is normally a string, but a few providers return the
                // multipart array form even on the way back.
                if (content.ValueKind == JsonValueKind.String)
                {
                    var value = content.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        text.Add(value);
                    }
                }
                else if (content.ValueKind == JsonValueKind.Array)
                {
                    text.AddRange(content.EnumerateArray()
                        .Select(part => ReadString(part, "text"))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!));
                }
            }
        }

        return string.Join("\n", text).Trim();
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
                "OpenRouter rejected the API key. Create one at openrouter.ai/keys and save it in Preferences.",
                status),
            HttpStatusCode.PaymentRequired => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                $"OpenRouter reports insufficient credit for this model. Free models are capped per day; try a ':free' model or add credit. {detail}",
                status),
            HttpStatusCode.Forbidden => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Permission,
                $"OpenRouter denied this model. Your account's data policy may exclude the providers serving it. {detail}",
                status),
            HttpStatusCode.NotFound => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.InvalidRequest,
                $"OpenRouter has no model '{model ?? "requested"}'. Use Find models to list the vision-capable ones.",
                status),
            HttpStatusCode.TooManyRequests => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                $"OpenRouter is rate-limiting this key. Free models allow roughly 20 requests a minute and a limited number per day. {detail}",
                status),
            HttpStatusCode.BadRequest => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.InvalidRequest,
                $"OpenRouter rejected the request. The model may not accept images or structured output. {detail}",
                status),
            _ when status >= 500 => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                $"OpenRouter or the upstream provider could not answer (HTTP {status}). {detail}",
                status),
            _ => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Unknown,
                $"OpenRouter returned HTTP {status}. {detail}",
                status)
        };
    }

    private static string NormalizeModel(string? model) =>
        string.IsNullOrWhiteSpace(model) || string.Equals(model.Trim(), "default", StringComparison.OrdinalIgnoreCase)
            ? "google/gemini-2.0-flash-exp:free"
            : model.Trim();

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
