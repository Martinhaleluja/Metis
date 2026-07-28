using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.Core.Contracts;
using Lulu.Core.Models;

namespace Lulu.AI;

public sealed class OpenAiProvider : IOpenAiProvider, IDisposable
{
    private const string ApiRoot = "https://api.openai.com/v1/";
    private const string SystemInstruction = ReasoningProviderSupport.SystemInstruction;
    private static readonly byte[] DiagnosticPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public OpenAiProvider(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(75)
        };
    }

    public async Task<OpenAiResponse> GenerateAsync(
        string apiKey,
        string model,
        string transcriptionModel,
        GeminiRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        ValidateModel(model);
        ArgumentNullException.ThrowIfNull(request);

        string? transcript = null;
        if (request.RecordedAudioWav is { Length: > 0 })
        {
            transcript = await TranscribeAsync(
                apiKey,
                transcriptionModel,
                request.RecordedAudioWav,
                cancellationToken).ConfigureAwait(false);
        }

        var content = new List<object>();
        var prompt = BuildPrompt(request, transcript);
        content.Add(new { type = "input_text", text = prompt });
        if (request.ScreenshotBytes is { Length: > 0 })
        {
            content.Add(new
            {
                type = "input_image",
                image_url = $"data:{ReasoningProviderSupport.NormalizeImageMimeType(request.ScreenshotMimeType)};base64,{Convert.ToBase64String(request.ScreenshotBytes)}"
            });
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = model.Trim(),
            instructions = SystemInstruction,
            input = new[]
            {
                new
                {
                    role = "user",
                    content
                }
            },
            store = false,
            max_output_tokens = 1000,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "lulu_desktop_plan",
                    strict = true,
                    schema = ReasoningProviderSupport.AssistantPlanJsonSchema
                }
            }
        }, SerializerOptions);

        using var httpRequest = CreateJsonRequest(HttpMethod.Post, "responses", apiKey, payload);
        using var response = await SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, model);
        }

        var rawResponse = ReadResponseText(body);
        var safetyContext = string.IsNullOrWhiteSpace(transcript)
            ? request.Prompt
            : $"{request.Prompt}\n{transcript}";
        var plan = AssistantPlanParser.Parse(
            rawResponse,
            request.ScreenshotBytes is { Length: > 0 },
            safetyContext);
        return new OpenAiResponse(plan.SpokenText, model.Trim(), transcript, plan);
    }

    public async Task<IReadOnlyList<OpenAiModelInfo>> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateKey(apiKey);
        using var request = CreateRequest(HttpMethod.Get, "models", apiKey);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, null);
        }

        using var document = ParseJson(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id) && IsReasoningModel(id!))
            .Select(id => new OpenAiModelInfo(id!, FormatDisplayName(id!)))
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => ModelSortOrder(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ProviderTestResult> TestModelAsync(
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await GenerateAsync(
                apiKey,
                model,
                "gpt-4o-mini-transcribe",
                new GeminiRequest(
                    "This is a Lulu connection diagnostic. Inspect the attached one-pixel image and reply with exactly the word OK.",
                    DiagnosticPng),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new ProviderTestResult(
                model.Trim(),
                true,
                $"{result.Model} works for text and screen input ({stopwatch.Elapsed.TotalSeconds:0.0}s): {Shorten(result.Text, 80)}",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(model.Trim(), false, exception.Message, stopwatch.Elapsed);
        }
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
        ValidateModel(model);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = model.Trim(),
            input = Shorten(text.Trim(), 4000),
            voice = string.IsNullOrWhiteSpace(voiceName) ? "alloy" : voiceName.Trim().ToLowerInvariant(),
            response_format = "pcm"
        }, SerializerOptions);
        using var request = CreateJsonRequest(HttpMethod.Post, "audio/speech", apiKey, payload);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateApiException(response.StatusCode, body, model);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes.Length == 0
            ? null
            : new SpeechAudio(bytes, 24000, 1, 16, "audio/pcm;rate=24000");
    }

    private async Task<string> TranscribeAsync(
        string apiKey,
        string model,
        byte[] wavBytes,
        CancellationToken cancellationToken)
    {
        ValidateModel(model);
        using var request = CreateRequest(HttpMethod.Post, "audio/transcriptions", apiKey);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model.Trim()), "model");
        var audio = new ByteArrayContent(wavBytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "lulu-recording.wav");
        request.Content = form;

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, model);
        }

        using var document = ParseJson(body);
        var transcript = document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new OpenAiProviderException(
                OpenAiErrorKind.EmptyResponse,
                "OpenAI transcribed no speech. Hold Ctrl+Shift+1 a little longer and speak clearly.");
        }

        return transcript.Trim();
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
            throw new OpenAiProviderException(
                OpenAiErrorKind.Network,
                "OpenAI did not respond before the request timed out. Check the connection and try again.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OpenAiProviderException(
                OpenAiErrorKind.Network,
                "Lulu could not reach OpenAI. Check the internet connection, DNS, firewall, or proxy and try again.",
                innerException: exception);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiRoot), relativeUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Lulu-Desktop/1.0");
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string relativeUrl,
        string apiKey,
        string payload)
    {
        var request = CreateRequest(method, relativeUrl, apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return request;
    }

    private static string BuildPrompt(GeminiRequest request, string? transcript)
    {
        var builder = new StringBuilder(ReasoningProviderSupport.BuildUserPrompt(request));
        if (!string.IsNullOrWhiteSpace(transcript))
        {
            builder.AppendLine().AppendLine().Append("User's transcribed voice request: ").Append(transcript);
        }

        return builder.ToString();
    }

    private static string ReadResponseText(string body)
    {
        using var document = ParseJson(body);
        var builder = new StringBuilder();
        if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(textElement.GetString()))
                    {
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        builder.Append(textElement.GetString()!.Trim());
                    }
                }
            }
        }

        if (builder.Length == 0)
        {
            throw new OpenAiProviderException(
                OpenAiErrorKind.EmptyResponse,
                "OpenAI returned no text response. Try rephrasing the request or testing another model.");
        }

        return builder.ToString();
    }

    private static OpenAiProviderException CreateApiException(
        HttpStatusCode statusCode,
        string body,
        string? model)
    {
        var detail = ReadErrorMessage(body);
        var status = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new OpenAiProviderException(
                OpenAiErrorKind.Authentication,
                "OpenAI rejected the API key. Create a current Platform API key, save it in Setup, and try again.",
                status),
            HttpStatusCode.PaymentRequired => new OpenAiProviderException(
                OpenAiErrorKind.QuotaOrRateLimit,
                $"OpenAI API billing is not active or has no available balance. ChatGPT Plus does not include API usage. {detail}",
                status),
            HttpStatusCode.Forbidden => new OpenAiProviderException(
                OpenAiErrorKind.Permission,
                $"OpenAI denied this request. Check the API project's permissions and model access. {detail}",
                status),
            HttpStatusCode.NotFound => new OpenAiProviderException(
                OpenAiErrorKind.ModelUnavailable,
                $"The OpenAI model '{model ?? "requested"}' or endpoint is unavailable to this API project. Use Find models in Setup. {detail}",
                status),
            HttpStatusCode.TooManyRequests => new OpenAiProviderException(
                OpenAiErrorKind.QuotaOrRateLimit,
                $"OpenAI's API rate limit or billing quota was reached. Check Platform usage and billing, then retry. {detail}",
                status),
            HttpStatusCode.BadRequest when detail.Contains("model", StringComparison.OrdinalIgnoreCase) =>
                new OpenAiProviderException(
                    OpenAiErrorKind.ModelUnavailable,
                    $"OpenAI could not use model '{model ?? "requested"}'. Choose a compatible model in Setup. {detail}",
                    status),
            HttpStatusCode.BadRequest => new OpenAiProviderException(
                OpenAiErrorKind.InvalidRequest,
                $"OpenAI rejected Lulu's request. {detail}",
                status),
            _ when status >= 500 => new OpenAiProviderException(
                OpenAiErrorKind.ServiceUnavailable,
                $"OpenAI is temporarily unavailable (HTTP {status}). Try again shortly. {detail}",
                status),
            _ => new OpenAiProviderException(
                OpenAiErrorKind.InvalidRequest,
                $"OpenAI returned HTTP {status}. {detail}",
                status)
        };
    }

    private static string ReadErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return Shorten(message.GetString() ?? string.Empty, 300);
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(body) ? "No additional details were returned." : Shorten(body, 300);
    }

    private static bool IsReasoningModel(string id)
    {
        if (!(id.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
              id.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase) ||
              id.Equals("gpt-4o-mini", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string[] excluded = ["audio", "realtime", "transcribe", "tts", "image", "search", "chat"];
        return !excluded.Any(value => id.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static int ModelSortOrder(string id) => id.Equals("gpt-5-mini", StringComparison.OrdinalIgnoreCase) ? 0
        : id.Equals("gpt-5.6-luna", StringComparison.OrdinalIgnoreCase) ? 1
        : id.Contains("mini", StringComparison.OrdinalIgnoreCase) ? 2
        : id.Contains("nano", StringComparison.OrdinalIgnoreCase) ? 3
        : 4;

    private static string FormatDisplayName(string id) => id.Replace('-', ' ');

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new OpenAiProviderException(
                OpenAiErrorKind.EmptyResponse,
                "OpenAI returned a response Lulu could not read.",
                innerException: exception);
        }
    }

    private static string Shorten(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static void ValidateKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new OpenAiProviderException(
                OpenAiErrorKind.Authentication,
                "No OpenAI API key is saved. Open Setup and add an OpenAI Platform API key.");
        }
    }

    private static void ValidateModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new OpenAiProviderException(OpenAiErrorKind.ModelUnavailable, "Choose an OpenAI model in Setup.");
        }
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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
