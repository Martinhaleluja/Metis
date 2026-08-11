using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Generates Metis's short interaction cues as raw PCM. They are synthesised
/// rather than shipped as audio files so there is nothing to install, lose, or
/// resolve a path to — the same reason the tray icon is drawn in code.
/// </summary>
public static class SoundCueFactory
{
    public const int SampleRate = 24_000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    /// <summary>
    /// A short rising blip for the moment Metis starts listening. Kept under a
    /// tenth of a second so it reads as a click rather than a tone, and quiet
    /// enough that the microphone opening right behind it barely picks it up.
    /// </summary>
    public static SpeechAudio Pop()
    {
        const double duration = 0.075;
        const double amplitude = 0.32;
        var samples = new double[(int)(SampleRate * duration)];
        var phase = 0d;

        for (var index = 0; index < samples.Length; index++)
        {
            var position = index / (double)samples.Length;
            var seconds = index / (double)SampleRate;

            // Sweeping upward reads as "opening". Advancing the phase by the
            // instantaneous frequency avoids the click a naive sin(2*pi*f*t)
            // sweep produces when the frequency changes.
            var frequency = 520d + (660d * position);
            phase += 2 * Math.PI * frequency / SampleRate;

            var attack = Math.Min(1d, seconds / 0.003);
            var decay = Math.Exp(-seconds / 0.019);
            samples[index] = Math.Sin(phase) * attack * decay * amplitude;
        }

        return ToAudio(samples);
    }

    /// <summary>
    /// A soft filtered-noise sweep for the moment the request leaves for the
    /// provider. Deterministic, so it sounds identical every time.
    /// </summary>
    public static SpeechAudio Woosh()
    {
        const double duration = 0.26;
        const double amplitude = 0.26;
        var samples = new double[(int)(SampleRate * duration)];
        var random = new Random(20260811);
        var lowPass = 0d;
        var highPass = 0d;

        for (var index = 0; index < samples.Length; index++)
        {
            var position = index / (double)samples.Length;
            var noise = (random.NextDouble() * 2d) - 1d;

            // One rise and fall of the cutoff is what makes it read as movement
            // past the listener rather than as plain hiss.
            var cutoff = 380d + (3_100d * Math.Sin(Math.PI * position));
            var alpha = 1d - Math.Exp(-2 * Math.PI * cutoff / SampleRate);
            lowPass += alpha * (noise - lowPass);

            // Removing the lowest rumble keeps it from sounding like a thud on
            // laptop speakers.
            highPass += 0.004 * (lowPass - highPass);
            var band = lowPass - highPass;

            var envelope = Math.Pow(Math.Sin(Math.PI * position), 1.6);
            samples[index] = band * envelope * amplitude;
        }

        return ToAudio(samples);
    }

    private static SpeechAudio ToAudio(double[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            var clamped = Math.Clamp(samples[index], -1d, 1d);
            var value = (short)Math.Round(clamped * short.MaxValue);
            pcm[index * 2] = (byte)(value & 0xFF);
            pcm[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new SpeechAudio(pcm, SampleRate, Channels, BitsPerSample, "audio/pcm");
    }
}
