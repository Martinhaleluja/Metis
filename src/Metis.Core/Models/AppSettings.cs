using Metis.Core.Services;

namespace Metis.Core.Models;

public sealed record AppSettings
{
    /// <summary>
    /// Schema version of the settings document. Describes the shape of the
    /// file rather than any user preference, so a later release can migrate a
    /// document written by an older build instead of inferring its age.
    /// </summary>
    public int SettingsVersion { get; init; } = 1;

    public string AiProvider { get; init; } = "Gemini";
    public string ReasoningModel { get; init; } = "gemini-3.5-flash";
    public string SpeechModel { get; init; } = "gemini-2.5-flash-preview-tts";
    public string VoiceName { get; init; } = "Kore";
    public string OpenAiReasoningModel { get; init; } = "gpt-5-mini";
    public string OpenAiTranscriptionModel { get; init; } = "gpt-4o-mini-transcribe";
    public string OpenAiSpeechModel { get; init; } = "tts-1";
    public string OpenAiVoiceName { get; init; } = "alloy";
    public string ClaudeReasoningModel { get; init; } = "claude-sonnet-5";
    public string OpenClawEndpoint { get; init; } = "http://127.0.0.1:18789";
    public string OpenClawModel { get; init; } = "default";
    public string OpenRouterEndpoint { get; init; } = "https://openrouter.ai/api";

    /// <summary>
    /// An OpenRouter model id. Defaults to a free vision model because Metis
    /// reasons about a screenshot on every turn and a text-only model cannot
    /// answer at all.
    /// </summary>
    public string OpenRouterModel { get; init; } = "google/gemini-2.0-flash-exp:free";

    public string OllamaEndpoint { get; init; } = "http://127.0.0.1:11434";
    public string OllamaModel { get; init; } = "qwen3-vl:2b-instruct-q4_K_M";
    public int LocalContextTokens { get; init; } = 2048;
    public string SpeechToTextProvider { get; init; } = "Native";
    public string AssemblyAiModel { get; init; } = "universal-2";
    public string WhisperCppExecutablePath { get; init; } = @"tools\whisper.cpp\Release\whisper-cli.exe";
    public string WhisperCppModelPath { get; init; } = @"models\whisper\ggml-tiny.bin";
    public string TextToSpeechProvider { get; init; } = "Native";
    public string ElevenLabsModel { get; init; } = "eleven_multilingual_v2";
    public string ElevenLabsVoiceId { get; init; } = string.Empty;

    /// <summary>
    /// Which of the voices that ship with Windows to speak with. Empty means
    /// whichever one Windows itself is set to use.
    /// </summary>
    public string WindowsVoiceName { get; init; } = string.Empty;
    public string PiperExecutablePath { get; init; } = @"tools\piper-standalone\piper\piper.exe";
    public string PiperVoiceModelPath { get; init; } = @"models\piper\en_US-lessac-medium.onnx";
    public string ChatterboxEndpoint { get; init; } = "http://127.0.0.1:4123/v1";
    public string ChatterboxModel { get; init; } = "chatterbox-nano";
    public string ChatterboxVoice { get; init; } = "default";
    public int CompanionSize { get; init; } = 56;
    public int CursorDistance { get; init; } = 25;
    public bool SpeechEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool CaptureActiveWindow { get; init; } = true;
    public bool FullDesktopControl { get; init; } = true;
    public string? PreferredMicrophoneId { get; init; }

    /// <summary>
    /// Enables the Ctrl+Alt context shortcut and the Ctrl+Alt+Shift inspect
    /// shortcut alongside the original Ctrl+Shift+1 hold-to-talk chord.
    /// </summary>
    public bool ContextShortcutsEnabled { get; init; } = true;

    /// <summary>
    /// Enables direct voice-to-agent shortcut chords (Ctrl+Shift+A / Ctrl+Alt+A).
    /// </summary>
    public bool DirectAgentShortcutsEnabled { get; init; } = true;

    /// <summary>
    /// The display name the user prefers Metis to address them by.
    /// Empty string means Metis will not use a specific name unless configured.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// The word that starts a request while Ctrl+Space listening is on. Kept
    /// configurable because a name Metis mishears constantly is worse than no
    /// wake word at all, and which name that is depends on the user's voice.
    /// </summary>
    public string WakeWord { get; init; } = "Metis";

    /// <summary>
    /// Whether Metis may move the real Windows pointer.
    ///
    /// Off by default. Metis works through the accessibility tree and window
    /// messages instead, which leaves the cursor where the user left it and the
    /// keyboard focus where they were typing — so they can carry on with
    /// something else while it works. Moving the pointer is the fallback for
    /// the applications that refuse both, and it takes the machine over while
    /// it happens.
    /// </summary>
    public bool MoveRealCursor { get; init; }

    /// <summary>
    /// Which Metis deployment this copy talks to. Read from settings rather
    /// than decided by the app, so a development build cannot end up writing to
    /// production data. Anything unrecognised resolves to production, which is
    /// the most restricted of the three.
    /// </summary>
    public string MetisEnvironment { get; init; } = "production";

    /// <summary>
    /// The Metis backend. Empty is the normal case and does not mean "no
    /// account": it means use the project compiled into the build, which is
    /// what every ordinary install does. Filling this in points a development
    /// copy somewhere else without a recompile. See <c>MetisBackend</c>.
    /// </summary>
    public string SupabaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// The publishable key. Safe to keep in settings by design: it identifies
    /// the project and grants nothing on its own, because row-level security
    /// decides what any request may actually read.
    /// </summary>
    public string SupabaseAnonKey { get; init; } = string.Empty;

    /// <summary>
    /// When this copy last held a session the backend agreed to. It is what
    /// bounds the offline grace period, so someone who signed in yesterday and
    /// is now on a train still gets in, while a machine abandoned for a month
    /// asks again. Null means signed in before Metis started recording it.
    /// </summary>
    public DateTimeOffset? LastAuthenticatedUtc { get; init; }

    /// <summary>
    /// Whether Metis may fetch and install a newer build by itself.
    ///
    /// On by default, because testers are the people who most need the newest
    /// build and are least likely to go looking for it. The installer upgrades
    /// in place, per-user, and needs no administrator rights, so an update is
    /// a prompt rather than an interruption.
    /// </summary>
    public bool AutomaticUpdates { get; init; } = true;

    /// <summary>
    /// The version whose changes have already been shown to this user.
    ///
    /// Empty on a fresh install, which is deliberate: someone installing Metis
    /// for the first time is being introduced to all of it at once and does not
    /// need a list of what changed since a version they never ran. The notes
    /// appear when this differs from the running build and is not empty.
    /// </summary>
    public string LastSeenVersion { get; init; } = string.Empty;

    /// <summary>
    /// Whether a walkthrough waits to see the learner do each step.
    ///
    /// On, Metis checks the screen before moving on, and nudges once if what
    /// the step said would happen has not. Off, it reads the whole walkthrough
    /// straight through on a timer, which is what it always used to do and is
    /// still the right behaviour for someone who wants to listen once and act
    /// afterwards. Either way it never blocks: a step whose outcome cannot be
    /// read from the screen advances on the timer regardless of this setting.
    /// </summary>
    public bool LessonWaitsForLearner { get; init; } = true;

    /// <summary>
    /// Lets Metis draw temporary highlights, arrows, and numbered steps over
    /// the desktop. Overlays are click-through and expire on their own.
    /// </summary>
    public bool VisualGuidanceEnabled { get; init; } = true;

    /// <summary>
    /// Records what the user has learned so guidance can shrink over time.
    /// Disabling it stops all skill and task writes.
    /// </summary>
    public bool MemoryEnabled { get; init; } = true;

    /// <summary>
    /// Plays a short cue when Metis starts and stops listening, so the user
    /// knows the microphone opened without looking away from their work.
    /// </summary>
    public bool ActivationSoundsEnabled { get; init; } = true;

    /// <summary>
    /// Folder of sound files matched to Metis's interaction moments by name,
    /// such as "audio recording started" or "error 2". Moments the folder does
    /// not cover fall back to the built-in synthesised cues or stay silent.
    /// </summary>
    public string SoundPackPath { get; init; } = "sound effects";

    /// <summary>
    /// Autonomous Agent safety/autonomy policy:
    /// "AskApproval" (Recommended: ask on high-risk actions),
    /// "Strict" (ask on all tool executions),
    /// "FullAutonomy" (auto-approve low/medium risk actions).
    /// </summary>
    public string AgentAutonomyMode { get; init; } = "AskApproval";

    /// <summary>
    /// Whether background agents send native Windows desktop toast notifications on start, approval, finish, and failure.
    /// </summary>
    public bool AgentWindowsNotificationsEnabled { get; init; } = true;

    /// <summary>
    /// Maximum turns/steps an autonomous agent may take on a task before completing.
    /// </summary>
    public int AgentMaxTurns { get; init; } = 30;

    /// <summary>
    /// Maximum timeout in seconds for a single tool execution before timing out.
    /// </summary>
    public int AgentTimeoutSeconds { get; init; } = 45;

    /// <summary>
    /// The companion's resting colour, by name from the shared palette. Only
    /// the resting colour is a preference: the listening, thinking, and error
    /// colours carry meaning and stay fixed.
    /// </summary>
    public string CompanionColor { get; init; } = CompanionPalette.DefaultName;

    /// <summary>
    /// The companion's silhouette, by name from the shared catalogue. Purely a
    /// preference: every form behaves identically, so changing it cannot alter
    /// what Metis is able to do.
    /// </summary>
    public string CompanionShape { get; init; } = CompanionShapes.DefaultName;

    /// <summary>
    /// Folder of markdown skills the user has written about their software.
    /// Loading one gives Metis the vocabulary and conventions of a program it
    /// would otherwise only be able to read off the screen.
    /// </summary>
    public string SkillsFolder { get; init; } = "skills";

    public bool UserSkillsEnabled { get; init; } = true;

    /// <summary>
    /// Keeps conversations between runs and lets a new one recall earlier ones.
    /// Disabling it stops all chat writes and recall.
    /// </summary>
    public bool ChatMemoryEnabled { get; init; } = true;

    /// <summary>
    /// Speaks a one-sentence version of any error through the offline Piper
    /// voice. Offline on purpose: errors are most likely exactly when the
    /// configured cloud voice cannot be reached either.
    /// </summary>
    public bool SpeakErrorsAloud { get; init; } = true;

    /// <summary>
    /// Set once the first-run wizard reaches its final step. This lives with
    /// the settings rather than in the memory document on purpose: clearing
    /// memory must not send the user back through onboarding.
    /// </summary>
    public bool OnboardingCompleted { get; init; }

    /// <summary>
    /// Which version of the welcome the user has actually seen.
    ///
    /// Onboarding is not only a first-run formality: it is where the shortcuts
    /// and the two modes are explained. When those change, someone who
    /// completed an older version has been taught something that is no longer
    /// true, so raising <see cref="OnboardingVersions.Current"/> shows them the
    /// new one once. Zero means they finished before this was tracked.
    /// </summary>
    public int OnboardingVersion { get; init; }

    /// <summary>
    /// System, Light, or Dark. System follows the Windows app theme and keeps
    /// tracking it while Metis runs; the other two pin Metis regardless of
    /// what Windows is set to. A high-contrast Windows theme overrides all
    /// three, since respecting it is an accessibility requirement rather than
    /// a preference.
    /// </summary>
    public string ThemePreference { get; init; } = "System";

    /// <summary>
    /// Shortens the companion's flight, the notch unfurl, and the window
    /// transitions for users who find the motion distracting or who get
    /// motion sickness from it.
    /// </summary>
    public bool ReduceMotion { get; init; }

    public AppSettings Normalize() => this with
    {
        SettingsVersion = SettingsVersion < 1 ? 1 : SettingsVersion,
        AiProvider = NormalizeProvider(AiProvider),
        ReasoningModel = NormalizeModel(ReasoningModel, "gemini-3.5-flash"),
        SpeechModel = NormalizeSpeechModel(SpeechModel, "gemini-2.5-flash-preview-tts"),
        VoiceName = string.IsNullOrWhiteSpace(VoiceName) ? "Kore" : VoiceName.Trim(),
        OpenAiReasoningModel = NormalizeModel(OpenAiReasoningModel, "gpt-5-mini"),
        OpenAiTranscriptionModel = NormalizeModel(OpenAiTranscriptionModel, "gpt-4o-mini-transcribe"),
        OpenAiSpeechModel = NormalizeModel(OpenAiSpeechModel, "tts-1"),
        OpenAiVoiceName = string.IsNullOrWhiteSpace(OpenAiVoiceName) ? "alloy" : OpenAiVoiceName.Trim().ToLowerInvariant(),
        ClaudeReasoningModel = NormalizeModel(ClaudeReasoningModel, "claude-sonnet-5"),
        OpenClawEndpoint = NormalizeEndpoint(OpenClawEndpoint, "http://127.0.0.1:18789"),
        OpenClawModel = NormalizeModel(OpenClawModel, "default"),
        OpenRouterEndpoint = NormalizeEndpoint(OpenRouterEndpoint, "https://openrouter.ai/api"),
        OpenRouterModel = NormalizeModel(OpenRouterModel, "google/gemini-2.0-flash-exp:free"),
        OllamaEndpoint = NormalizeEndpoint(OllamaEndpoint, "http://127.0.0.1:11434"),
        OllamaModel = NormalizeModel(OllamaModel, "qwen3-vl:2b-instruct-q4_K_M"),
        LocalContextTokens = Math.Clamp(LocalContextTokens, 2048, 4096),
        SpeechToTextProvider = NormalizeSpeechToTextProvider(SpeechToTextProvider),
        AssemblyAiModel = NormalizeModel(AssemblyAiModel, "universal-2"),
        WhisperCppExecutablePath = NormalizePath(WhisperCppExecutablePath, @"tools\whisper.cpp\Release\whisper-cli.exe"),
        WhisperCppModelPath = NormalizePath(WhisperCppModelPath, @"models\whisper\ggml-tiny.bin"),
        TextToSpeechProvider = NormalizeTextToSpeechProvider(TextToSpeechProvider),
        ElevenLabsModel = NormalizeModel(ElevenLabsModel, "eleven_multilingual_v2"),
        ElevenLabsVoiceId = ElevenLabsVoiceId?.Trim() ?? string.Empty,
        WindowsVoiceName = WindowsVoiceName?.Trim() ?? string.Empty,
        PiperExecutablePath = NormalizePath(PiperExecutablePath, @"tools\piper-standalone\piper\piper.exe"),
        PiperVoiceModelPath = NormalizePath(PiperVoiceModelPath, @"models\piper\en_US-lessac-medium.onnx"),
        ChatterboxEndpoint = NormalizeEndpoint(ChatterboxEndpoint, "http://127.0.0.1:4123/v1"),
        ChatterboxModel = NormalizeModel(ChatterboxModel, "chatterbox-nano"),
        ChatterboxVoice = string.IsNullOrWhiteSpace(ChatterboxVoice) ? "default" : ChatterboxVoice.Trim(),
        CompanionSize = Math.Clamp(CompanionSize, 32, 112),
        CursorDistance = Math.Clamp(CursorDistance, 0, 120),
        SoundPackPath = NormalizeOptionalPath(SoundPackPath),
        CompanionColor = CompanionPalette.Normalize(CompanionColor),
        CompanionShape = CompanionShapes.Normalize(CompanionShape),
        SkillsFolder = NormalizeOptionalPath(SkillsFolder),
        ThemePreference = NormalizeThemePreference(ThemePreference)
    };

    /// <summary>
    /// Falls back to System rather than throwing, matching every other
    /// normaliser here: an unreadable preference should leave Metis following
    /// Windows, not refuse to start.
    /// </summary>
    private static string NormalizeThemePreference(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => "Light",
        "dark" => "Dark",
        _ => "System"
    };

    /// <summary>
    /// Unlike the tool paths, an empty sound-pack path is meaningful: it selects
    /// the built-in cues, so there is no fallback to substitute.
    /// </summary>
    private static string NormalizeOptionalPath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('"');

    private static string NormalizeProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "openai" or "open ai" or "open-ai" => "OpenAI",
        "claude" or "anthropic" => "Claude",
        "openclaw" or "open claw" or "open-claw" => "OpenClaw",
        "openrouter" or "open router" or "open-router" => "OpenRouter",
        "ollama" => "Ollama",
        "automatic" or "auto" => "Automatic",
        _ => "Gemini"
    };

    private static string NormalizeSpeechToTextProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "assemblyai" or "assembly ai" or "assembly-ai" => "AssemblyAI",
        "whisper" or "whisper.cpp" or "whisper cpp" or "whisper-cpp" => "Whisper.cpp",
        _ => "Native"
    };

    private static string NormalizeTextToSpeechProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "elevenlabs" or "eleven labs" or "eleven-labs" => "ElevenLabs",
        "piper" => "Piper",
        "chatterbox" or "chatterbox-nano" or "chatterbox nano" => "Chatterbox-Nano",
        _ => "Native"
    };

    private static string NormalizePath(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().Trim('"');

    private static string NormalizeEndpoint(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimEnd('/');
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? candidate
            : fallback;
    }

    private static string NormalizeModel(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var model = value.Trim();
        return model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model["models/".Length..]
            : model;
    }

    /// <summary>
    /// Keeps the saved speech model to one that can actually speak.
    ///
    /// This test used to run the other way round: any model with "preview-tts"
    /// in its name was thrown away and replaced by the fallback, which was a
    /// text model. So the only models capable of speech were precisely the ones
    /// rejected, and a user who picked the right one had it overwritten with a
    /// wrong one on the next save.
    ///
    /// Keeping the rule here as well as in the provider is deliberate. The
    /// provider protects the request; this protects what is written back to
    /// settings.json, so a dead model does not persist across restarts.
    /// </summary>
    private static string NormalizeSpeechModel(string? value, string fallback)
    {
        var model = NormalizeModel(value, fallback);

        return model.Contains("tts", StringComparison.OrdinalIgnoreCase)
            ? model
            : fallback;
    }
}
