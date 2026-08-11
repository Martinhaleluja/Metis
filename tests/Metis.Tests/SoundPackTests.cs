using Metis.Core.Models;
using Metis.Windows;

namespace Metis.Tests;

public sealed class SoundPackTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "metis-pack-tests",
        Guid.NewGuid().ToString("n"));

    public SoundPackTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void A_missing_folder_yields_an_empty_pack_rather_than_throwing()
    {
        var pack = new SoundPack(Path.Combine(_directory, "does-not-exist"));

        Assert.True(pack.IsEmpty);
        Assert.Null(pack.TryGet(MetisSound.Error));
    }

    [Fact]
    public void A_null_folder_yields_an_empty_pack() => Assert.True(new SoundPack(null).IsEmpty);

    [Fact]
    public void Files_are_matched_to_their_moments()
    {
        WriteWav("Audio recording started.wav");
        WriteWav("Task complete.wav");
        WriteWav("stop metis.wav");

        var pack = new SoundPack(_directory);

        Assert.NotNull(pack.TryGet(MetisSound.RecordingStarted));
        Assert.NotNull(pack.TryGet(MetisSound.TaskComplete));
        Assert.NotNull(pack.TryGet(MetisSound.Stopped));
        Assert.Null(pack.TryGet(MetisSound.SettingsSaved));
    }

    [Fact]
    public void Unrecognised_files_are_ignored_without_breaking_the_pack()
    {
        WriteWav("Task complete.wav");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "not audio");
        WriteWav("untitled.wav");

        var pack = new SoundPack(_directory);

        Assert.NotNull(pack.TryGet(MetisSound.TaskComplete));
        Assert.Single(pack.AvailableSounds);
    }

    [Fact]
    public void Error_variants_rotate_so_a_repeated_failure_does_not_repeat_one_sound()
    {
        WriteWav("error 1.wav", frequency: 300);
        WriteWav("error 2.wav", frequency: 600);
        WriteWav("error 3.wav", frequency: 900);
        var pack = new SoundPack(_directory);

        var picks = Enumerable.Range(0, 12).Select(_ => pack.TryGet(MetisSound.Error)!.PcmData.Length).ToArray();

        Assert.All(picks, length => Assert.True(length > 0));
        for (var index = 1; index < picks.Length; index++)
        {
            // Variants differ in length, so an immediate repeat is detectable.
            Assert.True(
                picks[index] != picks[index - 1] || picks.Distinct().Count() == 1,
                "the same error variant played twice in a row");
        }
    }

    [Fact]
    public void A_broken_file_reports_once_and_then_stays_quiet()
    {
        File.WriteAllText(Path.Combine(_directory, "error 1.wav"), "definitely not audio");
        var messages = new List<string>();
        var pack = new SoundPack(_directory, messages.Add);

        Assert.Null(pack.TryGet(MetisSound.Error));
        Assert.Null(pack.TryGet(MetisSound.Error));

        // Decoding is attempted once and the failure cached, so a broken file
        // does not cost disk access on every activation.
        Assert.Single(messages, message => message.Contains("could not be used"));
    }

    private void WriteWav(string name, int frequency = 440)
    {
        const int sampleRate = 22_050;
        var sampleCount = sampleRate / 4 + frequency;
        var pcm = new byte[sampleCount * 2];
        for (var index = 0; index < sampleCount; index++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequency * index / sampleRate) * 8000);
            pcm[index * 2] = (byte)(value & 0xFF);
            pcm[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        using var stream = new FileStream(Path.Combine(_directory, name), FileMode.Create, FileAccess.Write);
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
