using System.Buffers.Binary;
using System.Text;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class AudioPayloadParserTests
{
    [Fact]
    public void Parse_handles_empty_bytes()
    {
        var result = AudioPayloadParser.Parse(Array.Empty<byte>(), "audio/pcm");
        Assert.NotNull(result);
        Assert.Empty(result.PcmData);
        Assert.Equal(24000, result.SampleRate);
    }

    [Fact]
    public void Parse_extracts_sample_rate_from_mime_type()
    {
        var raw = new byte[] { 1, 2, 3, 4 };
        var result = AudioPayloadParser.Parse(raw, "audio/L16;codec=pcm;rate=16000");

        Assert.Equal(16000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.Equal(16, result.BitsPerSample);
        Assert.Equal(raw, result.PcmData);
    }

    [Fact]
    public void Parse_extracts_pcm_from_riff_wave_container()
    {
        var rawPcm = new byte[] { 10, 20, 30, 40, 50, 60 };
        var audio = new SpeechAudio(rawPcm, 22050, 2, 16, "audio/pcm");
        var wavBytes = audio.ToWavBytes();

        var parsed = AudioPayloadParser.Parse(wavBytes, "audio/wav");

        Assert.Equal(22050, parsed.SampleRate);
        Assert.Equal(2, parsed.Channels);
        Assert.Equal(16, parsed.BitsPerSample);
        Assert.Equal(rawPcm, parsed.PcmData);
    }

    [Fact]
    public void SpeechAudio_ToWavBytes_generates_valid_riff_header()
    {
        var rawPcm = new byte[100];
        var audio = new SpeechAudio(rawPcm, 48000, 1, 16, "audio/pcm");
        var wav = audio.ToWavBytes();

        Assert.Equal(144, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav.AsSpan(0, 4)));
        Assert.Equal(136, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4, 4)));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav.AsSpan(8, 4)));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav.AsSpan(12, 4)));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20, 2))); // PCM format
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22, 2))); // Channels
        Assert.Equal(48000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24, 4))); // SampleRate
        Assert.Equal(96000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28, 4))); // ByteRate
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32, 2))); // BlockAlign
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34, 2))); // BitsPerSample
        Assert.Equal("data", Encoding.ASCII.GetString(wav.AsSpan(36, 4)));
        Assert.Equal(100, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4)));
    }
}
