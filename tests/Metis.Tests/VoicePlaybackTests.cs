using Metis.AI;
using Metis.Core.Contracts;

namespace Metis.Tests;

/// <summary>
/// Metis has one output device, so starting a sound stops whatever was already
/// playing. That was unconditional, which meant a keypress cue could cut a
/// sentence short — and because the interrupted caller was handed a completed
/// task, nothing was logged and the voice simply went quiet for no reason.
/// </summary>
public sealed class AudioArbitrationTests
{
    [Fact]
    public void A_cue_is_dropped_rather_than_cutting_speech_short() =>
        Assert.True(AudioArbitration.ShouldDrop(AudioPriority.Cue, AudioPriority.Speech, isPlaying: true));

    [Fact]
    public void Speech_takes_the_device_from_a_cue() =>
        Assert.False(AudioArbitration.ShouldDrop(AudioPriority.Speech, AudioPriority.Cue, isPlaying: true));

    /// <summary>
    /// A spoken error replacing the reply being spoken is the intended
    /// behaviour: the newer sentence is the one worth hearing.
    /// </summary>
    [Fact]
    public void Speech_replaces_speech() =>
        Assert.False(AudioArbitration.ShouldDrop(AudioPriority.Speech, AudioPriority.Speech, isPlaying: true));

    [Fact]
    public void A_cue_replaces_a_cue() =>
        Assert.False(AudioArbitration.ShouldDrop(AudioPriority.Cue, AudioPriority.Cue, isPlaying: true));

    /// <summary>
    /// With nothing playing there is nothing to yield to, so a cue must not be
    /// swallowed just because speech happened to play earlier.
    /// </summary>
    [Theory]
    [InlineData(AudioPriority.Cue)]
    [InlineData(AudioPriority.Speech)]
    public void Silence_never_drops_anything(AudioPriority incoming) =>
        Assert.False(AudioArbitration.ShouldDrop(incoming, AudioPriority.Speech, isPlaying: false));
}

/// <summary>
/// A speech response carrying no audio used to return null, which the caller
/// could only turn into "voice was unavailable" with nothing written to the
/// log. Each of these is a real way for that to happen, and each now says so.
/// </summary>
public sealed class GeminiSpeechFailureTests
{
    [Fact]
    public async Task Text_instead_of_audio_names_the_misconfigured_model()
    {
        using var provider = new GeminiProvider(new HttpClient(new StubResponse("""
            {"candidates":[{"content":{"parts":[{"text":"Sure, here you go."}]}}]}
            """)));

        var error = await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-3.5-flash", "Kore", "Hello"));

        Assert.Contains("gemini-3.5-flash", error.Message, StringComparison.Ordinal);
        Assert.Contains("not a speech model", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blocked_request_says_it_was_blocked()
    {
        using var provider = new GeminiProvider(new HttpClient(new StubResponse("""
            {"promptFeedback":{"blockReason":"SAFETY"},"candidates":[]}
            """)));

        var error = await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-2.5-flash-preview-tts", "Kore", "Hello"));

        Assert.Contains("SAFETY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_truncated_generation_says_why_it_stopped()
    {
        using var provider = new GeminiProvider(new HttpClient(new StubResponse("""
            {"candidates":[{"finishReason":"MAX_TOKENS","content":{"parts":[]}}]}
            """)));

        var error = await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-2.5-flash-preview-tts", "Kore", "Hello"));

        Assert.Contains("MAX_TOKENS", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty response still has to explain itself.</summary>
    [Fact]
    public async Task Nothing_at_all_still_names_the_model()
    {
        using var provider = new GeminiProvider(new HttpClient(new StubResponse("""
            {"candidates":[{"content":{"parts":[]}}]}
            """)));

        var error = await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-2.5-flash-preview-tts", "Kore", "Hello"));

        Assert.Contains("gemini-2.5-flash-preview-tts", error.Message, StringComparison.Ordinal);
    }

    private sealed class StubResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
