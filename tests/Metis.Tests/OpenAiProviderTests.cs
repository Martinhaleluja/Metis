using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class OpenAiProviderTests
{
    [Fact]
    public async Task Generate_uses_bearer_header_and_sends_private_vision_request()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var handler = new StubHandler(async request =>
        {
            observed = request;
            observedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(ResponseBody("Hello from OpenAI"));
        });
        using var provider = new OpenAiProvider(new HttpClient(handler));
        const string fakeKey = "sk-test-secret";

        var result = await provider.GenerateAsync(
            fakeKey,
            "gpt-5-mini",
            "gpt-4o-mini-transcribe",
            new GeminiRequest("Hi", [1, 2, 3], ScreenshotMimeType: "image/jpeg"));

        Assert.Equal("Hello from OpenAI", result.Text);
        Assert.NotNull(observed);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", fakeKey), observed.Headers.Authorization);
        Assert.DoesNotContain(fakeKey, observed.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("input_image", observedBody, StringComparison.Ordinal);
        Assert.Contains("data:image/jpeg;base64", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"store\":false", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"json_schema\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"maxItems\":6", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"screen_observed\"", observedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_returns_spoken_text_and_parsed_plan()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(ResponseBody("""
            {"screen_observed":true,"spoken_text":"The control is at the top.","bubble_cue":"Press here","actions":[{"type":"move_pointer","x":450,"y":80}]}
            """))));
        using var provider = new OpenAiProvider(new HttpClient(handler));

        var result = await provider.GenerateAsync(
            "sk-test-secret",
            "gpt-5-mini",
            "gpt-4o-mini-transcribe",
            new GeminiRequest("Where is the control?", ScreenshotBytes: [1]));

        Assert.Equal("The control is at the top.", result.Text);
        Assert.NotNull(result.Plan);
        Assert.Equal("Press here", result.Plan.BubbleCue);
        Assert.True(result.Plan.ScreenObserved);
        Assert.Equal(DesktopActionKind.MovePointer, Assert.Single(result.Plan.Actions).Kind);
    }

    [Fact]
    public async Task Voice_request_is_transcribed_before_reasoning_request()
    {
        var call = 0;
        var handler = new StubHandler(async request =>
        {
            call++;
            if (call == 1)
            {
                Assert.EndsWith("/audio/transcriptions", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
                Assert.IsType<MultipartFormDataContent>(request.Content);
                return JsonResponse("{\"text\":\"open the calendar\"}");
            }

            Assert.EndsWith("/responses", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("open the calendar", body, StringComparison.Ordinal);
            return JsonResponse(ResponseBody("I can help with that."));
        });
        using var provider = new OpenAiProvider(new HttpClient(handler));

        var result = await provider.GenerateAsync(
            "sk-test-secret",
            "gpt-5-mini",
            "gpt-4o-mini-transcribe",
            new GeminiRequest("Answer the voice request", RecordedAudioWav: [1, 2, 3, 4]));

        Assert.Equal(2, call);
        Assert.Equal("open the calendar", result.Transcript);
    }

    [Fact]
    public async Task Speech_returns_raw_24khz_pcm_for_existing_player()
    {
        var handler = new StubHandler(request =>
        {
            Assert.EndsWith("/audio/speech", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            });
        });
        using var provider = new OpenAiProvider(new HttpClient(handler));

        var audio = await provider.SynthesizeSpeechAsync(
            "sk-test-secret",
            "tts-1",
            "alloy",
            "Hello");

        Assert.NotNull(audio);
        Assert.Equal(24000, audio.SampleRate);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.Equal([1, 2, 3, 4], audio.PcmData);
    }

    [Fact]
    public async Task Billing_or_rate_error_is_actionable_and_does_not_expose_key()
    {
        const string fakeKey = "sk-test-secret";
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(
            "{\"error\":{\"message\":\"insufficient_quota\"}}",
            HttpStatusCode.TooManyRequests)));
        using var provider = new OpenAiProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<OpenAiProviderException>(() => provider.GenerateAsync(
            fakeKey,
            "gpt-5-mini",
            "gpt-4o-mini-transcribe",
            new GeminiRequest("Hi")));

        Assert.Equal(OpenAiErrorKind.QuotaOrRateLimit, exception.Kind);
        Assert.Contains("billing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fakeKey, exception.Message, StringComparison.Ordinal);
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
