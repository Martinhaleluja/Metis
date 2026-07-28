using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.AI;
using Lulu.Core.Models;

namespace Lulu.Tests;

public sealed class OllamaReasoningProviderTests
{
    [Fact]
    public async Task Generate_uses_native_chat_vision_schema_and_parses_plan()
    {
        Uri? observedUri = null;
        AuthenticationHeaderValue? observedAuth = null;
        string? observedBody = null;
        var handler = new StubHandler(async (request, _) =>
        {
            observedUri = request.RequestUri;
            observedAuth = request.Headers.Authorization;
            observedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(ChatResponse("gemma3", """
                {"screen_observed":true,"spoken_text":"I see it.","bubble_cue":"Press here","actions":[{"type":"move_pointer","x":300,"y":600}]}
                """));
        });
        using var provider = new OllamaReasoningProvider(
            new HttpClient(handler),
            new Uri("https://ollama.example.test/lulu"));
        const string token = "ollama-cloud-secret";

        var result = await provider.GenerateAsync(
            token,
            "gemma3",
            new GeminiRequest("Find it", [1, 2, 3]));

        Assert.Equal("https://ollama.example.test/lulu/api/chat", observedUri!.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", token), observedAuth);
        Assert.DoesNotContain(token, observedUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("\"images\":[\"AQID\"]", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"format\":{\"type\":\"object\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"stream\":false", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"think\":true", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"num_ctx\":4096", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"num_predict\":700", observedBody, StringComparison.Ordinal);
        Assert.Equal("I see it.", result.Text);
        Assert.Equal("Press here", result.Plan.BubbleCue);
        Assert.True(result.Plan.ScreenObserved);
    }

    [Fact]
    public async Task ListModels_uses_tags_and_formats_local_model_details()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("http://127.0.0.1:11434/api/tags", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse("""
                {"models":[
                  {"name":"gemma3:4b","details":{"parameter_size":"4.3B","quantization_level":"Q4_K_M"}},
                  {"model":"qwen3:latest","details":{"parameter_size":"8B"}}
                ]}
                """));
        });
        using var provider = new OllamaReasoningProvider(new HttpClient(handler));

        var models = await provider.ListModelsAsync(null);

        Assert.Equal(2, models.Count);
        Assert.Contains(models, item => item.Name == "gemma3:4b" && item.DisplayName.Contains("4.3B", StringComparison.Ordinal));
        Assert.Contains(models, item => item.Name == "qwen3:latest" && item.DisplayName.Contains("8B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_retries_without_thinking_when_model_rejects_it()
    {
        var requestBodies = new List<string>();
        var handler = new StubHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            requestBodies.Add(body);
            return requestBodies.Count == 1
                ? JsonResponse("""{"error":"qwen3-vl:2b-instruct-q4_K_M does not support thinking"}""", HttpStatusCode.BadRequest)
                : JsonResponse(ChatResponse("qwen3-vl:2b-instruct-q4_K_M", """
                    {"screen_observed":false,"spoken_text":"OK","bubble_cue":null,"actions":[]}
                    """));
        });
        using var provider = new OllamaReasoningProvider(new HttpClient(handler), enableThinking: true);

        var result = await provider.GenerateAsync(
            null,
            "qwen3-vl:2b-instruct-q4_K_M",
            new GeminiRequest("Hello"));

        Assert.Equal(2, requestBodies.Count);
        Assert.Contains("\"think\":true", requestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"think\":false", requestBodies[1], StringComparison.Ordinal);
        Assert.Equal("OK", result.Text);
    }

    [Fact]
    public async Task Generate_can_disable_thinking_for_instruct_models()
    {
        string? observedBody = null;
        var handler = new StubHandler(async (request, _) =>
        {
            observedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(ChatResponse("qwen3-vl:2b-instruct-q4_K_M", """
                {"screen_observed":false,"spoken_text":"OK","bubble_cue":null,"actions":[]}
                """));
        });
        using var provider = new OllamaReasoningProvider(new HttpClient(handler), enableThinking: false);

        await provider.GenerateAsync(
            null,
            "qwen3-vl:2b-instruct-q4_K_M",
            new GeminiRequest("Hello"));

        Assert.Contains("\"think\":false", observedBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_allows_loopback_http_but_requires_https_remotely()
    {
        using var local = new OllamaReasoningProvider(endpoint: new Uri("http://localhost:11434"));
        Assert.Equal("http://localhost:11434/api/", local.Endpoint.AbsoluteUri);

        var exception = Assert.Throws<ReasoningProviderException>(() =>
            new OllamaReasoningProvider(endpoint: new Uri("http://192.0.2.8:11434")));
        Assert.Equal(ReasoningProviderErrorKind.InvalidEndpoint, exception.Kind);
        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_model_error_is_actionable_and_redacts_optional_token()
    {
        const string token = "ollama-do-not-leak";
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(
            $"{{\"error\":\"model not found; token {token}\"}}",
            HttpStatusCode.NotFound)));
        using var provider = new OllamaReasoningProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<ReasoningProviderException>(() => provider.GenerateAsync(
            token,
            "missing-model",
            new GeminiRequest("Hello")));

        Assert.Equal(ReasoningProviderErrorKind.ModelUnavailable, exception.Kind);
        Assert.Contains("Pull it", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
    }

    private static string ChatResponse(string model, string text) => JsonSerializer.Serialize(new
    {
        model,
        message = new { role = "assistant", content = text },
        done = true
    });

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
}
