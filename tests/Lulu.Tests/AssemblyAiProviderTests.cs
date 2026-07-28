using System.Net;
using System.Text;
using Lulu.AI;
using Lulu.Core.Models;

namespace Lulu.Tests;

public sealed class AssemblyAiProviderTests
{
    [Fact]
    public async Task Transcribe_uploads_submits_and_polls_without_exposing_key()
    {
        const string fakeKey = "assembly-test-secret";
        var calls = 0;
        var handler = new StubHandler(async request =>
        {
            calls++;
            Assert.Equal(fakeKey, request.Headers.GetValues("Authorization").Single());
            Assert.DoesNotContain(fakeKey, request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            if (request.RequestUri.AbsolutePath.EndsWith("/v2/upload", StringComparison.Ordinal))
            {
                Assert.Equal("application/octet-stream", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal([1, 2, 3, 4], await request.Content.ReadAsByteArrayAsync());
                return JsonResponse("{\"upload_url\":\"https://cdn.example/audio\"}");
            }

            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync();
                Assert.Contains("universal-3-pro", body, StringComparison.Ordinal);
                Assert.Contains("universal-2", body, StringComparison.Ordinal);
                return JsonResponse("{\"id\":\"transcript-1\",\"status\":\"queued\"}");
            }

            return JsonResponse("{\"id\":\"transcript-1\",\"status\":\"completed\",\"text\":\"open settings\"}");
        });
        using var provider = new AssemblyAiProvider(new HttpClient(handler));

        var result = await provider.TranscribeAsync(
            fakeKey,
            "universal-3-pro, universal-2",
            new RecordedAudio([1, 2, 3, 4], TimeSpan.FromSeconds(1), "test"));

        Assert.Equal(3, calls);
        Assert.Equal("open settings", result.Text);
        Assert.Equal("AssemblyAI", result.Provider);
    }

    [Fact]
    public async Task Authentication_error_is_actionable_and_hides_key()
    {
        const string fakeKey = "assembly-test-secret";
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(
            "{\"error\":\"Invalid API key\"}",
            HttpStatusCode.Unauthorized)));
        using var provider = new AssemblyAiProvider(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<ExternalVoiceProviderException>(() => provider.TranscribeAsync(
            fakeKey,
            "universal-2",
            new RecordedAudio([1], TimeSpan.FromSeconds(1), "test")));

        Assert.Equal(ExternalVoiceErrorKind.Authentication, error.Kind);
        Assert.Contains("API key", error.Message, StringComparison.OrdinalIgnoreCase);
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

