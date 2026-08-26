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

    [Fact]
    public async Task SynthesizeSpeech_returns_speech_audio_from_inline_pcm()
    {
        var rawPcm = new byte[] { 0, 1, 2, 3, 4, 5 };
        var base64 = Convert.ToBase64String(rawPcm);
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse($$"""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "inlineData": {
                          "mimeType": "audio/L16;codec=pcm;rate=24000",
                          "data": "{{base64}}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """)));
        using var provider = new GeminiProvider(new HttpClient(handler));

        var result = await provider.SynthesizeSpeechAsync("fake-key", "gemini-2.5-flash-preview-tts", "Puck", "Hello world");

        Assert.NotNull(result);
        Assert.Equal(24000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.Equal(16, result.BitsPerSample);
        Assert.Equal(rawPcm, result.PcmData);
    }

    [Fact]
    public async Task SynthesizeSpeech_cascades_through_models_on_failure()
    {
        var attempts = new List<string>();
        var rawPcm = new byte[] { 10, 20, 30, 40 };
        var base64 = Convert.ToBase64String(rawPcm);

        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!.ToString();
            attempts.Add(uri);
            if (uri.Contains("gemini-2.5-flash-preview-tts:"))
            {
                return Task.FromResult(JsonResponse("{\"error\":{\"message\":\"Model not available\"}}", HttpStatusCode.NotFound));
            }

            return Task.FromResult(JsonResponse($$"""
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          {
                            "inlineData": {
                              "mimeType": "audio/pcm;rate=24000",
                              "data": "{{base64}}"
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """));
        });
        using var provider = new GeminiProvider(new HttpClient(handler));

        var result = await provider.SynthesizeSpeechAsync("fake-key", "gemini-2.5-flash-preview-tts", "Charon", "Hello");

        Assert.NotNull(result);
        Assert.Equal(2, attempts.Count);
        Assert.Contains("gemini-2.5-flash-preview-tts:", attempts[0]);
        Assert.Contains("gemini-3.1-flash-tts-preview:", attempts[1]);
        Assert.Equal(rawPcm, result.PcmData);
    }

    [Fact]
    public async Task SynthesizeSpeech_throws_descriptive_error_when_no_audio_in_response()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse("""
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "I cannot speak."
                      }
                    ]
                  }
                }
              ]
            }
            """)));
        using var provider = new GeminiProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-key", "gemini-1.5-flash", "Kore", "Hello"));

        Assert.Equal(GeminiErrorKind.EmptyResponse, exception.Kind);
        Assert.Contains("answered with text instead of audio", exception.Message);
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
