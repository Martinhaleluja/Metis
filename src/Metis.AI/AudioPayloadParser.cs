using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Metis.Core.Models;

namespace Metis.AI;

public static partial class AudioPayloadParser
{
    public static SpeechAudio Parse(byte[] bytes, string? mimeType, int defaultSampleRate = 24000)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return new SpeechAudio(Array.Empty<byte>(), defaultSampleRate, 1, 16, mimeType ?? "audio/pcm");
        }

        var normalizedMime = string.IsNullOrWhiteSpace(mimeType) ? "audio/pcm" : mimeType.Trim();

        // Check for RIFF WAVE container
        if (bytes.Length >= 44 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            var offset = 12;
            var channels = 1;
            var sampleRate = defaultSampleRate;
            var bits = 16;
            while (offset + 8 <= bytes.Length)
            {
                var chunkId = bytes.AsSpan(offset, 4);
                var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
                if (chunkLength < 0)
                {
                    break;
                }

                if (chunkId.SequenceEqual("fmt "u8) && chunkLength >= 16 && offset + 8 + 16 <= bytes.Length)
                {
                    channels = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 10, 2));
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 12, 4));
                    bits = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset + 22, 2));
                }
                else if (chunkId.SequenceEqual("data"u8))
                {
                    var dataOffset = offset + 8;
                    var available = bytes.Length - dataOffset;
                    var actualLength = (chunkLength <= available && chunkLength > 0) ? chunkLength : available;
                    if (actualLength > 0)
                    {
                        return new SpeechAudio(
                            bytes.AsSpan(dataOffset, actualLength).ToArray(),
                            sampleRate > 0 ? sampleRate : defaultSampleRate,
                            channels > 0 ? channels : 1,
                            bits > 0 ? bits : 16,
                            normalizedMime);
                    }
                }

                offset += 8 + chunkLength + (chunkLength & 1);
            }
        }

        // Parse rate from mimeType if present e.g. "audio/L16;codec=pcm;rate=24000" or "audio/pcm;rate=24000"
        var rateMatch = SampleRateRegex().Match(normalizedMime);
        var rate = rateMatch.Success && int.TryParse(rateMatch.Groups[1].Value, out var parsedRate) && parsedRate > 0
            ? parsedRate
            : defaultSampleRate;

        return new SpeechAudio(bytes, rate, 1, 16, normalizedMime);
    }

    [GeneratedRegex("(?:rate=|rate:)(\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleRateRegex();
}
