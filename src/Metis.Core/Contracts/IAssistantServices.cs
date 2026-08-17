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
    Task<GeminiResponse> GenerateAsync(
        string apiKey,
        string model,
        GeminiRequest request,
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

    Task<ReasoningResponse> GenerateAsync(
        string? credential,
        string model,
        GeminiRequest request,
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

public interface IAudioPlayback : IDisposable
{
    Task PlayAsync(SpeechAudio audio, CancellationToken cancellationToken = default);
    void Stop();
}

public interface IScreenCaptureService
{
    Task<ScreenCapture?> CaptureActiveWindowAsync(CancellationToken cancellationToken = default);
}

public interface IGlobalPushToTalk : IDisposable
{
    event EventHandler? Pressed;
    event EventHandler? Released;
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

    bool IsRunning { get; }
    void Start();
    void Stop();
}

public interface IUiAutomationService
{
    /// <summary>
    /// Puts text into whichever control currently has keyboard focus, through
    /// the accessibility tree rather than by synthesising keystrokes.
    ///
    /// Typed keystrokes go wherever focus happens to be at that instant, so a
    /// user who clicks into their own document mid-task receives Metis's text
    /// in it. Setting the value addresses the control directly and cannot land
    /// anywhere else. It only works where the control exposes a value pattern,
    /// which most real text fields do and most canvases do not.
    /// </summary>
    Task<UiAutomationResult> TrySetFocusedValueAsync(
        string text,
        CancellationToken cancellationToken = default);

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
    /// Finds the on-screen control that best matches a description, using the
    /// accessibility tree rather than the model's estimate. This is how Metis
    /// can still point at something when the model answered in prose without
    /// coordinates, and it yields the control's exact rectangle so the
    /// highlight takes its true shape.
    /// </summary>
    Task<UiElementHit?> FindElementAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<UiAutomationResult> TryInvokeAsync(
        string automationId,
        ScreenCapture capture,
        CancellationToken cancellationToken = default);

    Task<UiAutomationResult> TryInvokeAtAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default);
}

public interface IDesktopAutomationPipeline : IDisposable
{
    bool IsEmergencyStopped { get; }
    void StartSession();
    Task<DesktopActionResult> EnqueueAsync(
        DesktopAction action,
        ScreenCapture capture,
        CancellationToken cancellationToken = default);
    void CancelSession();
    void EmergencyStop();
}

public interface ICursorService
{
    (int X, int Y) GetPosition();
    (int Left, int Top, int Right, int Bottom) GetWorkingArea(int x, int y);
    (int Left, int Top, int Right, int Bottom) GetMonitorArea(int x, int y);
}

public interface IDesktopAutomationService
{
    bool FullControlEnabled { get; set; }

    /// <summary>
    /// Whether the real Windows pointer may be moved when no background route
    /// works. False keeps the cursor and keyboard focus with the user, at the
    /// cost of refusing the applications that ignore accessibility and window
    /// messages alike.
    /// </summary>
    bool MoveRealCursor { get; set; }

    bool TryResolveTarget(
        DesktopAction action,
        ScreenCapture capture,
        out int screenX,
        out int screenY,
        out string error);

    Task<DesktopActionResult> ExecuteAsync(
        DesktopAction action,
        ScreenCapture capture,
        CancellationToken cancellationToken = default);
}

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
