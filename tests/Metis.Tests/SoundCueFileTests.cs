using Metis.Core.Models;
using Metis.Windows;

namespace Metis.Tests;

public sealed class SoundCueFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "metis-cue-tests",
        Guid.NewGuid().ToString("n"));

    public SoundCueFileTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_selects_the_built_in_cue_without_an_error(string? path)
    {
        Assert.Null(SoundCueFile.TryLoad(path, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void A_valid_short_wav_is_loaded()
    {
        var path = WriteWav("cue.wav", seconds: 0.2);

        var audio = SoundCueFile.TryLoad(path, out var error);

        Assert.NotNull(audio);
        Assert.Null(error);
        Assert.InRange(SoundCueFile.DurationSeconds(audio!), 0.15, 0.25);
    }

    [Fact]
    public void A_quoted_path_is_accepted_because_windows_copy_as_path_adds_quotes()
    {
        var path = WriteWav("quoted.wav", seconds: 0.2);

        Assert.NotNull(SoundCueFile.TryLoad($"\"{path}\"", out _));
    }

    [Fact]
    public void A_missing_file_reports_why_rather_than_throwing()
    {
        Assert.Null(SoundCueFile.TryLoad(Path.Combine(_directory, "nope.wav"), out var error));
        Assert.Contains("No file exists", error);
    }

    [Fact]
    public void An_over_long_cue_is_rejected_so_it_cannot_talk_over_the_user()
    {
        var path = WriteWav("long.wav", seconds: SoundCueFile.MaxDurationSeconds + 1);

        Assert.Null(SoundCueFile.TryLoad(path, out var error));
        Assert.Contains("must be under", error);
    }

    [Fact]
    public void A_file_that_is_not_a_wav_is_rejected_rather_than_played_as_noise()
    {
        var path = Path.Combine(_directory, "notaudio.wav");
        File.WriteAllText(path, "this is plainly not a wave file");

        Assert.Null(SoundCueFile.TryLoad(path, out var error));
        Assert.NotNull(error);
    }

    private string WriteWav(string name, double seconds)
    {
        const int sampleRate = 22_050;
        var path = Path.Combine(_directory, name);
        var sampleCount = (int)(sampleRate * seconds);
        var pcm = new byte[sampleCount * 2];
        for (var index = 0; index < sampleCount; index++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * 8000);
            pcm[index * 2] = (byte)(value & 0xFF);
            pcm[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }
}
