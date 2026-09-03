using System.Net;
using System.Text.Json;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.AI;

internal static class ReasoningProviderSupport
{
    // The prompt kernel moved to Metis.Core so the gateway can build the same
    // prompt this client does. These forwarders keep every provider file in this
    // assembly reading exactly as it did; nothing here decides anything any more.
    internal const int MaxInlineScreenshotBytes = AssistantPromptKernel.MaxInlineScreenshotBytes;
    internal const int MaxPlanTokens = AssistantPromptKernel.MaxPlanTokens;
    internal const string SystemInstruction = AssistantPromptKernel.SystemInstruction;

    internal static string BuildSystemInstruction(GeminiRequest request) =>
        AssistantPromptKernel.BuildSystemInstruction(request);

    internal static readonly string[] AssistantPlanPropertyOrder =
        AssistantPromptKernel.AssistantPlanPropertyOrder;

    internal static object AssistantPlanJsonSchema => AssistantPromptKernel.AssistantPlanJsonSchema;

    internal static string BuildUserPrompt(GeminiRequest request) =>
        AssistantPromptKernel.BuildUserPrompt(request);

    internal static ReasoningResponse ParsePlanResponse(
        string providerId,
        string model,
        string responseText,
        GeminiRequest request)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw Error(
                providerId,
                ReasoningProviderErrorKind.EmptyResponse,
                $"{ProviderName(providerId)} returned an empty response. Try again or choose another model.");
        }

        var plan = AssistantPlanParser.Parse(
            responseText,
            request.ScreenshotBytes is { Length: > 0 },
            request.Prompt);
        return new ReasoningResponse(plan.SpokenText, model, providerId, plan);
    }

    internal static string NormalizeImageMimeType(string? mimeType) =>
        AssistantPromptKernel.NormalizeImageMimeType(mimeType);

    internal static Uri NormalizeEndpoint(Uri? endpoint, string fallback, string apiSegment, string providerId)
    {
        var candidate = endpoint ?? new Uri(fallback, UriKind.Absolute);
        if (!candidate.IsAbsoluteUri ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment))
        {
            throw Error(
                providerId,
                ReasoningProviderErrorKind.InvalidEndpoint,
                $"{ProviderName(providerId)} needs an absolute HTTP or HTTPS endpoint without embedded credentials, query parameters, or fragments.");
        }

        if (candidate.Scheme == Uri.UriSchemeHttp && !candidate.IsLoopback)
        {
            throw Error(
                providerId,
                ReasoningProviderErrorKind.InvalidEndpoint,
                $"{ProviderName(providerId)} only permits plain HTTP on this computer. Use HTTPS for a remote endpoint.");
        }

        var builder = new UriBuilder(candidate);
        var path = builder.Path.TrimEnd('/');
        if (path.Length == 0)
        {
            path = apiSegment;
        }
        else if (!path.EndsWith(apiSegment, StringComparison.OrdinalIgnoreCase))
        {
            path += apiSegment;
        }

        builder.Path = path.Trim('/') + "/";
        return builder.Uri;
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        string providerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error(
                providerId,
                ReasoningProviderErrorKind.Network,
                $"{ProviderName(providerId)} did not respond before the request timed out. Check the service and try again.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            var guidance = providerId switch
            {
                "openclaw" => "Start the OpenClaw Gateway and enable its OpenResponses HTTP endpoint.",
                "ollama" => "Start Ollama and confirm its API is listening.",
                _ => "Check the internet connection, firewall, DNS, or proxy."
            };
            throw Error(
                providerId,
                ReasoningProviderErrorKind.Network,
                $"Metis could not reach {ProviderName(providerId)}. {guidance}",
                innerException: exception);
        }
    }

    /// <summary>
    /// Reads a streamed reply, handing each event to <paramref name="onEvent"/>
    /// as it lands.
    ///
    /// Covers both shapes Metis meets: server-sent events, where every payload
    /// arrives on a "data:" line, and the newline-delimited JSON that local
    /// runners emit instead. Treating them the same costs one <c>StartsWith</c>
    /// and saves a second reader that would do all of this again.
    ///
    /// A malformed event is skipped rather than thrown. Half of the point of
    /// streaming is that the answer is already on screen by the time anything
    /// goes wrong, and discarding it over one unreadable frame would give that
    /// back.
    /// </summary>
    internal static async Task ReadEventStreamAsync(
        HttpResponseMessage response,
        Action<JsonElement> onEvent,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            var payload = line.Trim();
            if (payload.Length == 0)
            {
                continue;
            }

            if (payload.StartsWith("data:", StringComparison.Ordinal))
            {
                payload = payload[5..].Trim();
            }
            else if (payload.StartsWith("event:", StringComparison.Ordinal) ||
                     payload.StartsWith(':'))
            {
                // Event names and comments carry no payload; the data line that
                // follows carries the type Metis actually switches on.
                continue;
            }

            if (payload.Length == 0 ||
                string.Equals(payload, "[DONE]", StringComparison.Ordinal) ||
                payload[0] is not ('{' or '['))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                onEvent(document.RootElement);
            }
        }
    }

    /// <summary>
    /// Reads a string property, or null when it is missing or another kind.
    /// Streamed events are read one frame at a time and every frame carries a
    /// different subset of fields, so absence is normal rather than an error.
    /// </summary>
    internal static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Pulls the text out of one streamed Chat Completions frame, the shape
    /// every OpenAI-compatible gateway speaks.
    /// </summary>
    internal static string? ReadChatCompletionDelta(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("delta", out var delta) &&
                ReadString(delta, "content") is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    internal static JsonDocument ParseJson(string providerId, string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw Error(
                providerId,
                ReasoningProviderErrorKind.EmptyResponse,
                $"{ProviderName(providerId)} returned a response Metis could not read.",
                innerException: exception);
        }
    }

    internal static string ReadErrorDetail(string body, string? credential = null)
    {
        var detail = "No additional details were returned.";
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var nestedMessage) &&
                    nestedMessage.ValueKind == JsonValueKind.String)
                {
                    detail = nestedMessage.GetString() ?? detail;
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    detail = error.GetString() ?? detail;
                }
            }
            else if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                detail = message.GetString() ?? detail;
            }
            else if (root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String)
            {
                detail = detailElement.GetString() ?? detail;
            }
        }
        catch (JsonException)
        {
            // Do not echo an unreadable provider body; it can contain proxy details or secrets.
        }

        detail = Shorten(detail, 300);
        return string.IsNullOrWhiteSpace(credential)
            ? detail
            : detail.Replace(credential.Trim(), "[redacted]", StringComparison.Ordinal);
    }

    internal static ReasoningProviderException Error(
        string providerId,
        ReasoningProviderErrorKind kind,
        string message,
        int? statusCode = null,
        Exception? innerException = null) =>
        new(providerId, kind, message, statusCode, innerException);

    internal static string ProviderName(string providerId) => providerId switch
    {
        "claude" => "Claude",
        "openclaw" => "OpenClaw",
        "ollama" => "Ollama",
        _ => "The reasoning provider"
    };

    internal static string Shorten(string value, int maxLength) =>
        AssistantPromptKernel.Shorten(value, maxLength);
}
