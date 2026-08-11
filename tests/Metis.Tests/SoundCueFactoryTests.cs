using Metis.Core.Services;

namespace Metis.Tests;

public sealed class SoundCueFactoryTests
{
    [Fact]
    public void The_pop_is_short_enough_to_read_as_a_click()
    {
        var pop = SoundCueFactory.Pop();

        Assert.Equal(SoundCueFactory.SampleRate, pop.SampleRate);
        Assert.Equal(1, pop.Channels);
        Assert.Equal(16, pop.BitsPerSample);
        Assert.InRange(DurationSeconds(pop), 0.05, 0.12);
    }

    [Fact]
    public void The_woosh_is_long_enough_to_read_as_movement()
    {
        var woosh = SoundCueFactory.Woosh();

        Assert.InRange(DurationSeconds(woosh), 0.2, 0.4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cues_never_clip(bool woosh)
    {
        var audio = woosh ? SoundCueFactory.Woosh() : SoundCueFactory.Pop();

        var peak = Samples(audio).Max(Math.Abs);

        Assert.True(peak > 1_000, $"cue is inaudibly quiet (peak {peak})");
        Assert.True(peak < short.MaxValue, $"cue clips at the ceiling (peak {peak})");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cues_start_and_end_near_silence_so_they_do_not_click(bool woosh)
    {
        var samples = Samples(woosh ? SoundCueFactory.Woosh() : SoundCueFactory.Pop());

        Assert.InRange(Math.Abs((int)samples[0]), 0, 400);
        Assert.InRange(Math.Abs((int)samples[^1]), 0, 400);
    }

    [Fact]
    public void The_woosh_is_identical_every_time()
    {
        Assert.Equal(SoundCueFactory.Woosh().PcmData, SoundCueFactory.Woosh().PcmData);
    }

    private static double DurationSeconds(Metis.Core.Models.SpeechAudio audio) =>
        audio.PcmData.Length / 2d / audio.SampleRate;

    private static short[] Samples(Metis.Core.Models.SpeechAudio audio)
    {
        var samples = new short[audio.PcmData.Length / 2];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (short)(audio.PcmData[index * 2] | (audio.PcmData[(index * 2) + 1] << 8));
        }

        return samples;
    }
}
