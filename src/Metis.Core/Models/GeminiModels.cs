namespace Metis.Core.Models;

public sealed record GeminiRequest(
    string Prompt,
    byte[]? ScreenshotBytes = null,
    byte[]? RecordedAudioWav = null,
    string? ActiveWindowTitle = null,
    string? AutomationContext = null,
    string ScreenshotMimeType = "image/png",
    int ScreenshotWidth = 0,
    int ScreenshotHeight = 0,
    int ScreenshotScreenLeft = 0,
    int ScreenshotScreenTop = 0,
    int ScreenshotSourceWidth = 0,
    int ScreenshotSourceHeight = 0,
    OperatingMode Mode = OperatingMode.Guide,
    ActivationKind Activation = ActivationKind.Typed,
    PointerContext? Pointer = null,
    string? TaskContext = null,
    string? SkillContext = null,

    /// <summary>
    /// Knowledge the user wrote about this software, from their skills folder.
    /// Separate from <see cref="SkillContext"/>, which is what Metis has
    /// observed about the user's own proficiency.
    /// </summary>
    string? UserSkillPacks = null,

    /// <summary>
    /// A digest of earlier conversations that look relevant to this request.
    /// </summary>
    string? ChatRecall = null,

    /// <summary>
    /// The last few exchanges of the conversation happening right now, verbatim.
    ///
    /// Distinct from <see cref="ChatRecall"/>, which digests older sessions and
    /// only refreshes when the subject changes. This is the immediate thread,
    /// and without it a follow-up cannot be understood at all: "tidy my
    /// downloads" is an answer to "what should the agent do?" or an ordinary
    /// question, and nothing in the request said which.
    /// </summary>
    string? RecentTurns = null,

    /// <summary>
    /// An area the user circled on screen. When present the answer must concern
    /// that region specifically, and the screenshot is cropped to it.
    /// </summary>
    ScreenRegion? Region = null,

    /// <summary>
    /// True when this turn teaches a subject rather than a program, so the
    /// answer should draw a diagram instead of marking the screen. Decided from
    /// the domain of whichever skill matched, not from the request's wording.
    /// </summary>
    bool AcademicTeaching = false,

    /// <summary>
    /// The user's configured preferred display name.
    /// </summary>
    string? UserName = null);

public sealed record GeminiResponse(string Text, string Model, AssistantPlan? Plan = null);

public sealed record OpenAiResponse(
    string Text,
    string Model,
    string? Transcript = null,
    AssistantPlan? Plan = null);

public sealed record GeminiModelInfo(
    string Name,
    string DisplayName,
    IReadOnlyList<string> SupportedGenerationMethods);

public sealed record OpenAiModelInfo(string Name, string DisplayName);

public sealed record ProviderTestResult(
    string Model,
    bool Success,
    string Message,
    TimeSpan Duration);

public sealed record SpeechAudio(
    byte[] PcmData,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string MimeType)
{
    public byte[] ToWavBytes()
    {
        var dataLength = PcmData.Length;
        var bytesPerSample = Math.Max(1, BitsPerSample / 8);
        var effectiveChannels = Math.Max(1, Channels);
        var effectiveSampleRate = Math.Max(1, SampleRate);
        var blockAlign = (short)(effectiveChannels * bytesPerSample);
        var byteRate = effectiveSampleRate * blockAlign;
        var wave = new byte[44 + dataLength];

        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wave, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), 36 + dataLength);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wave, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(wave, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); // PCM
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), (short)effectiveChannels);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), effectiveSampleRate);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), byteRate);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), blockAlign);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), (short)BitsPerSample);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wave, 36);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), dataLength);
        PcmData.CopyTo(wave, 44);

        return wave;
    }
}
