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
    private static readonly string[] PreferredFreeFriendlyModels =
    [
        "gemini-3.5-flash",
        "gemini-3.1-flash",
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite"
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
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(65)
        };
    }

    public async Task<GeminiResponse> GenerateAsync(
        string apiKey,
        string model,
        GeminiRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
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
                GeminiRequestBuilder.BuildGenerateContentJson(request),
                cancellationToken).ConfigureAwait(false);
            var plan = AssistantPlanParser.Parse(
                rawResponse,
                request.ScreenshotBytes is { Length: > 0 },
                request.Prompt);
            return new GeminiResponse(plan.SpokenText, normalizedModel, plan);
        }
        catch (GeminiProviderException exception) when (exception.Kind == GeminiErrorKind.ModelUnavailable)
        {
            var fallback = await ResolveFallbackModelAsync(apiKey, normalizedModel, cancellationToken).ConfigureAwait(false);
            var rawResponse = await SendGenerateContentAsync(
                apiKey,
                fallback,
                GeminiRequestBuilder.BuildGenerateContentJson(request),
                cancellationToken).ConfigureAwait(false);
            var plan = AssistantPlanParser.Parse(
                rawResponse,
                request.ScreenshotBytes is { Length: > 0 },
                request.Prompt);
            return new GeminiResponse(plan.SpokenText, fallback, plan);
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
                    "Metis model diagnostics"));
            var text = await SendGenerateContentAsync(apiKey, normalizedModel, payload, cancellationToken)
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

    public async Task<SpeechAudio?> SynthesizeSpeechAsync(
        string apiKey,
        string model,
        string voiceName,
        string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        var normalizedModel = NormalizeModel(model);
        var payload = GeminiRequestBuilder.BuildSpeechJson(voiceName, text);
        var body = await SendForJsonAsync(apiKey, normalizedModel, payload, cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(body);
        foreach (var part in EnumerateResponseParts(document.RootElement))
        {
            if (!part.TryGetProperty("inlineData", out var inlineData))
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
                var mimeType = GetString(inlineData, "mimeType") ?? "audio/L16;codec=pcm;rate=24000";
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
        CancellationToken cancellationToken)
    {
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

    private static SpeechAudio ParseSpeechAudio(byte[] bytes, string mimeType)
    {
        if (bytes.Length > 44 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            var offset = 12;
            var channels = 1;
            var sampleRate = 24000;
            var bits = 16;
            while (offset + 8 <= bytes.Length)
            {
                var chunkId = bytes.AsSpan(offset, 4);
                var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
                if (chunkLength < 0 || offset + 8 + chunkLength > bytes.Length)
                {
                    break;
                }

                if (chunkId.SequenceEqual("fmt "u8) && chunkLength >= 16)
                {
                    channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 10, 2));
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 12, 4));
                    bits = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 22, 2));
                }
                else if (chunkId.SequenceEqual("data"u8))
                {
                    return new SpeechAudio(
                        bytes.AsSpan(offset + 8, chunkLength).ToArray(),
                        sampleRate,
                        channels,
                        bits,
                        mimeType);
                }

                offset += 8 + chunkLength + (chunkLength & 1);
            }
        }

        var rateMatch = SampleRateRegex().Match(mimeType);
        var rate = rateMatch.Success && int.TryParse(rateMatch.Groups[1].Value, out var parsedRate)
            ? parsedRate
            : 24000;
        return new SpeechAudio(bytes, rate, 1, 16, mimeType);
    }

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
