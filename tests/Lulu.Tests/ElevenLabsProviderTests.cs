using System.Net;
using System.Text;
using Lulu.AI;

namespace Lulu.Tests;

public sealed class ElevenLabsProviderTests
{
    [Fact]
    public async Task Speech_requests_raw_pcm_and_uses_header_key()
    {
        const string fakeKey = "eleven-test-secret";
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(fakeKey, request.Headers.GetValues("xi-api-key").Single());
            Assert.Contains("output_format=pcm_24000", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.DoesNotContain(fakeKey, request.RequestUri.AbsoluteUri, StringComparison.Ordinal);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("eleven_flash_v2_5", body, StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
        });
        using var provider = new ElevenLabsProvider(new HttpClient(handler));

        var audio = await provider.SynthesizeSpeechAsync(
            fakeKey,
            "eleven_flash_v2_5",
            "voice-1",
            "Hello");

        Assert.NotNull(audio);
        Assert.Equal(24000, audio.SampleRate);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.Equal([1, 2, 3, 4], audio.PcmData);
    }

    [Fact]
    public async Task ListVoices_returns_sorted_voice_choices()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse("""
            {"voices":[
              {"voice_id":"b","name":"Zara","category":"premade"},
              {"voice_id":"a","name":"Adam","category":"premade"}
            ]}
            """)));
        using var provider = new ElevenLabsProvider(new HttpClient(handler));

        var voices = await provider.ListVoicesAsync("eleven-test-secret");

        Assert.Equal(["Adam", "Zara"], voices.Select(voice => voice.Name));
    }

    [Fact]
    public async Task Quota_error_is_actionable_and_hides_key()
    {
        const string fakeKey = "eleven-test-secret";
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(
            "{\"detail\":{\"message\":\"quota_exceeded\"}}",
            HttpStatusCode.TooManyRequests)));
        using var provider = new ElevenLabsProvider(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<ExternalVoiceProviderException>(() =>
            provider.SynthesizeSpeechAsync(fakeKey, "eleven_flash_v2_5", "voice-1", "Hello"));

        Assert.Equal(ExternalVoiceErrorKind.QuotaOrRateLimit, error.Kind);
        Assert.Contains("quota", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fakeKey, error.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request);
    }
}

