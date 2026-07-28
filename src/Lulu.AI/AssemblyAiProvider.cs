using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.Core.Contracts;
using Lulu.Core.Models;

namespace Lulu.AI;

public sealed class AssemblyAiProvider : IAssemblyAiProvider, IDisposable
{
    private const string ProviderName = "AssemblyAI";
    private const string ApiRoot = "https://api.assemblyai.com/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public AssemblyAiProvider(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(70) };
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string apiKey,
        string model,
        RecordedAudio recording,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ThrowIfDisposed();
        ValidateKey(apiKey);
        if (recording.WavBytes.Length == 0)
        {
            throw Error(ExternalVoiceErrorKind.InvalidRequest, "AssemblyAI cannot transcribe an empty recording.");
        }

        var stopwatch = Stopwatch.StartNew();
        var uploadUrl = await UploadAsync(apiKey, recording.WavBytes, cancellationToken).ConfigureAwait(false);
        var transcriptId = await SubmitAsync(apiKey, uploadUrl, ParseModels(model), cancellationToken)
            .ConfigureAwait(false);
        var text = await WaitForTranscriptAsync(apiKey, transcriptId, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new TranscriptionResult(text, ProviderName, NormalizeModelLabel(model), stopwatch.Elapsed);
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
            using var request = CreateRequest(HttpMethod.Get, "v2/transcript?limit=1", apiKey);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, body);
            }

            stopwatch.Stop();
            return new ProviderTestResult(
                ProviderName,
                true,
                $"AssemblyAI is connected ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(ProviderName, false, exception.Message, stopwatch.Elapsed);
        }
    }

    private async Task<string> UploadAsync(string apiKey, byte[] wavBytes, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "v2/upload", apiKey);
        request.Content = new ByteArrayContent(wavBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        using var document = ParseJson(body);
        var uploadUrl = ReadString(document.RootElement, "upload_url");
        return !string.IsNullOrWhiteSpace(uploadUrl)
            ? uploadUrl
            : throw Error(ExternalVoiceErrorKind.EmptyResponse, "AssemblyAI accepted the audio but returned no upload URL.");
    }

    private async Task<string> SubmitAsync(
        string apiKey,
        string uploadUrl,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            audio_url = uploadUrl,
            speech_models = models,
            format_text = true,
            punctuate = true
        }, JsonOptions);
        using var request = CreateRequest(HttpMethod.Post, "v2/transcript", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body);
        }

        using var document = ParseJson(body);
        var transcriptId = ReadString(document.RootElement, "id");
        return !string.IsNullOrWhiteSpace(transcriptId)
            ? transcriptId
            : throw Error(ExternalVoiceErrorKind.EmptyResponse, "AssemblyAI accepted the request but returned no transcript ID.");
    }

    private async Task<string> WaitForTranscriptAsync(
        string apiKey,
        string transcriptId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = CreateRequest(
                HttpMethod.Get,
                $"v2/transcript/{Uri.EscapeDataString(transcriptId)}",
                apiKey);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, body);
            }

            using var document = ParseJson(body);
            var status = ReadString(document.RootElement, "status")?.ToLowerInvariant();
            if (status == "completed")
            {
                var text = ReadString(document.RootElement, "text");
                return !string.IsNullOrWhiteSpace(text)
                    ? text.Trim()
                    : throw Error(
                        ExternalVoiceErrorKind.EmptyResponse,
                        "AssemblyAI completed the transcription but detected no speech. Speak clearly and try again.");
            }

            if (status == "error")
            {
                var detail = Shorten(ReadString(document.RootElement, "error") ?? "Unknown transcription error.", 300);
                throw Error(ExternalVoiceErrorKind.InvalidRequest, $"AssemblyAI could not transcribe the recording. {detail}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);
        }

        throw Error(
            ExternalVoiceErrorKind.ServiceUnavailable,
            "AssemblyAI did not finish the transcription within 60 seconds. Try again shortly.");
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
                "AssemblyAI did not respond before the request timed out. Check the connection and try again.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw Error(
                ExternalVoiceErrorKind.Network,
                "Lulu could not reach AssemblyAI. Check the internet connection, firewall, DNS, or proxy.",
                innerException: exception);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiRoot), relativeUrl));
        request.Headers.TryAddWithoutValidation("Authorization", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Lulu-Desktop/1.0");
        return request;
    }

    private static IReadOnlyList<string> ParseModels(string? value)
    {
        var models = (value ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return models.Length > 0 ? models : ["universal-3-pro", "universal-2"];
    }

    private static string NormalizeModelLabel(string? value) => string.Join(
        ", ",
        ParseModels(value));

    private static ExternalVoiceProviderException CreateApiException(HttpStatusCode statusCode, string body)
    {
        var detail = ReadError(body);
        var status = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => Error(
                ExternalVoiceErrorKind.Authentication,
                "AssemblyAI rejected the API key. Save a current AssemblyAI key in Setup.", status),
            HttpStatusCode.Forbidden => Error(
                ExternalVoiceErrorKind.Permission,
                $"AssemblyAI denied this request. Check the key's project and account permissions. {detail}", status),
            HttpStatusCode.TooManyRequests => Error(
                ExternalVoiceErrorKind.QuotaOrRateLimit,
                $"AssemblyAI's rate limit or account quota was reached. Wait and retry. {detail}", status),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => Error(
                ExternalVoiceErrorKind.InvalidRequest,
                $"AssemblyAI rejected the recording or model settings. {detail}", status),
            _ when status >= 500 => Error(
                ExternalVoiceErrorKind.ServiceUnavailable,
                $"AssemblyAI is temporarily unavailable (HTTP {status}). Try again shortly. {detail}", status),
            _ => Error(
                ExternalVoiceErrorKind.Unknown,
                $"AssemblyAI returned HTTP {status}. {detail}", status)
        };
    }

    private static string ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var error = ReadString(root, "error") ?? ReadString(root, "message");
            return string.IsNullOrWhiteSpace(error) ? "No additional details were returned." : Shorten(error, 300);
        }
        catch (JsonException)
        {
            return "No readable error details were returned.";
        }
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
                "AssemblyAI returned a response Lulu could not read.",
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
                "No AssemblyAI API key is saved. Open Setup and add one first.");
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
