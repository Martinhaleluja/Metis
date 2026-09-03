using Metis.Core.Models;
using Metis.Windows;

namespace Metis.Tests;

/// <summary>
/// The voice that ships with Windows. Piper is offline too, but it is a
/// separate executable plus a voice model far too large for the installer to
/// carry, so on an installed copy it is simply absent — which reads as the
/// voice being broken rather than missing. These tests use the real system
/// speech stack, and skip rather than fail on an image that has no voices at
/// all, which is a legitimate if unusual state.
/// </summary>
public sealed class WindowsVoiceProviderTests
{
    private static bool HasVoices => new WindowsVoiceProvider().ListVoices().Count > 0;

    [Fact]
    public void The_voices_already_on_this_machine_are_listed()
    {
        var voices = new WindowsVoiceProvider().ListVoices();

        Assert.All(voices, voice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(voice.Id));
            Assert.False(string.IsNullOrWhiteSpace(voice.Name));
        });
    }

    [Fact]
    public async Task Speaking_produces_playable_sixteen_bit_audio()
    {
        if (!HasVoices)
        {
            return;
        }

        var audio = await new WindowsVoiceProvider().SynthesizeSpeechAsync(null, "Metis can speak offline.");

        Assert.NotNull(audio);
        Assert.NotEmpty(audio.PcmData);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.True(audio.SampleRate > 0);
        Assert.True(audio.Channels > 0);
    }

    /// <summary>
    /// A voice that was uninstalled, or a name typed by hand, must not silence
    /// Metis — the whole point of this provider is that it is the one that
    /// always works.
    /// </summary>
    [Fact]
    public async Task An_unknown_voice_falls_back_instead_of_failing()
    {
        if (!HasVoices)
        {
            return;
        }

        var audio = await new WindowsVoiceProvider()
            .SynthesizeSpeechAsync("A Voice That Is Not Installed", "Still speaking.");

        Assert.NotNull(audio);
        Assert.NotEmpty(audio.PcmData);
    }

    [Fact]
    public async Task Nothing_to_say_produces_nothing() =>
        Assert.Null(await new WindowsVoiceProvider().SynthesizeSpeechAsync(null, "   "));

    [Fact]
    public async Task The_test_button_reports_success_and_names_the_voice()
    {
        if (!HasVoices)
        {
            return;
        }

        var result = await new WindowsVoiceProvider().TestAsync(null);

        Assert.True(result.Success, result.Message);
        Assert.Equal("Windows", result.Model);
        Assert.Contains("no download", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The stored setting has to survive a round trip, or the choice silently
    /// reverts to a cloud voice on the next launch.
    /// </summary>
    [Theory]
    [InlineData("Windows", "Native")]
    [InlineData("windows", "Native")]
    [InlineData("system", "Native")]
    [InlineData("Piper", "Piper")]
    [InlineData("nonsense", "Native")]
    public void The_provider_name_normalizes(string stored, string expected) =>
        Assert.Equal(expected, new AppSettings { TextToSpeechProvider = stored }.Normalize().TextToSpeechProvider);
}
