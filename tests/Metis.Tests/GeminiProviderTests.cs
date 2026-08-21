using System.Net;
using System.Text;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class GeminiProviderTests
{
    [Fact]
    public async Task Generate_uses_secret_header_and_never_query_string()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var handler = new StubHandler(async request =>
        {
            observed = request;
            observedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""
                {"candidates":[{"content":{"parts":[{"text":"Hello"}]}}]}
                """);
        });
        using var provider = new GeminiProvider(new HttpClient(handler));
        const string fakeKey = "fake-secret-key";

        var result = await provider.GenerateAsync(
            fakeKey,
            "gemini-3.5-flash",
            new GeminiRequest("Hi", [1, 2], [3, 4]));

        Assert.Equal("Hello", result.Text);
        Assert.NotNull(observed);
        Assert.True(observed.Headers.TryGetValues("x-goog-api-key", out var values));
        Assert.Equal(fakeKey, Assert.Single(values));
        Assert.DoesNotContain(fakeKey, observed.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("inlineData", observedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_returns_spoken_text_and_parsed_plan()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse("""
            {"candidates":[{"content":{"parts":[{"text":"{\"screen_observed\":true,\"spoken_text\":\"I found it.\",\"bubble_cue\":\"Press here\",\"scope\":\"control\",\"x\":700,\"y\":200,\"element\":\"Settings\"}"}]}}]}
            """)));
        using var provider = new GeminiProvider(new HttpClient(handler));

        var result = await provider.GenerateAsync(
            "fake-secret-key",
            "gemini-3.5-flash",
            new GeminiRequest("Where is Settings?", ScreenshotBytes: [1]));

        Assert.Equal("I found it.", result.Text);
        Assert.NotNull(result.Plan);
        Assert.Equal("Press here", result.Plan.BubbleCue);
        Assert.True(result.Plan.ScreenObserved);
        Assert.True(result.Plan.HasAnnotation);
        Assert.Equal(700, result.Plan.NormalizedX);
    }

    [Fact]
    public async Task Model_discovery_only_returns_generate_content_models()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse("""
            {
              "models": [
                {"name":"models/gemini-3.5-flash","displayName":"Flash","supportedGenerationMethods":["generateContent"]},
                {"name":"models/embedding-001","displayName":"Embedding","supportedGenerationMethods":["embedContent"]}
              ]
            }
            """)));
        using var provider = new GeminiProvider(new HttpClient(handler));

        var models = await provider.ListModelsAsync("fake-secret-key");

        var model = Assert.Single(models);
        Assert.Equal("gemini-3.5-flash", model.Name);
    }

    [Fact]
    public async Task Quota_error_is_human_readable_and_does_not_include_key()
    {
        const string fakeKey = "fake-secret-key";
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(
            "{\"error\":{\"message\":\"Quota exceeded\"}}",
            HttpStatusCode.TooManyRequests)));
        using var provider = new GeminiProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<GeminiProviderException>(() => provider.GenerateAsync(
            fakeKey,
            "gemini-3.5-flash",
            new GeminiRequest("Hi")));

        Assert.Equal(GeminiErrorKind.QuotaOrRateLimit, exception.Kind);
        Assert.Contains("free-tier quota", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fakeKey, exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request);
    }
}
