using System.Net;
using System.Text;
using System.Text.Json;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class ClaudeReasoningProviderTests
{
    [Fact]
    public async Task Generate_uses_messages_api_header_auth_vision_and_parses_plan()
    {
        Uri? observedUri = null;
        string? observedKey = null;
        string? observedVersion = null;
        string? observedBody = null;
        var handler = new StubHandler(async (request, _) =>
        {
            observedUri = request.RequestUri;
            observedKey = request.Headers.GetValues("x-api-key").Single();
            observedVersion = request.Headers.GetValues("anthropic-version").Single();
            observedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(MessageResponse("""
                {"screen_observed":true,"spoken_text":"I found it.","bubble_cue":"Press here","scope":"control","x":250,"y":400,"element":"Settings"}
                """));
        });
        using var provider = new ClaudeReasoningProvider(new HttpClient(handler));
        const string apiKey = "sk-ant-test-secret";

        var result = await provider.GenerateAsync(
            apiKey,
            "claude-sonnet-4-5",
            new GeminiRequest(
                "Find the button",
                [1, 2, 3],
                ActiveWindowTitle: "Settings",
                ScreenshotMimeType: "image/jpeg"));

        Assert.Equal("https://api.anthropic.com/v1/messages", observedUri!.AbsoluteUri);
        Assert.Equal(apiKey, observedKey);
        Assert.Equal("2023-06-01", observedVersion);
        Assert.DoesNotContain(apiKey, observedUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"image\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"media_type\":\"image/jpeg\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("Settings", observedBody, StringComparison.Ordinal);
        Assert.Equal("I found it.", result.Text);
        Assert.Equal("Press here", result.Plan.BubbleCue);
        Assert.True(result.Plan.ScreenObserved);
        Assert.True(result.Plan.HasAnnotation);
        Assert.Equal(250, result.Plan.NormalizedX);
    }

    [Fact]
    public async Task ListModels_reads_official_models_shape_and_capabilities()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("https://api.anthropic.com/v1/models?limit=1000", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(JsonResponse("""
                {"data":[
                  {"id":"claude-sonnet-4-5","display_name":"Claude Sonnet 4.5","capabilities":{"image_input":{"supported":true}}},
                  {"id":"claude-text","display_name":"Claude Text","capabilities":{"image_input":{"supported":false}}}
                ]}
                """));
        });
        using var provider = new ClaudeReasoningProvider(new HttpClient(handler));

        var models = await provider.ListModelsAsync("sk-ant-test");

        Assert.Equal(2, models.Count);
        Assert.Equal("Claude Sonnet 4.5", models[0].DisplayName);
        Assert.True(models[0].Capabilities.HasFlag(ReasoningProviderCapabilities.Vision));
        Assert.False(models[1].Capabilities.HasFlag(ReasoningProviderCapabilities.Vision));
    }

    [Fact]
    public async Task Authentication_error_never_exposes_api_key()
    {
        const string apiKey = "sk-ant-do-not-leak";
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(
            $"{{\"error\":{{\"message\":\"bad key {apiKey}\"}}}}",
            HttpStatusCode.Unauthorized)));
        using var provider = new ClaudeReasoningProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<ReasoningProviderException>(() => provider.GenerateAsync(
            apiKey,
            "claude-sonnet-4-5",
            new GeminiRequest("Hello")));

        Assert.Equal(ReasoningProviderErrorKind.Authentication, exception.Kind);
        Assert.DoesNotContain(apiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_propagates_caller_cancellation()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        using var provider = new ClaudeReasoningProvider(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GenerateAsync(
            "sk-ant-test",
            "claude-sonnet-4-5",
            new GeminiRequest("Hello"),
            onTextDelta: null,
            cancellation.Token));
    }

    private static string MessageResponse(string text) => JsonSerializer.Serialize(new
    {
        model = "claude-sonnet-4-5",
        content = new[] { new { type = "text", text } }
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
