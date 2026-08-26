using Metis.AI;
using Metis.Core.Contracts;
using Metis.Windows;

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

        // A text model cannot speak, so the request is sent to the speech
        // default instead, and the message names what was actually asked.
        Assert.Contains("tts", error.Message, StringComparison.OrdinalIgnoreCase);
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
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-2.0-flash", "Kore", "Hello"));

        Assert.Contains("tts", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_chosen_speech_model_is_the_one_that_gets_used()
    {
        using var provider = new GeminiProvider(new HttpClient(new StubResponse("""
            {"candidates":[{"content":{"parts":[]}}]}
            """)));

        var error = await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-2.5-flash-preview-tts", "Kore", "Hello"));

        // This used to assert the opposite -- that a real speech model was
        // swapped out for a text one. That swap is exactly why nobody could
        // hear Metis, so the rule is pinned the right way round now.
        Assert.Contains("gemini-2.5-flash-preview-tts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_model_never_reaches_the_speech_endpoint()
    {
        var attempted = new List<string>();
        using var provider = new GeminiProvider(new HttpClient(new RecordingStub(attempted)));

        await Assert.ThrowsAsync<GeminiProviderException>(() =>
            provider.SynthesizeSpeechAsync("fake-secret-key", "gemini-2.0-flash", "Kore", "Hello"));

        Assert.NotEmpty(attempted);
        Assert.All(attempted, url =>
            Assert.Contains("tts", url, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CleanForSpeech_strips_markdown_code_and_urls()
    {
        var raw = "Here is how to do it:\n```python\nprint('hello')\n```\nClick **File** -> [Link](https://example.com/guide) and run `command`.";
        var cleaned = Metis.Core.Services.CompanionSpeech.CleanForSpeech(raw);

        Assert.DoesNotContain("```", cleaned);
        Assert.DoesNotContain("print('hello')", cleaned);
        Assert.DoesNotContain("**", cleaned);
        Assert.DoesNotContain("`", cleaned);
        Assert.DoesNotContain("https://", cleaned);
        Assert.Contains("Here is how to do it:", cleaned);
        Assert.Contains("Click File -> Link and run command.", cleaned);
    }

    [Theory]
    [InlineData("kore", "Kore")]
    [InlineData("PUCK", "Puck")]
    [InlineData("charon", "Charon")]
    [InlineData("fenrir", "Fenrir")]
    [InlineData("aoede", "Aoede")]
    [InlineData("", "Kore")]
    [InlineData(null, "Kore")]
    public void NormalizeVoice_handles_all_casing_and_defaults(string? input, string expected)
    {
        Assert.Equal(expected, GeminiRequestBuilder.NormalizeVoice(input));
    }

    [Fact]
    public void ModelCatalog_contains_free_gemini_speech_models_and_voices()
    {
        Assert.Contains("Kore", Metis.Core.Models.ModelCatalog.GeminiVoices);
        Assert.Contains("Puck", Metis.Core.Models.ModelCatalog.GeminiVoices);
        Assert.Contains("Charon", Metis.Core.Models.ModelCatalog.GeminiVoices);
        Assert.Contains("Fenrir", Metis.Core.Models.ModelCatalog.GeminiVoices);
        Assert.Contains("Aoede", Metis.Core.Models.ModelCatalog.GeminiVoices);

        // Every speech model offered must actually be able to speak. Google
        // names them all with "tts" and names nothing else that way.
        Assert.NotEmpty(Metis.Core.Models.ModelCatalog.GeminiSpeechModels);
        Assert.All(
            Metis.Core.Models.ModelCatalog.GeminiSpeechModels,
            m => Assert.Contains("tts", m.Id, StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            Metis.Core.Models.ModelCatalog.GeminiSpeechModels,
            m => m.Id == Metis.Core.Models.ModelCatalog.DefaultGeminiSpeechModel
                 && m.Tier == Metis.Core.Models.ModelTier.Free);
    }

    private sealed class RecordingStub(List<string> attempts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            attempts.Add(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"candidates":[{"content":{"parts":[]}}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
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

public sealed class WaveAudioPipelineTests
{
    [Fact]
    public async Task WaveAudioPlayback_handles_empty_pcm_without_error()
    {
        using var playback = new WaveAudioPlayback();
        var audio = new Metis.Core.Models.SpeechAudio([], 24000, 1, 16, "audio/pcm");
        await playback.PlayAsync(audio);
    }

    [Fact]
    public void WaveAudioPlayback_stop_and_dispose_are_idempotent()
    {
        using var playback = new WaveAudioPlayback();
        playback.Stop();
        playback.Stop();
        playback.Dispose();
        playback.Dispose();
    }

    [Fact]
    public void WaveAudioDecoder_decodes_raw_pcm_fallback()
    {
        var rawPcm = new byte[1000];
        var decoded = WaveAudioDecoder.Decode(rawPcm, "TestProvider");
        Assert.NotNull(decoded);
        Assert.Equal(16, decoded.BitsPerSample);
    }

    [Fact]
    public void WaveAudioDecoder_decodes_empty_without_throwing()
    {
        var decoded = WaveAudioDecoder.Decode([], "TestProvider");
        Assert.NotNull(decoded);
        Assert.Empty(decoded.PcmData);
    }
}

