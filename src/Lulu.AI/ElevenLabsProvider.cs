using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.Core.Contracts;
using Lulu.Core.Models;

namespace Lulu.AI;

public sealed class ElevenLabsProvider : IElevenLabsProvider, IDisposable
{
    private const string ProviderName = "ElevenLabs";
    private const string ApiRoot = "https://api.elevenlabs.io/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public ElevenLabsProvider(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    }

    public async Task<SpeechAudio?> SynthesizeSpeechAsync(
        string apiKey,
        string model,
        string voiceId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            throw Error(ExternalVoiceErrorKind.InvalidRequest, "Choose an ElevenLabs voice in Setup.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalizedModel = string.IsNullOrWhiteSpace(model) ? "eleven_flash_v2_5" : model.Trim();
        var payload = JsonSerializer.Serialize(new
        {
            text = text.Trim(),
            model_id = normalizedModel
        }, JsonOptions);
        using var request = CreateRequest(
            HttpMethod.Post,
            $"v1/text-to-speech/{Uri.EscapeDataString(voiceId.Trim())}?output_format=pcm_24000",
            apiKey);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/pcm"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateApiException(response.StatusCode, body);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes.Length == 0
            ? null
            : new SpeechAudio(bytes, 24000, 1, 16, "audio/pcm;rate=24000");
    }

    public async Task<IReadOnlyList<SpeechVoiceInfo>> ListVoicesAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        using var request = CreateRequest(
            HttpMethod.Get,
            "v2/voices?page_size=100&sort=name&sort_direction=asc&include_total_count=false",
            apiKey);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        using var document = ParseJson(body);
        if (!document.RootElement.TryGetProperty("voices", out var voices) ||
            voices.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return voices.EnumerateArray()
            .Select(voice => new SpeechVoiceInfo(
                ReadString(voice, "voice_id") ?? string.Empty,
                ReadString(voice, "name") ?? "Unnamed voice",
                ReadString(voice, "category"),
                ReadString(voice, "description")))
            .Where(voice => !string.IsNullOrWhiteSpace(voice.Id))
            .DistinctBy(voice => voice.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ProviderTestResult> TestConnectionAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ThrowIfDisposed();
            ValidateKey(apiKey);
            using var request = CreateRequest(HttpMethod.Get, "v1/user/subscription", apiKey);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, body);
            }

            using var document = ParseJson(body);
            var tier = ReadString(document.RootElement, "tier") ?? "connected account";
            stopwatch.Stop();
            return new ProviderTestResult(
                ProviderName,
                true,
                $"ElevenLabs is connected ({tier}, {stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(ProviderName, false, exception.Message, stopwatch.Elapsed);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error(
                ExternalVoiceErrorKind.Network,
                "ElevenLabs did not respond before the request timed out. Check the connection and try again.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw Error(
                ExternalVoiceErrorKind.Network,
                "Lulu could not reach ElevenLabs. Check the internet connection, firewall, DNS, or proxy.",
                innerException: exception);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiRoot), relativeUrl));
        request.Headers.TryAddWithoutValidation("xi-api-key", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Lulu-Desktop/1.0");
        return request;
    }

    private static ExternalVoiceProviderException CreateApiException(HttpStatusCode statusCode, string body)
    {
        var detail = ReadError(body);
        var status = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => Error(
                ExternalVoiceErrorKind.Authentication,
                "ElevenLabs rejected the API key. Save a current ElevenLabs key in Setup.", status),
            HttpStatusCode.Forbidden => Error(
                ExternalVoiceErrorKind.Permission,
                $"ElevenLabs denied this request. The voice or model may not be available to this account tier. {detail}", status),
            HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests => Error(
                ExternalVoiceErrorKind.QuotaOrRateLimit,
                $"ElevenLabs' character quota or rate limit was reached. Check account usage and retry. {detail}", status),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => Error(
                ExternalVoiceErrorKind.InvalidRequest,
                $"ElevenLabs rejected the selected voice, model, or text. {detail}", status),
            _ when status >= 500 => Error(
                ExternalVoiceErrorKind.ServiceUnavailable,
                $"ElevenLabs is temporarily unavailable (HTTP {status}). Try again shortly. {detail}", status),
            _ => Error(
                ExternalVoiceErrorKind.Unknown,
                $"ElevenLabs returned HTTP {status}. {detail}", status)
        };
    }

    private static string ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("detail", out var detail))
            {
                return "No additional details were returned.";
            }

            if (detail.ValueKind == JsonValueKind.String)
            {
                return Shorten(detail.GetString() ?? string.Empty, 300);
            }

            if (detail.ValueKind == JsonValueKind.Object)
            {
                var message = ReadString(detail, "message") ?? ReadString(detail, "status");
                return string.IsNullOrWhiteSpace(message) ? "No additional details were returned." : Shorten(message, 300);
            }
        }
        catch (JsonException)
        {
        }

        return "No readable error details were returned.";
    }

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw Error(
                ExternalVoiceErrorKind.EmptyResponse,
                "ElevenLabs returned a response Lulu could not read.",
                innerException: exception);
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Shorten(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static void ValidateKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw Error(
                ExternalVoiceErrorKind.Authentication,
                "No ElevenLabs API key is saved. Open Setup and add one first.");
        }
    }

    private static ExternalVoiceProviderException Error(
        ExternalVoiceErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null) =>
        new(ProviderName, kind, message, statusCode, innerException);

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
