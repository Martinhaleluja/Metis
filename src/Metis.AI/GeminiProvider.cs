using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.AI;

public sealed partial class GeminiProvider : IGeminiProvider, IDisposable
{
    private const string ApiRoot = "https://generativelanguage.googleapis.com/v1beta/";
    /// <summary>
    /// What to try when the chosen reasoning model will not answer.
    ///
    /// Every entry was checked against the live API. The tail of this list used
    /// to be gemini-2.5-flash, gemini-2.5-pro, gemini-1.5-flash and
    /// gemini-2.0-flash, all of which Google has since withdrawn — they answer
    /// 404. A fallback chain whose lower half is dead does not degrade, it just
    /// takes longer to fail, so they are gone rather than left as padding.
    /// </summary>
    private static readonly string[] PreferredFreeFriendlyModels =
    [
        "gemini-3.7-flash",
        "gemini-3.6-flash",
        "gemini-3.5-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite"
    ];
    private static readonly byte[] DiagnosticPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] DiagnosticWav = CreateDiagnosticWave();

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public GeminiProvider(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? MetisHttp.CreateClient(TimeSpan.FromSeconds(65));
    }

    public async Task<GeminiResponse> GenerateAsync(
        string apiKey,
        string model,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        LastUsage = null;
        var normalizedModel = NormalizeModel(model);

        if (string.Equals(normalizedModel, "auto", StringComparison.OrdinalIgnoreCase))
        {
            normalizedModel = await ResolveFallbackModelAsync(apiKey, null, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var rawResponse = await SendGenerateContentAsync(
                apiKey,
                normalizedModel,
                GeminiRequestBuilder.BuildGenerateContentJson(request, normalizedModel),
                onTextDelta,
                cancellationToken).ConfigureAwait(false);
            var plan = AssistantPlanParser.Parse(
                rawResponse,
                request.ScreenshotBytes is { Length: > 0 },
                request.Prompt);
            return new GeminiResponse(plan.SpokenText, normalizedModel, plan, LastUsage);
        }
        catch (GeminiProviderException exception) when (exception.Kind == GeminiErrorKind.ModelUnavailable)
        {
            var fallback = await ResolveFallbackModelAsync(apiKey, normalizedModel, cancellationToken).ConfigureAwait(false);
            var rawResponse = await SendGenerateContentAsync(
                apiKey,
                fallback,
                GeminiRequestBuilder.BuildGenerateContentJson(request, fallback),
                onTextDelta,
                cancellationToken).ConfigureAwait(false);
            var plan = AssistantPlanParser.Parse(
                rawResponse,
                request.ScreenshotBytes is { Length: > 0 },
                request.Prompt);
            return new GeminiResponse(plan.SpokenText, fallback, plan, LastUsage);
        }
    }

    public async Task<IReadOnlyList<GeminiModelInfo>> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        var models = new List<GeminiModelInfo>();
        string? pageToken = null;

        do
        {
            var relativeUrl = "models?pageSize=100";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                relativeUrl += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = CreateRequest(HttpMethod.Get, relativeUrl, apiKey, null);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, body, null);
            }

            using var document = ParseJson(body);
            if (document.RootElement.TryGetProperty("models", out var modelArray))
            {
                foreach (var modelElement in modelArray.EnumerateArray())
                {
                    var methods = ReadStringArray(modelElement, "supportedGenerationMethods");
                    if (!methods.Contains("generateContent", StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = NormalizeModel(GetString(modelElement, "name") ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    models.Add(new GeminiModelInfo(
                        name,
                        GetString(modelElement, "displayName") ?? name,
                        methods));
                }
            }

            pageToken = GetString(document.RootElement, "nextPageToken");
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return models
            .DistinctBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ProviderTestResult> TestModelAsync(
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedModel = NormalizeModel(model);
        try
        {
            ValidateKey(apiKey);
            var payload = GeminiRequestBuilder.BuildGenerateContentJson(
                new GeminiRequest(
                    "This is a Metis connection diagnostic. Inspect the attached one-pixel image and silent WAV, then reply with exactly the word OK.",
                    DiagnosticPng,
                    DiagnosticWav,
                    "Metis model diagnostics"),
                normalizedModel);
            var text = await SendGenerateContentAsync(
                    apiKey,
                    normalizedModel,
                    payload,
                    onTextDelta: null,
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            return new ProviderTestResult(
                normalizedModel,
                true,
                $"{normalizedModel} works for text, screen, and WAV input ({stopwatch.Elapsed.TotalSeconds:0.0}s): {Shorten(text, 80)}",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(
                normalizedModel,
                false,
                exception.Message,
                stopwatch.Elapsed);
        }
    }

    private static byte[] CreateDiagnosticWave()
    {
        const int sampleRate = 16000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleCount = sampleRate / 10;
        var dataLength = sampleCount * channels * (bitsPerSample / 8);
        var wave = new byte[44 + dataLength];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wave, 0);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wave, 8);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), sampleRate * channels * (bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), (short)(channels * (bitsPerSample / 8)));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), bitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(wave, 36);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), dataLength);
        return wave;
    }

    /// <summary>
    /// The model Metis speaks with unless told otherwise. Verified against the
    /// live API: it returns 24 kHz mono PCM and is reachable on an ordinary AI
    /// Studio key.
    /// </summary>
    private const string DefaultSpeechModel = "gemini-2.5-flash-preview-tts";

    /// <summary>
    /// Every model here can actually emit audio, and that is the entire point.
    ///
    /// This list previously held gemini-2.0-flash, gemini-2.0-flash-exp and
    /// gemini-2.5-flash. None of the three can produce speech — they are text
    /// models, and asking one for responseModalities AUDIO is not a request it
    /// can satisfy. Two of them have since been withdrawn outright and answer
    /// 404. So the first attempt failed, then all three fallbacks failed, and
    /// voice was silent for every user with no way to configure around it.
    ///
    /// The rule this encodes: a fallback list for speech may only contain
    /// speech models. Ordered cheapest-and-most-available first, because the
    /// pro tier is quota-limited on a free key.
    /// </summary>
    private static readonly string[] SpeechFallbackModels =
    [
        "gemini-2.5-flash-preview-tts",
        "gemini-3.1-flash-tts-preview",
        "gemini-2.5-pro-preview-tts",
    ];

    public async Task<SpeechAudio?> SynthesizeSpeechAsync(
        string apiKey,
        string model,
        string voiceName,
        string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalizedModel = NormalizeSpeechModel(model);
        var payload = GeminiRequestBuilder.BuildSpeechJson(voiceName, text);
        
        string body;
        try
        {
            body = await SendForJsonAsync(apiKey, normalizedModel, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (GeminiProviderException ex) when (ex.Kind is GeminiErrorKind.InvalidRequest or GeminiErrorKind.EmptyResponse or GeminiErrorKind.ModelUnavailable or GeminiErrorKind.QuotaOrRateLimit or GeminiErrorKind.ServiceUnavailable)
        {
            var succeeded = false;
            string? fallbackBody = null;
            GeminiProviderException? lastException = ex;

            foreach (var fallback in SpeechFallbackModels)
            {
                if (string.Equals(fallback, normalizedModel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    normalizedModel = fallback;
                    fallbackBody = await SendForJsonAsync(apiKey, normalizedModel, payload, cancellationToken).ConfigureAwait(false);
                    succeeded = true;
                    break;
                }
                catch (GeminiProviderException fallbackEx)
                {
                    lastException = fallbackEx;
                    // Continue to next fallback
                }
            }

            if (!succeeded || fallbackBody is null)
            {
                throw lastException ?? ex;
            }

            body = fallbackBody;
        }

        using var document = ParseJson(body);
        foreach (var part in EnumerateResponseParts(document.RootElement))
        {
            if (!part.TryGetProperty("inlineData", out var inlineData) &&
                !part.TryGetProperty("inline_data", out inlineData))
            {
                continue;
            }

            var encoded = GetString(inlineData, "data");
            if (string.IsNullOrWhiteSpace(encoded))
            {
                continue;
            }

            try
            {
                var bytes = Convert.FromBase64String(encoded);
                var mimeType = GetString(inlineData, "mimeType") ?? GetString(inlineData, "mime_type") ?? "audio/L16;codec=pcm;rate=24000";
                return ParseSpeechAudio(bytes, mimeType);
            }
            catch (FormatException exception)
            {
                throw new GeminiProviderException(
                    GeminiErrorKind.EmptyResponse,
                    "Gemini returned speech data in an unreadable format.",
                    innerException: exception);
            }
        }

        // No audio in the response. This used to return null, which the caller
        // could only report as "voice was unavailable" — nothing reached the
        // log, so the voice went quiet with no way to find out why. The text
        // path has always explained itself; now this one does too.
        throw new GeminiProviderException(
            GeminiErrorKind.EmptyResponse,
            DescribeMissingSpeech(document.RootElement, normalizedModel));
    }

    /// <summary>
    /// Why a speech response carried no audio. Three causes are worth telling
    /// apart: the request was blocked, the chosen model is not a speech model
    /// at all — it answers with text, which is the usual misconfiguration —
    /// or generation stopped early.
    /// </summary>
    private static string DescribeMissingSpeech(JsonElement root, string model)
    {
        var blockReason = root.TryGetProperty("promptFeedback", out var feedback)
            ? GetString(feedback, "blockReason")
            : null;
        if (!string.IsNullOrWhiteSpace(blockReason))
        {
            return $"Gemini blocked the speech request ({blockReason}). The written answer is still on screen.";
        }

        foreach (var part in EnumerateResponseParts(root))
        {
            if (!string.IsNullOrWhiteSpace(GetString(part, "text")))
            {
                return $"'{model}' answered with text instead of audio, so it is not a speech model. " +
                    "Choose a text-to-speech model under Voice & input.";
            }
        }

        string? finishReason = null;
        if (root.TryGetProperty("candidates", out var candidates))
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                finishReason = GetString(candidate, "finishReason");
                if (!string.IsNullOrWhiteSpace(finishReason))
                {
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(finishReason) &&
            !string.Equals(finishReason, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            return $"Gemini stopped before producing any audio ({finishReason}).";
        }

        return $"Gemini returned no audio for '{model}'. Check that it is a text-to-speech model under Voice & input.";
    }

    private async Task<string> SendGenerateContentAsync(
        string apiKey,
        string model,
        string payload,
        IProgress<string>? onTextDelta,
        CancellationToken cancellationToken)
    {
        // Streaming only earns its extra handling when somebody is watching the
        // reply arrive. Diagnostics and self-tests want the whole answer and
        // nothing else, so they keep the plain path.
        if (onTextDelta is not null)
        {
            return await StreamGenerateContentAsync(apiKey, model, payload, onTextDelta, cancellationToken)
                .ConfigureAwait(false);
        }

        var body = await SendForJsonAsync(apiKey, model, payload, cancellationToken).ConfigureAwait(false);
        using var document = ParseJson(body);
        var builder = new StringBuilder();
        foreach (var part in EnumerateResponseParts(document.RootElement))
        {
            var text = GetString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(text.Trim());
            }
        }

        if (builder.Length > 0)
        {
            return builder.ToString();
        }

        var blockReason = document.RootElement.TryGetProperty("promptFeedback", out var feedback)
            ? GetString(feedback, "blockReason")
            : null;
        var suffix = string.IsNullOrWhiteSpace(blockReason) ? string.Empty : $" Block reason: {blockReason}.";
        throw new GeminiProviderException(
            GeminiErrorKind.EmptyResponse,
            $"Gemini returned no text response.{suffix} Try rephrasing the request or testing another model.");
    }

    /// <summary>
    /// The same request against the streaming endpoint, publishing the answer
    /// as it is written and returning the assembled JSON for the parser.
    ///
    /// The fragments are concatenated exactly as they arrive. The buffered path
    /// below trims each part and joins them with newlines, which is harmless
    /// for the single part a whole reply comes back as, and would quietly
    /// corrupt a JSON string that happened to be split across two frames.
    /// </summary>
    private async Task<string> StreamGenerateContentAsync(
        string apiKey,
        string model,
        string payload,
        IProgress<string> onTextDelta,
        CancellationToken cancellationToken)
    {
        ValidateModel(model);
        using var request = CreateRequest(
            HttpMethod.Post,
            $"models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse",
            apiKey,
            payload);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateApiException(response.StatusCode, errorBody, model);
        }

        var answer = new StreamingPlanText(onTextDelta);
        string? blockReason = null;
        await ReasoningProviderSupport.ReadEventStreamAsync(
            response,
            element =>
            {
                foreach (var part in EnumerateResponseParts(element))
                {
                    answer.Append(GetString(part, "text"));
                }

                if (blockReason is null && element.TryGetProperty("promptFeedback", out var feedback))
                {
                    blockReason = GetString(feedback, "blockReason");
                }

                // Usage arrives on the closing frames, so the last one wins.
                if (ReadUsage(element) is { } usage)
                {
                    LastUsage = usage;
                }
            },
            cancellationToken).ConfigureAwait(false);

        var text = answer.Raw;
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var suffix = string.IsNullOrWhiteSpace(blockReason) ? string.Empty : $" Block reason: {blockReason}.";
        throw new GeminiProviderException(
            GeminiErrorKind.EmptyResponse,
            $"Gemini returned no text response.{suffix} Try rephrasing the request or testing another model.");
    }

    private async Task<string> SendForJsonAsync(
        string apiKey,
        string model,
        string payload,
        CancellationToken cancellationToken)
    {
        ValidateModel(model);
        using var request = CreateRequest(
            HttpMethod.Post,
            $"models/{Uri.EscapeDataString(model)}:generateContent",
            apiKey,
            payload);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, model);
        }

        return body;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GeminiProviderException(
                GeminiErrorKind.Network,
                "Gemini did not respond before the request timed out. Check the connection and try again.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new GeminiProviderException(
                GeminiErrorKind.Network,
                "Metis could not reach Gemini. Check the internet connection, DNS, firewall, or proxy and try again.",
                innerException: exception);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativeUrl,
        string apiKey,
        string? json)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiRoot), relativeUrl));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<string> ResolveFallbackModelAsync(
        string apiKey,
        string? excludedModel,
        CancellationToken cancellationToken)
    {
        var available = await ListModelsAsync(apiKey, cancellationToken).ConfigureAwait(false);
        foreach (var preferred in PreferredFreeFriendlyModels)
        {
            if (string.Equals(preferred, excludedModel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (available.Any(model => string.Equals(model.Name, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return preferred;
            }
        }

        var fallback = available.FirstOrDefault(model =>
            !string.Equals(model.Name, excludedModel, StringComparison.OrdinalIgnoreCase) &&
            model.Name.Contains("flash", StringComparison.OrdinalIgnoreCase));
        fallback ??= available.FirstOrDefault(model =>
            !string.Equals(model.Name, excludedModel, StringComparison.OrdinalIgnoreCase));
        return fallback?.Name ?? throw new GeminiProviderException(
            GeminiErrorKind.ModelUnavailable,
            "This Gemini key exposes no models that support generateContent. Check the key's project, region, and API access.");
    }

    private static GeminiProviderException CreateApiException(
        HttpStatusCode statusCode,
        string body,
        string? model)
    {
        var detail = ReadErrorMessage(body);
        var status = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new GeminiProviderException(
                GeminiErrorKind.Authentication,
                "Gemini rejected the API key. Save a current Google AI Studio API key and try again.",
                status),
            HttpStatusCode.Forbidden => new GeminiProviderException(
                GeminiErrorKind.Permission,
                $"Gemini denied this request. The key, project, model, region, or free-tier access may not permit it. {detail}",
                status),
            HttpStatusCode.NotFound => new GeminiProviderException(
                GeminiErrorKind.ModelUnavailable,
                $"The Gemini model '{model ?? "requested"}' is not available to this key or API version. Use Find models in Setup. {detail}",
                status),
            HttpStatusCode.TooManyRequests => new GeminiProviderException(
                GeminiErrorKind.QuotaOrRateLimit,
                $"Gemini's free-tier quota or rate limit was reached. Wait and retry, or test another free-compatible model. {detail}",
                status),
            HttpStatusCode.BadRequest when detail.Contains("API key", StringComparison.OrdinalIgnoreCase) =>
                new GeminiProviderException(
                    GeminiErrorKind.Authentication,
                    "Gemini says the API key is invalid. Replace it in Setup and test again.",
                    status),
            HttpStatusCode.BadRequest => new GeminiProviderException(
                GeminiErrorKind.InvalidRequest,
                $"Gemini rejected the request format or model settings. {detail}",
                status),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => new GeminiProviderException(
                GeminiErrorKind.Network,
                "Gemini timed out. Check the connection and retry.",
                status),
            _ when (int)statusCode >= 500 => new GeminiProviderException(
                GeminiErrorKind.ServiceUnavailable,
                $"Gemini is temporarily unavailable (HTTP {status}). Try again shortly. {detail}",
                status),
            _ => new GeminiProviderException(
                GeminiErrorKind.Unknown,
                $"Gemini returned HTTP {status}. {detail}",
                status)
        };
    }

    /// <summary>
    /// Everything Gemini said about a failure, including the field violations.
    ///
    /// Gemini's top-level message for a malformed request is the entirely
    /// uninformative "Request contains an invalid argument."; which argument it
    /// means is in error.details[].fieldViolations[]. Reading only the message
    /// meant a schema the API had refused produced an error naming nothing, and
    /// the offending field had to be found by bisecting the schema against the
    /// live API instead of simply being read out of the reply.
    /// </summary>
    private static string ReadErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return string.Empty;
            }

            var parts = new List<string>();
            var message = GetString(error, "message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                parts.Add(message.Trim());
            }

            if (error.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("fieldViolations", out var violations) ||
                        violations.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var violation in violations.EnumerateArray())
                    {
                        var field = GetString(violation, "field");
                        var description = GetString(violation, "description");
                        var line = string.IsNullOrWhiteSpace(field)
                            ? description
                            : $"{field}: {description}";
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            parts.Add(line.Trim());
                        }
                    }
                }
            }

            return parts.Count == 0 ? string.Empty : Shorten(string.Join(" ", parts), 600);
        }
        catch (JsonException)
        {
            // A generic status-specific explanation is safer than echoing arbitrary HTML.
        }

        return string.Empty;
    }

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new GeminiProviderException(
                GeminiErrorKind.EmptyResponse,
                "Gemini returned an unreadable response. Retry or test another model.",
                innerException: exception);
        }
    }

    /// <summary>
    /// What the last generate cost, as the API reported it. Diagnostics only:
    /// one request is in flight at a time on this path, and nothing depends on
    /// it beyond a line in the log.
    /// </summary>
    internal ModelUsageReport? LastUsage { get; private set; }

    private static ModelUsageReport? ReadUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("usageMetadata", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ModelUsageReport(
            ReadInt(usage, "promptTokenCount"),
            ReadInt(usage, "thoughtsTokenCount"),
            ReadInt(usage, "candidatesTokenCount"));
    }

    private static int ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : 0;

    private static IEnumerable<JsonElement> EnumerateResponseParts(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates))
        {
            yield break;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts))
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                yield return part;
            }
        }
    }

    private static SpeechAudio ParseSpeechAudio(byte[] bytes, string mimeType) =>
        AudioPayloadParser.Parse(bytes, mimeType);

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static void ValidateKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiProviderException(
                GeminiErrorKind.Authentication,
                "No Gemini API key is saved. Open Setup and add one first.");
        }
    }

    private static string NormalizeModel(string? model)
    {
        var normalized = string.IsNullOrWhiteSpace(model) ? "auto" : model.Trim();
        if (normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["models/".Length..];
        }

        ValidateModel(normalized);
        return normalized;
    }

    /// <summary>
    /// Settles which model a speech request is actually sent to.
    ///
    /// A model that cannot emit audio is not a speech model, whatever a
    /// settings file says — so anything that is not a text-to-speech model is
    /// replaced rather than attempted. That is not second-guessing the user: it
    /// is the difference between speaking and failing, and there is no reading
    /// of "use gemini-2.0-flash for speech" under which the request could have
    /// worked.
    ///
    /// It matters for upgrades as much as for defaults. Copies of Metis in the
    /// field have a text model saved in settings.json from when this method
    /// returned one unconditionally; changing the default alone would leave
    /// every one of them silent.
    /// </summary>
    private static string NormalizeSpeechModel(string? model)
    {
        var trimmed = (model ?? string.Empty).Trim();

        if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["models/".Length..];
        }

        return IsSpeechCapable(trimmed) ? trimmed : DefaultSpeechModel;
    }

    /// <summary>
    /// Google names every text-to-speech model with "tts" in it, and names no
    /// other model that way. Matching on the name rather than a fixed list is
    /// what lets a model released after this build still be chosen.
    /// </summary>
    private static bool IsSpeechCapable(string model) =>
        model.Contains("tts", StringComparison.OrdinalIgnoreCase);

    private static void ValidateModel(string model)
    {
        if (!ModelNameRegex().IsMatch(model))
        {
            throw new GeminiProviderException(
                GeminiErrorKind.InvalidRequest,
                "The selected Gemini model name is invalid. Use Find models in Setup and select one from the list.");
        }
    }

    private static string Shorten(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + "…";

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

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelNameRegex();

    [GeneratedRegex("(?:rate=|rate:)(\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleRateRegex();
}
