using System.Diagnostics;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace Metis.Windows;

/// <summary>
/// Speech from the synthesiser built into Windows.
///
/// Nothing to install and nothing to download: the voices are part of the
/// operating system, so this works on a fresh machine, with no key, with no
/// network, and on the first run after the installer finishes. That is what
/// makes it a usable default for offline speech where Piper is not — Piper
/// needs a separate executable and a voice model far too large to ship, so on
/// an installed copy it is simply not there.
/// </summary>
public sealed class WindowsVoiceProvider : IWindowsVoiceProvider
{
    public IReadOnlyList<SpeechVoiceInfo> ListVoices() =>
        SpeechSynthesizer.AllVoices
            .Select(voice => new SpeechVoiceInfo(
                voice.Id,
                voice.DisplayName,
                voice.Language,
                $"{voice.Gender} · {voice.Language}"))
            .ToArray();

    public async Task<SpeechAudio?> SynthesizeSpeechAsync(
        string? voiceName,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Windows ships with at least one voice, but a trimmed image or a
        // language pack that was never completed can leave none. Saying so is
        // better than returning nothing and leaving the user with silence.
        if (SpeechSynthesizer.AllVoices.Count == 0)
        {
            throw new InvalidOperationException(
                "Windows has no speech voices installed. Add one under " +
                "Settings > Time & language > Speech, or choose another voice in Metis.");
        }

        using var synthesizer = new SpeechSynthesizer();
        var voice = SelectVoice(voiceName);
        if (voice is not null)
        {
            synthesizer.Voice = voice;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await synthesizer
            .SynthesizeTextToStreamAsync(text)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var wav = await ReadAllAsync(stream, cancellationToken).ConfigureAwait(false);

        // Decoded through the same path as every other provider, so the output
        // is 16-bit PCM in the one shape the player understands.
        return WaveAudioDecoder.Decode(wav, "Windows");
    }

    public async Task<ProviderTestResult> TestAsync(
        string? voiceName,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var audio = await SynthesizeSpeechAsync(
                    voiceName,
                    "The Windows voice is ready.",
                    cancellationToken)
                .ConfigureAwait(false);
            if (audio is null || audio.PcmData.Length == 0)
            {
                throw new InvalidOperationException("Windows returned no audio.");
            }

            stopwatch.Stop();
            var spoken = SelectVoice(voiceName)?.DisplayName ?? SpeechSynthesizer.DefaultVoice.DisplayName;
            return new ProviderTestResult(
                "Windows",
                true,
                $"{spoken} is ready and needs no download ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult("Windows", false, exception.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Matches on the name shown in the picker first and the underlying id
    /// second, so a settings file written by hand works either way. An unknown
    /// name falls back to the system default rather than failing: a voice that
    /// was uninstalled should not silence Metis.
    /// </summary>
    private static VoiceInformation? SelectVoice(string? voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            return null;
        }

        var wanted = voiceName.Trim();
        return SpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                   string.Equals(voice.DisplayName, wanted, StringComparison.OrdinalIgnoreCase)) ??
               SpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                   string.Equals(voice.Id, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<byte[]> ReadAllAsync(
        SpeechSynthesisStream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[stream.Size];
        if (bytes.Length == 0)
        {
            return bytes;
        }

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken).ConfigureAwait(false);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
