using System.IO;
using Metis.Core.Models;
using NAudio.Wave;

namespace Metis.Windows;

/// <summary>
/// Loads a user-supplied sound file and converts it to the PCM the playback
/// layer expects. A cue interrupts whatever is currently playing and fires on
/// every activation, so a file that is long, silent, or unreadable is rejected
/// in favour of a built-in cue rather than allowed to degrade every interaction.
/// </summary>
public static class SoundCueFile
{
    /// <summary>
    /// Cues are feedback, not content. Anything longer than this would still be
    /// playing while the user is talking or while Metis is answering.
    /// </summary>
    public const double MaxDurationSeconds = 6d;

    private const long MaxFileBytes = 20L * 1024 * 1024;

    public static SpeechAudio? TryLoad(string? path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().Trim('"');
        if (!File.Exists(trimmed))
        {
            error = $"No file exists at '{trimmed}'.";
            return null;
        }

        try
        {
            var info = new FileInfo(trimmed);
            if (info.Length > MaxFileBytes)
            {
                error = $"'{info.Name}' is larger than 20 MB, which is far bigger than a cue needs to be.";
                return null;
            }

            // AudioFileReader covers wav, mp3, aiff, and wma, and normalises all
            // of them to 32-bit float. Everything below converts that to the
            // 16-bit PCM the player takes, keeping the file's own sample rate and
            // channel count so nothing has to be resampled.
            using var reader = new AudioFileReader(trimmed);
            if (reader.TotalTime.TotalSeconds > MaxDurationSeconds)
            {
                error = $"'{info.Name}' is {reader.TotalTime.TotalSeconds:0.0}s; " +
                        $"cues must be under {MaxDurationSeconds:0}s.";
                return null;
            }

            var samples = ReadAllSamples(reader);
            if (samples.Count == 0)
            {
                error = $"'{info.Name}' contains no audio.";
                return null;
            }

            return new SpeechAudio(
                ToPcm16(samples),
                reader.WaveFormat.SampleRate,
                reader.WaveFormat.Channels,
                16,
                "audio/pcm");
        }
        catch (Exception exception)
        {
            error = $"'{Path.GetFileName(trimmed)}' could not be read as audio. {exception.Message}";
            return null;
        }
    }

    private static List<float> ReadAllSamples(AudioFileReader reader)
    {
        var samples = new List<float>((int)Math.Min(reader.Length / 4, 8_000_000));
        var buffer = new float[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                samples.Add(buffer[index]);
            }
        }

        return samples;
    }

    private static byte[] ToPcm16(List<float> samples)
    {
        var pcm = new byte[samples.Count * 2];
        for (var index = 0; index < samples.Count; index++)
        {
            // Clamping matters: decoded float audio can exceed +/-1 on material
            // that was mastered loud, and wrapping would turn that into a click.
            var value = (short)Math.Round(Math.Clamp(samples[index], -1f, 1f) * short.MaxValue);
            pcm[index * 2] = (byte)(value & 0xFF);
            pcm[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    public static double DurationSeconds(SpeechAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var bytesPerSecond = audio.SampleRate * audio.Channels * (audio.BitsPerSample / 8d);
        return bytesPerSecond <= 0 ? 0d : audio.PcmData.Length / bytesPerSecond;
    }
}
