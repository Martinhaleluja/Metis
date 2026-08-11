using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.AI;

/// <summary>
/// OpenClaw Gateway provider using its OpenResponses-compatible HTTP surface.
/// OpenClaw plans work; Metis remains the only component that executes desktop input.
/// </summary>
public sealed class OpenClawReasoningProvider : IReasoningProvider, IDisposable
{
    private const string ProviderId = "openclaw";
    private const string DefaultEndpoint = "http://127.0.0.1:18789";
    private static readonly byte[] DiagnosticPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public OpenClawReasoningProvider(HttpClient? httpClient = null, Uri? endpoint = null)
    {
        Endpoint = ReasoningProviderSupport.NormalizeEndpoint(
            endpoint,
            DefaultEndpoint,
            "/v1",
            ProviderId);
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    public ReasoningProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "OpenClaw Gateway",
        ReasoningAuthenticationKind.OptionalBearerToken,
        ReasoningProviderCapabilities.Text |
        ReasoningProviderCapabilities.Vision |
        ReasoningProviderCapabilities.ModelDiscovery |
        ReasoningProviderCapabilities.StructuredPlans |
        ReasoningProviderCapabilities.LocalEndpoint |
        ReasoningProviderCapabilities.RemoteEndpoint |
        ReasoningProviderCapabilities.AgentGateway);

    public Uri Endpoint { get; }

    public async Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedModel = NormalizeModel(model);
        var prompt = ReasoningProviderSupport.BuildUserPrompt(request);
        var content = new List<object> { new { type = "input_text", text = prompt } };
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
            model = normalizedModel,
            instructions = ReasoningProviderSupport.BuildSystemInstruction(request),
            input = new[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content
                }
            },
            stream = false,
            store = false,
            max_output_tokens = 1000,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "metis_desktop_plan",
                    strict = true,
                    schema = ReasoningProviderSupport.AssistantPlanJsonSchema
                }
            }
        }, JsonOptions);

        using var httpRequest = CreateRequest(HttpMethod.Post, "responses", credential);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await ReasoningProviderSupport.SendAsync(
                _httpClient,
                httpRequest,
                ProviderId,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, body, normalizedModel, credential);
        }

        var text = ReadResponseText(body);
        return ReasoningProviderSupport.ParsePlanResponse(ProviderId, normalizedModel, text, request);
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
        return data.EnumerateArray()
            .Select(item => new
            {
                Id = ReadString(item, "id"),
                DisplayName = ReadString(item, "display_name") ?? ReadString(item, "name")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => new ReasoningModelInfo(
                item.Id!,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id! : item.DisplayName!,
                capabilities))
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
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            return new ProviderTestResult(
                result.Model,
                true,
                $"OpenClaw agent {result.Model} is connected ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult(
                string.IsNullOrWhiteSpace(model) ? "openclaw" : model.Trim(),
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

        request.Headers.TryAddWithoutValidation("x-openclaw-agent-id", "main");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Metis-Desktop/1.0");
        return request;
    }

    private static string ReadResponseText(string body)
    {
        using var document = ReasoningProviderSupport.ParseJson(ProviderId, body);
        var root = document.RootElement;
        if (root.TryGetProperty("output_text", out var directText) && directText.ValueKind == JsonValueKind.String)
        {
            return directText.GetString()?.Trim() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var text = new List<string>();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (string.Equals(ReadString(outputItem, "type"), "output_text", StringComparison.OrdinalIgnoreCase))
            {
                var value = ReadString(outputItem, "text");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    text.Add(value);
                }
            }

            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            text.AddRange(content.EnumerateArray()
                .Where(item => string.Equals(ReadString(item, "type"), "output_text", StringComparison.OrdinalIgnoreCase))
                .Select(item => ReadString(item, "text"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!));
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
                "OpenClaw rejected the Gateway token or password. Save the same secret configured by gateway.auth.",
                status),
            HttpStatusCode.Forbidden => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Permission,
                $"OpenClaw denied this agent run. Check the Gateway operator scopes and agent permissions. {detail}",
                status),
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                "OpenClaw's OpenResponses endpoint is not available. Enable gateway.http.endpoints.responses and restart the Gateway.",
                status),
            HttpStatusCode.TooManyRequests => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.QuotaOrRateLimit,
                $"OpenClaw is rate-limiting requests or failed authentication attempts. Wait before retrying. {detail}",
                status),
            HttpStatusCode.BadRequest => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.InvalidRequest,
                $"OpenClaw rejected Metis's request or agent model '{model ?? "requested"}'. {detail}",
                status),
            _ when status >= 500 => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.ServiceUnavailable,
                $"OpenClaw could not complete the agent run (HTTP {status}). Check the Gateway and its configured model provider. {detail}",
                status),
            _ => ReasoningProviderSupport.Error(
                ProviderId,
                ReasoningProviderErrorKind.Unknown,
                $"OpenClaw returned HTTP {status}. {detail}",
                status)
        };
    }

    private static string NormalizeModel(string? model) =>
        string.IsNullOrWhiteSpace(model) || string.Equals(model.Trim(), "default", StringComparison.OrdinalIgnoreCase)
            ? "openclaw"
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
