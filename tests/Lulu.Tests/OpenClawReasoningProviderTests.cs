using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.AI;
using Lulu.Core.Models;

namespace Lulu.Tests;

public sealed class OpenClawReasoningProviderTests
{
    [Fact]
    public async Task Generate_normalizes_gateway_root_uses_bearer_and_openresponses_shape()
    {
        Uri? observedUri = null;
        AuthenticationHeaderValue? observedAuth = null;
        string? observedBody = null;
        var handler = new StubHandler(async (request, _) =>
        {
            observedUri = request.RequestUri;
            observedAuth = request.Headers.Authorization;
            observedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(ResponseBody("""
                {"screen_observed":true,"spoken_text":"Moving there.","bubble_cue":null,"actions":[{"type":"move_pointer","x":700,"y":200}]}
                """));
        });
        using var provider = new OpenClawReasoningProvider(
            new HttpClient(handler),
            new Uri("http://127.0.0.1:18789"));
        const string token = "gateway-test-secret";

        var result = await provider.GenerateAsync(
            token,
            "default",
            new GeminiRequest("Point to it", [9, 8, 7], ScreenshotMimeType: "image/jpeg"));

        Assert.Equal("http://127.0.0.1:18789/v1/responses", observedUri!.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", token), observedAuth);
        Assert.DoesNotContain(token, observedUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"openclaw\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"input_image\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("data:image/jpeg;base64", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"json_schema\"", observedBody, StringComparison.Ordinal);
        Assert.Equal("openclaw", result.Model);
        Assert.Equal(DesktopActionKind.MovePointer, Assert.Single(result.Plan.Actions).Kind);
    }

    [Fact]
    public async Task ListModels_uses_gateway_compatibility_endpoint_without_requiring_token()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("http://localhost:18789/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse("""
                {"data":[{"id":"openclaw","display_name":"Main agent"},{"id":"research","name":"Research agent"}]}
                """));
        });
        using var provider = new OpenClawReasoningProvider(
            new HttpClient(handler),
            new Uri("http://localhost:18789"));

        var models = await provider.ListModelsAsync(null);

        Assert.Equal(2, models.Count);
        Assert.Equal("Main agent", models[0].DisplayName);
        Assert.Equal("Research agent", models[1].DisplayName);
    }

    [Fact]
    public void Constructor_rejects_insecure_remote_or_secret_bearing_endpoint()
    {
        var insecure = Assert.Throws<ReasoningProviderException>(() =>
            new OpenClawReasoningProvider(endpoint: new Uri("http://example.com:18789")));
        Assert.Equal(ReasoningProviderErrorKind.InvalidEndpoint, insecure.Kind);

        var secretBearing = Assert.Throws<ReasoningProviderException>(() =>
            new OpenClawReasoningProvider(endpoint: new Uri("https://example.com?token=do-not-put-secrets-here")));
        Assert.Equal(ReasoningProviderErrorKind.InvalidEndpoint, secretBearing.Kind);
        Assert.DoesNotContain("do-not-put-secrets-here", secretBearing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_error_is_actionable_and_redacts_token()
    {
        const string token = "gateway-do-not-leak";
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(
            $"{{\"error\":{{\"message\":\"upstream echoed {token}\"}}}}",
            HttpStatusCode.BadGateway)));
        using var provider = new OpenClawReasoningProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<ReasoningProviderException>(() => provider.GenerateAsync(
            token,
            "openclaw",
            new GeminiRequest("Hello")));

        Assert.Equal(ReasoningProviderErrorKind.ServiceUnavailable, exception.Kind);
        Assert.Contains("Gateway", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_timeout_returns_network_guidance()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("simulated timeout"));
        using var provider = new OpenClawReasoningProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<ReasoningProviderException>(() => provider.GenerateAsync(
            null,
            "openclaw",
            new GeminiRequest("Hello")));

        Assert.Equal(ReasoningProviderErrorKind.Network, exception.Kind);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResponseBody(string text) => JsonSerializer.Serialize(new
    {
        output = new[]
        {
            new
            {
                type = "message",
                content = new[] { new { type = "output_text", text } }
            }
        }
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
