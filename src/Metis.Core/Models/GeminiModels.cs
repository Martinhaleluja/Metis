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
    /// An area the user circled on screen. When present the answer must concern
    /// that region specifically, and the screenshot is cropped to it.
    /// </summary>
    ScreenRegion? Region = null,

    /// <summary>
    /// True when this turn teaches a subject rather than a program, so the
    /// answer should draw a diagram instead of marking the screen. Decided from
    /// the domain of whichever skill matched, not from the request's wording.
    /// </summary>
    bool AcademicTeaching = false);

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
    string MimeType);
