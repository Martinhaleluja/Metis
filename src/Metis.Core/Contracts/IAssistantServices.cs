using Metis.Core.Models;

namespace Metis.Core.Contracts;

public interface ISettingsStore
{
    string SettingsPath { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    string? ReadGeminiApiKey();
    void WriteGeminiApiKey(string apiKey);
    void DeleteGeminiApiKey();
    string? ReadOpenAiApiKey();
    void WriteOpenAiApiKey(string apiKey);
    void DeleteOpenAiApiKey();
    string? ReadClaudeApiKey();
    void WriteClaudeApiKey(string apiKey);
    void DeleteClaudeApiKey();
    string? ReadOpenClawToken();
    void WriteOpenClawToken(string token);
    void DeleteOpenClawToken();
    string? ReadOpenRouterApiKey();
    void WriteOpenRouterApiKey(string apiKey);
    void DeleteOpenRouterApiKey();
    string? ReadAssemblyAiApiKey();
    void WriteAssemblyAiApiKey(string apiKey);
    void DeleteAssemblyAiApiKey();
    string? ReadElevenLabsApiKey();
    void WriteElevenLabsApiKey(string apiKey);
    void DeleteElevenLabsApiKey();
}

public interface IDiagnosticLog
{
    string LogPath { get; }
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public interface IGeminiProvider
{
    /// <param name="onTextDelta">
    /// Receives the answer as it is written, so it can be shown before the
    /// reply is finished. Null asks for the whole answer at once, which is what
    /// diagnostics and self-tests want.
    /// </param>
    Task<GeminiResponse> GenerateAsync(
        string apiKey,
        string model,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeminiModelInfo>> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestModelAsync(
        string apiKey,
        string model,
        CancellationToken cancellationToken = default);

    Task<SpeechAudio?> SynthesizeSpeechAsync(
        string apiKey,
        string model,
        string voiceName,
        string text,
        CancellationToken cancellationToken = default);
}

public interface IOpenAiProvider
{
    Task<OpenAiResponse> GenerateAsync(
        string apiKey,
        string model,
        string transcriptionModel,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpenAiModelInfo>> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestModelAsync(
        string apiKey,
        string model,
        CancellationToken cancellationToken = default);

    Task<SpeechAudio?> SynthesizeSpeechAsync(
        string apiKey,
        string model,
        string voiceName,
        string text,
        CancellationToken cancellationToken = default);
}

public interface IAssemblyAiProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        string apiKey,
        string model,
        RecordedAudio recording,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestConnectionAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

public interface IElevenLabsProvider
{
    Task<SpeechAudio?> SynthesizeSpeechAsync(
        string apiKey,
        string model,
        string voiceId,
        string text,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpeechVoiceInfo>> ListVoicesAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestConnectionAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

public interface IWhisperCppProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        string executablePath,
        string modelPath,
        RecordedAudio recording,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestAsync(
        string executablePath,
        string modelPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The voices that ship with Windows itself.
///
/// This is the offline voice Metis can actually count on. Piper is offline too,
/// but it is a separate executable and a voice model of tens of megabytes that
/// the installer does not carry, so on an installed copy it is simply absent —
/// which reads as the voice being broken rather than missing. Windows has had a
/// speech synthesiser built in for years: no download, no key, no network.
/// </summary>
public interface IWindowsVoiceProvider
{
    /// <summary>
    /// Speaks <paramref name="text"/>. An empty <paramref name="voiceName"/>
    /// uses whichever voice Windows is set to.
    /// </summary>
    Task<SpeechAudio?> SynthesizeSpeechAsync(
        string? voiceName,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>Every voice installed on this machine.</summary>
    IReadOnlyList<SpeechVoiceInfo> ListVoices();

    Task<ProviderTestResult> TestAsync(
        string? voiceName,
        CancellationToken cancellationToken = default);
}

public interface IPiperProvider
{
    Task<SpeechAudio?> SynthesizeSpeechAsync(
        string executablePath,
        string voiceModelPath,
        string text,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestAsync(
        string executablePath,
        string voiceModelPath,
        CancellationToken cancellationToken = default);
}

public interface IChatterboxNanoProvider
{
    Task<SpeechAudio?> SynthesizeSpeechAsync(
        string endpoint,
        string model,
        string voice,
        string text,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestAsync(
        string endpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Common contract for text/vision reasoning providers. Credentials are supplied at call time
/// so implementations never persist them or place them in request URLs.
/// </summary>
public interface IReasoningProvider
{
    ReasoningProviderDescriptor Descriptor { get; }
    Uri Endpoint { get; }

    /// <param name="onTextDelta">
    /// Receives the answer as it is written. Null asks for the whole answer at
    /// once. A provider that cannot stream may ignore it and report the reply
    /// in one piece; the caller must cope with either.
    /// </param>
    Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
        IProgress<string>? onTextDelta = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReasoningModelInfo>> ListModelsAsync(
        string? credential,
        CancellationToken cancellationToken = default);

    Task<ProviderTestResult> TestModelAsync(
        string? credential,
        string model,
        CancellationToken cancellationToken = default);
}

public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    event EventHandler<float>? LevelChanged;
    void Start(string? preferredDeviceId = null);
    Task<RecordedAudio?> StopAsync(CancellationToken cancellationToken = default);
    void Cancel();
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
}

public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// What a piece of audio is for, and therefore what may interrupt it.
///
/// Only one sound plays at a time, so this is what decides who wins when two
/// arrive together. It exists because they are not equally important: a
/// keypress cue is decoration, and a sentence Metis is halfway through saying
/// is the answer the user actually asked for.
/// </summary>
public enum AudioPriority
{
    /// <summary>
    /// Decoration — a keypress, a saved setting, a finished task. Dropped
    /// outright when speech is already playing, rather than cutting it short.
    /// </summary>
    Cue,

    /// <summary>
    /// Something Metis is saying. Takes the device from a cue, and is never
    /// displaced by one.
    /// </summary>
    Speech
}

/// <summary>
/// Who wins when two sounds want the one output device.
/// </summary>
public static class AudioArbitration
{
    /// <summary>
    /// Whether <paramref name="incoming"/> should be dropped instead of taking
    /// the device from what is already playing.
    ///
    /// Exactly one case says yes: a cue arriving while speech is playing.
    /// Truncating a sentence for the sake of a keypress sound throws away the
    /// answer the user asked for, and the interrupted caller cannot tell it
    /// happened — so the voice appears to go quiet for no reason at all.
    /// </summary>
    public static bool ShouldDrop(AudioPriority incoming, AudioPriority playing, bool isPlaying) =>
        isPlaying && incoming == AudioPriority.Cue && playing == AudioPriority.Speech;
}

public interface IAudioPlayback : IDisposable
{
    /// <summary>
    /// Plays <paramref name="audio"/>, replacing whatever is already playing
    /// unless doing so would cut speech short for the sake of a cue. Returns
    /// when playback finishes, is stopped, or is dropped.
    /// </summary>
    Task PlayAsync(
        SpeechAudio audio,
        AudioPriority priority = AudioPriority.Speech,
        CancellationToken cancellationToken = default);

    void Stop();

    /// <summary>
    /// Indicates whether audio is currently playing.
    /// </summary>
    bool IsPlaying { get; }
}

/// <summary>
/// How much of the screen's detail a capture needs to keep.
/// </summary>
public enum ScreenCaptureDetail
{
    /// <summary>
    /// Enough to read and answer about. The image is the largest single thing
    /// in a request and its size is paid for twice — once uploading it and
    /// again as the tokens the model reads it as — so an ordinary question
    /// about the screen gets a smaller frame.
    /// </summary>
    Standard,

    /// <summary>
    /// Everything the display has. For pointing at one small control, where the
    /// answer is a coordinate and detail lost in a downscale cannot be
    /// recovered.
    /// </summary>
    Full
}

public interface IScreenCaptureService
{
    Task<ScreenCapture?> CaptureActiveWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures at the given level of detail. Implementations that have only
    /// one may ignore it.
    /// </summary>
    Task<ScreenCapture?> CaptureActiveWindowAsync(
        ScreenCaptureDetail detail,
        CancellationToken cancellationToken = default) =>
        CaptureActiveWindowAsync(cancellationToken);

    /// <summary>
    /// The coordinate space the next capture will use, if it can be known
    /// without taking one. Null when it cannot, in which case a caller that
    /// wanted to start work alongside the capture has to wait for it instead.
    /// </summary>
    ScreenBounds? PeekCaptureBounds() => null;
}

public interface IGlobalPushToTalk : IDisposable
{
    event EventHandler? Pressed;
    event EventHandler? Released;
    event EventHandler? LiveListeningToggled;
    event EventHandler? DictationPressed;
    event EventHandler? DictationReleased;
    event EventHandler? DirectAgentVoicePressed;
    event EventHandler? DirectAgentVoiceReleased;
    event EventHandler? EmergencyStopPressed;

    /// <summary>
    /// Raised when the user holds Ctrl+Alt (context) or Ctrl+Alt+Shift
    /// (inspect). The kind here reflects whether Shift was already down at that
    /// instant; the authoritative kind arrives with the release event, because
    /// Shift may be added after the hold has started.
    /// </summary>
    event EventHandler<ActivationKind>? ContextActivationPressed;

    event EventHandler<ActivationKind>? ContextActivationReleased;

    /// <summary>
    /// Raised when Shift joins a hold already in progress, upgrading it to an
    /// inspect activation.
    /// </summary>
    event EventHandler? ContextActivationUpgraded;

    /// <summary>
    /// Raised when Ctrl+Space is pressed, turning continuous listening on or
    /// off. A toggle rather than a hold, because its purpose is to let the user
    /// work with both hands while Metis listens.
    /// </summary>
    event EventHandler? ActiveListeningToggled;

    /// <summary>
    /// Raised when Escape is pressed while <see cref="CancelKeyEnabled"/> is
    /// set, so on-screen surfaces that never take focus can still be dismissed
    /// with the key their own hint offers.
    /// </summary>
    event EventHandler? CancelPressed;

    /// <summary>
    /// Whether Escape currently belongs to Metis. Enabled only while something
    /// is on screen that Escape should dismiss.
    /// </summary>
    bool CancelKeyEnabled { get; set; }

    /// <summary>
    /// Enables the Ctrl+Alt shortcuts. The original hold-to-talk chord and the
    /// F12 emergency stop stay active regardless.
    /// </summary>
    bool ContextShortcutsEnabled { get; set; }

    /// <summary>
    /// Enables direct voice-to-agent shortcut chords (Ctrl+Shift+A / Ctrl+Alt+A).
    /// </summary>
    bool DirectAgentShortcutsEnabled { get; set; }

    bool IsRunning { get; }
    void Start();
    void Stop();
}

public interface IUiAutomationService
{

    Task<string?> DescribeWindowAsync(
        ScreenCapture capture,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes the smallest control under a screen coordinate, for Inspect
    /// activation. Returns null when nothing identifiable is there, so Metis
    /// can say it could not resolve the target rather than guess.
    /// </summary>
    Task<string?> DescribeElementAtAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes all visible controls, labels, and text situated within a specified screen region
    /// or bounding box, for traced area and rectangle inspection.
    /// </summary>
    Task<string?> DescribeRegionAsync(
        ScreenCapture capture,
        int screenLeft,
        int screenTop,
        int screenWidth,
        int screenHeight,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the on-screen control that best matches a description, using the
    /// accessibility tree rather than the model's estimate. This is how Metis
    /// can still point at something when the model answered in prose without
    /// coordinates, and it yields the control's exact rectangle so the
    /// highlight takes its true shape.
    /// </summary>
    Task<UiElementHit?> FindElementAsync(
        string query,
        CancellationToken cancellationToken = default);

}

public interface ICursorService
{
    (int X, int Y) GetPosition();
    (int Left, int Top, int Right, int Bottom) GetWorkingArea(int x, int y);
    (int Left, int Top, int Right, int Bottom) GetMonitorArea(int x, int y);
}

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

/// <summary>
/// Works out where an annotation's subject really is on screen.
///
/// Read-only by nature: it asks Windows where a control sits so the mark can
/// wrap it exactly, and never touches the control it finds.
/// </summary>
public interface IAnnotationResolver
{
    Task<ResolvedAnnotation?> ResolveAsync(
        AnnotationTarget target,
        ScreenCapture capture,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What Metis remembers between sessions: which skills the learner has
/// practised, how those attempts went, and their stored preferences.
/// </summary>
public interface IMemoryService
{
    /// <summary>Where the memory file lives on disk.</summary>
    string MemoryPath { get; }

    Task<MemoryDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the learner worked through a skill, and whether they needed
    /// to be shown. This is what lets Metis pitch later explanations at what
    /// they already know rather than starting from nothing every time.
    /// </summary>
    Task<SkillProgress?> RecordSkillUseAsync(
        string application,
        string skill,
        bool succeeded,
        bool neededGuidance,
        CancellationToken cancellationToken = default);

    Task RecordTaskOutcomeAsync(
        AgentTaskState state,
        bool success,
        string summary,
        CancellationToken cancellationToken = default);

    Task<string?> GetPreferenceAsync(string key, CancellationToken cancellationToken = default);

    Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
