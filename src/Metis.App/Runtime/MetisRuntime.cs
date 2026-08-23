using System.IO;
using System.Text.Json;
using Metis.AI;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;
using Metis.Core.State;
using Metis.Data;
using Metis.Windows;

namespace Metis.App.Runtime;

public sealed class MetisRuntime : IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly IDiagnosticLog _log;

    /// <summary>
    /// The diagnostic log, so window-level code can record what the user saw
    /// alongside what the runtime did. Interface-typed: callers get to write
    /// entries, not to reconfigure logging.
    /// </summary>
    public IDiagnosticLog Log => _log;
    private readonly IGeminiProvider _gemini;
    private readonly IOpenAiProvider _openAi;
    private readonly IReasoningProvider _claude;
    private readonly IAssemblyAiProvider _assemblyAi;
    private readonly IElevenLabsProvider _elevenLabs;
    private readonly IWhisperCppProvider _whisperCpp;
    private readonly IPiperProvider _piper;
    private readonly IWindowsVoiceProvider _windowsVoice;
    private readonly IChatterboxNanoProvider _chatterboxNano;
    private readonly IAudioRecorder _recorder;
    private readonly IAudioPlayback _audioPlayback;
    private readonly IScreenCaptureService _capture;
    private readonly IUiAutomationService _uiAutomation;

    /// <summary>
    /// Turns what the model said it was pointing at into a rectangle on the
    /// real screen. Constructed directly rather than injected because it holds
    /// no state and no configuration: it is a question asked of Windows.
    /// </summary>
    private readonly IAnnotationResolver _annotations = new WindowsAnnotationResolver();
    private readonly IGlobalPushToTalk _pushToTalk;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IMemoryService _memory;
    private readonly TaskContextTracker _taskContext = new();
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private SoundPack _soundPack = new(null);
    private FileSkillStore _skillStore = new(null);
    private IReadOnlyList<SkillPack> _userSkills = [];
    private readonly JsonChatStore _chatStore;
    private List<ChatSession> _chatSessions = [];
    private ChatSession _currentChat = ChatSession.Start();

    /// <summary>The conversations Metis has stored, newest first.</summary>
    public IReadOnlyList<ChatSession> Chats => _chatSessions;

    public ChatSession CurrentChat => _currentChat;

    /// <summary>The skills the user has taught Metis.</summary>
    public IReadOnlyList<SkillPack> UserSkills => _userSkills;

    public string SkillsFolder => _skillStore.FolderPath ?? string.Empty;

    public event EventHandler? ChatsChanged;
    private ScreenCapture? _lastLessonCapture;

    /// <summary>
    /// Where the reply said it was talking about, kept for the steps that do
    /// not say for themselves.
    ///
    /// A model often names the spot once, at the top of its answer, and then
    /// writes steps that read as prose without repeating the coordinates. The
    /// lesson used to drop that annotation on the floor and mark nothing at
    /// all, which is how a reply that knew exactly where to point ended up
    /// pointing nowhere.
    /// </summary>
    private AnnotationTarget? _lessonFallbackTarget;

    /// <summary>
    /// Whether this turn is teaching a subject rather than a program, from the
    /// domain of whichever skill matched.
    /// </summary>
    private bool _academicTeaching;
    private CancellationTokenSource? _turnCancellation;
    private ActivationKind _pendingActivation = ActivationKind.Typed;
    private PointerContext? _pendingPointer;
    private IReadOnlyList<GuidancePoint>? _pendingTrace;
    private bool _disposed;

    public MetisRuntime()
        : this(
            new JsonSettingsStore(),
            new WindowsCredentialStore(),
            new FileDiagnosticLog(),
            new GeminiProvider(),
            new OpenAiProvider(),
            new ClaudeReasoningProvider(),
            new AssemblyAiProvider(),
            new ElevenLabsProvider(),
            new WhisperCppProvider(),
            new PiperProvider(),
            new WindowsVoiceProvider(),
            new ChatterboxNanoProvider(),
            new WaveAudioRecorder(),
            new WaveAudioPlayback(),
            new VirtualDesktopCaptureService(),
            new FlaUiAutomationService(),
            new GlobalPushToTalk(),
            new CursorService(),
            new StartupRegistration(),
            new JsonMemoryStore())
    {
    }

    internal MetisRuntime(
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        IDiagnosticLog log,
        IGeminiProvider gemini,
        IOpenAiProvider openAi,
        IReasoningProvider claude,
        IAssemblyAiProvider assemblyAi,
        IElevenLabsProvider elevenLabs,
        IWhisperCppProvider whisperCpp,
        IPiperProvider piper,
        IWindowsVoiceProvider windowsVoice,
        IChatterboxNanoProvider chatterboxNano,
        IAudioRecorder recorder,
        IAudioPlayback audioPlayback,
        IScreenCaptureService capture,
        IUiAutomationService uiAutomation,
        IGlobalPushToTalk pushToTalk,
        ICursorService cursor,
        IStartupRegistration startupRegistration,
        IMemoryService memory)
    {
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _log = log;
        _gemini = gemini;
        _openAi = openAi;
        _claude = claude;
        _assemblyAi = assemblyAi;
        _elevenLabs = elevenLabs;
        _whisperCpp = whisperCpp;
        _piper = piper;
        _windowsVoice = windowsVoice;
        _chatterboxNano = chatterboxNano;
        _recorder = recorder;
        _audioPlayback = audioPlayback;
        _capture = capture;
        _uiAutomation = uiAutomation;
        _pushToTalk = pushToTalk;
        _startupRegistration = startupRegistration;
        _memory = memory;
        _chatStore = new JsonChatStore(log: message => log.Info(message));
        Cursor = cursor;
        State = new AssistantStateMachine();
    }

    public AppSettings Settings { get; private set; } = new();
    public AssistantStateMachine State { get; }
    public ICursorService Cursor { get; }

    /// <summary>
    /// What this machine has asked of each model. Counted locally because most
    /// requests go straight from here to the provider on the user's own key and
    /// never touch a Metis server, so a server-side tally would read zero for
    /// exactly the people who want to know what is left of a free allowance.
    /// </summary>
    public ModelUsageLedger ModelUsage { get; } = new();

    /// <summary>
    /// Points the current provider at a different model. Which setting that
    /// writes depends on the provider, because each keeps its own.
    /// </summary>
    public async Task SetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        var updated = (Settings.AiProvider switch
        {
            "OpenAI" => Settings with { OpenAiReasoningModel = modelId },
            "Claude" => Settings with { ClaudeReasoningModel = modelId },
            "OpenRouter" => Settings with { OpenRouterModel = modelId },
            "Ollama" => Settings with { OllamaModel = modelId },
            _ => Settings with { ReasoningModel = modelId }
        }).Normalize();

        await _settingsStore.SaveAsync(updated, cancellationToken);
        Settings = updated;
        SettingsChanged?.Invoke(this, Settings);
        SetStatus($"Answering with {modelId}");
        _log.Info($"Model set to {modelId} for {Settings.AiProvider}.");
    }

    /// <summary>
    /// Who is signed in, or <see cref="MetisAccount.SignedOut"/>. Metis works
    /// fully without an account on the user's own API key, so signed out is an
    /// ordinary state rather than an error.
    /// </summary>
    public MetisAccount Account { get; private set; } = MetisAccount.SignedOut;

    public event EventHandler<MetisAccount>? AccountChanged;

    /// <summary>
    /// Adopts a session established by the sign-in window. The account arrives
    /// from the backend; nothing here invents or upgrades it, because an
    /// entitlement the client granted itself is not an entitlement.
    /// </summary>
    public void SignIn(MetisAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        Account = account;
        AccountChanged?.Invoke(this, account);
        _log.Info($"Signed in as {account.Role} on the {account.Plan} plan.");
        SetStatus($"Signed in — {account.Plan} plan");
    }

    public void SignOut()
    {
        Account = MetisAccount.SignedOut;
        AccountChanged?.Invoke(this, Account);
        _log.Info("Signed out.");
        SetStatus("Signed out. Metis still works with your own API key.");
    }

    public string MemoryPath => _memory.MemoryPath;
    public bool HasGeminiKey => !string.IsNullOrWhiteSpace(_secretStore.ReadGeminiApiKey());
    public bool HasOpenAiKey => !string.IsNullOrWhiteSpace(_secretStore.ReadOpenAiApiKey());
    public bool HasClaudeKey => !string.IsNullOrWhiteSpace(_secretStore.ReadClaudeApiKey());
    public bool HasOpenClawToken => !string.IsNullOrWhiteSpace(_secretStore.ReadOpenClawToken());
    public bool HasOpenRouterKey => !string.IsNullOrWhiteSpace(_secretStore.ReadOpenRouterApiKey());
    public bool HasAssemblyAiKey => !string.IsNullOrWhiteSpace(_secretStore.ReadAssemblyAiApiKey());
    public bool HasElevenLabsKey => !string.IsNullOrWhiteSpace(_secretStore.ReadElevenLabsApiKey());
    public bool HasAnyApiKey => HasGeminiKey || HasOpenAiKey || HasClaudeKey;
    public string SettingsPath => _settingsStore.SettingsPath;
    public string LogPath => _log.LogPath;
    public string CurrentStatus { get; private set; } = "Starting Metis…";
    public MetisActivity CurrentActivity { get; private set; } = MetisActivity.Idle;

    public event EventHandler<AppSettings>? SettingsChanged;
    public event EventHandler<AssistantMessage>? MessageAdded;
    public event EventHandler<CompanionResponse>? CompanionResponseStarted;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<CompanionGuidance>? CompanionGuidanceRequested;
    public event EventHandler<GuidanceOverlayRequest>? GuidanceOverlayRequested;

    /// <summary>
    /// Asks the user to approve a high-risk action before it runs. The handler
    /// returns true to allow it.
    ///
    /// The safety engine has always been able to say an action needs
    /// confirming, and nothing ever asked — so high-risk actions were
    /// classified and then performed anyway. Nothing may subscribe to this and
    /// answer automatically: an approval nobody saw is worse than no approval,
    /// because it looks like one.
    /// </summary>
    public event EventHandler<MetisActivity>? ActivityChanged;

    /// <summary>
    /// True while a lesson runs. The companion stops following the cursor for
    /// the duration: during teaching its only job is to show the learner where
    /// to look, and a companion trailing the pointer competes with the very
    /// thing it is pointing at.
    /// </summary>
    public event EventHandler<bool>? CompanionDetachRequested;

    /// <summary>Raised as a lesson moves between steps.</summary>
    public event EventHandler<LessonState>? LessonChanged;

    /// <summary>
    /// Asks the companion to perform a movement as a ghost cursor. Raised
    /// rather than awaited: the demonstration is meant to play while Metis
    /// explains it, the way a person talks through what their hand is doing.
    /// </summary>
    public event EventHandler<CompanionDemo>? CompanionDemoRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Settings = (await _settingsStore.LoadAsync(cancellationToken)).Normalize();
        ReloadSoundPack();
        ReloadUserSkills();
        _chatSessions = _chatStore.LoadAll().ToList();
        ConfigureCaptureProfile();
        _recorder.LevelChanged += OnAudioLevelChanged;
        _pushToTalk.Pressed += OnPushToTalkPressed;
        _pushToTalk.Released += OnPushToTalkReleased;
        _pushToTalk.EmergencyStopPressed += OnEmergencyStopPressed;
        _pushToTalk.CancelPressed += (_, _) => TraceCancelKeyPressed?.Invoke(this, EventArgs.Empty);
        _pushToTalk.ContextActivationPressed += OnContextActivationPressed;
        _pushToTalk.ContextActivationReleased += OnContextActivationReleased;
        _pushToTalk.ContextActivationUpgraded += OnContextActivationUpgraded;
        _pushToTalk.ActiveListeningToggled += OnActiveListeningToggled;
        _pushToTalk.ContextShortcutsEnabled = Settings.ContextShortcutsEnabled;
        try
        {
            _pushToTalk.Start();
            SetStatus(HasConfiguredProviderKey()
                ? "Ready — hold Ctrl+Alt to ask, Ctrl+Space to listen hands-free, Ctrl+Alt+Shift to point"
                : "Setup required — add a Gemini or OpenAI API key");
        }
        catch (Exception exception)
        {
            _log.Error("The global hold-to-talk shortcut could not start.", exception);
            SetStatus("Metis started, but Ctrl+Shift+1 is unavailable. Open diagnostics for details.");
        }
        _log.Info("Metis runtime initialized.");
        PlayCue(MetisSound.AppStarted);
    }

    public async Task SaveSettingsAsync(
        AppSettings settings,
        string? newGeminiApiKey,
        string? newOpenAiApiKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalized = settings.Normalize();

        if (!string.IsNullOrWhiteSpace(newGeminiApiKey))
        {
            _secretStore.WriteGeminiApiKey(newGeminiApiKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(newOpenAiApiKey))
        {
            _secretStore.WriteOpenAiApiKey(newOpenAiApiKey.Trim());
        }

        if (normalized.StartWithWindows != Settings.StartWithWindows)
        {
            _startupRegistration.SetEnabled(normalized.StartWithWindows);
        }

        await _settingsStore.SaveAsync(normalized, cancellationToken);
        Settings = normalized;
        _pushToTalk.ContextShortcutsEnabled = Settings.ContextShortcutsEnabled;
        ReloadSoundPack();
        ReloadUserSkills();
        ConfigureCaptureProfile();
        SettingsChanged?.Invoke(this, Settings);
        PlayCue(MetisSound.SettingsSaved);
        SetStatus(HasConfiguredProviderKey()
            ? "Settings saved — Metis is ready"
            : $"Settings saved — add a {ProviderDisplayName(normalized.AiProvider)} key to ask Metis");
        _log.Info("Settings saved.");
    }

    public void SaveAdditionalProviderSecrets(
        string? claudeApiKey,
        string? openClawToken,
        string? assemblyAiApiKey,
        string? elevenLabsApiKey,
        string? openRouterApiKey = null)
    {
        ThrowIfDisposed();
        WriteIfPresent(claudeApiKey, _secretStore.WriteClaudeApiKey);
        WriteIfPresent(openClawToken, _secretStore.WriteOpenClawToken);
        WriteIfPresent(openRouterApiKey, _secretStore.WriteOpenRouterApiKey);
        WriteIfPresent(assemblyAiApiKey, _secretStore.WriteAssemblyAiApiKey);
        WriteIfPresent(elevenLabsApiKey, _secretStore.WriteElevenLabsApiKey);
        _log.Info("Additional provider credentials were updated in Windows Credential Manager.");
    }

    private static void WriteIfPresent(string? value, Action<string> writer)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer(value.Trim());
        }
    }

    public async Task<IReadOnlyList<GeminiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var key = RequireApiKey();
        SetStatus("Checking models available to this key…");
        var models = await _gemini.ListModelsAsync(key, cancellationToken);
        SetStatus($"Found {models.Count} compatible Gemini models");
        return models;
    }

    public async Task<IReadOnlyList<OpenAiModelInfo>> GetOpenAiModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var key = RequireOpenAiApiKey();
        SetStatus("Checking OpenAI models available to this API project…");
        var models = await _openAi.ListModelsAsync(key, cancellationToken);
        SetStatus($"Found {models.Count} compatible OpenAI models");
        return models;
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        ThrowIfDisposed();
        return _recorder.GetInputDevices();
    }

    public async Task<ProviderTestResult> TestModelAsync(string model, CancellationToken cancellationToken = default)
    {
        var key = RequireApiKey();
        SetStatus($"Testing {model}…");
        var result = await _gemini.TestModelAsync(key, model, cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<ProviderTestResult> TestOpenAiModelAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        var key = RequireOpenAiApiKey();
        SetStatus($"Testing OpenAI {model}…");
        var result = await _openAi.TestModelAsync(key, model, cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<ProviderTestResult> TestAssemblyAiAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Testing AssemblyAIâ€¦");
        var result = await _assemblyAi.TestConnectionAsync(RequireAssemblyAiApiKey(), cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<ProviderTestResult> TestElevenLabsAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Testing ElevenLabsâ€¦");
        var result = await _elevenLabs.TestConnectionAsync(RequireElevenLabsApiKey(), cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<ProviderTestResult> TestWhisperCppAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Testing local whisper.cpp Tiny…");
        var result = await _whisperCpp.TestAsync(
            ResolveLocalPath(Settings.WhisperCppExecutablePath),
            ResolveLocalPath(Settings.WhisperCppModelPath),
            cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<ProviderTestResult> TestPiperAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Testing local Piper voice…");
        var result = await _piper.TestAsync(
            ResolveLocalPath(Settings.PiperExecutablePath),
            ResolveLocalPath(Settings.PiperVoiceModelPath),
            cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<ProviderTestResult> TestWindowsVoiceAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Testing the built-in Windows voice…");
        var result = await _windowsVoice.TestAsync(Settings.WindowsVoiceName, cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    /// <summary>
    /// The voices Windows already has. Read straight from the system rather
    /// than fetched, so this works with no key and no network.
    /// </summary>
    public IReadOnlyList<SpeechVoiceInfo> GetWindowsVoices() => _windowsVoice.ListVoices();

    public async Task<ProviderTestResult> TestChatterboxNanoAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Testing local Chatterbox-Nano…");
        var result = await _chatterboxNano.TestAsync(Settings.ChatterboxEndpoint, cancellationToken);
        SetStatus(result.Message);
        return result;
    }

    public async Task<IReadOnlyList<SpeechVoiceInfo>> GetElevenLabsVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        SetStatus("Loading ElevenLabs voicesâ€¦");
        var voices = await _elevenLabs.ListVoicesAsync(RequireElevenLabsApiKey(), cancellationToken);
        SetStatus($"Found {voices.Count} ElevenLabs voices");
        return voices;
    }

    public async Task<IReadOnlyList<ReasoningModelInfo>> GetReasoningModelsAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        SetStatus($"Checking {ProviderDisplayName(provider)} modelsâ€¦");
        if (provider == "Claude")
        {
            var models = await _claude.ListModelsAsync(RequireClaudeApiKey(), cancellationToken);
            SetStatus($"Found {models.Count} Claude models");
            return models;
        }

        var localProvider = CreateEndpointProvider(provider);
        try
        {
            var credential = EndpointProviderCredential(provider);
            var localModels = await localProvider.ListModelsAsync(credential, cancellationToken);
            SetStatus($"Found {localModels.Count} {ProviderDisplayName(provider)} models");
            return localModels;
        }
        finally
        {
            (localProvider as IDisposable)?.Dispose();
        }
    }

    public async Task<ProviderTestResult> TestReasoningProviderAsync(
        string provider,
        string model,
        CancellationToken cancellationToken = default)
    {
        SetStatus($"Testing {ProviderDisplayName(provider)} {model}â€¦");
        ProviderTestResult result;
        if (provider == "Claude")
        {
            result = await _claude.TestModelAsync(RequireClaudeApiKey(), model, cancellationToken);
        }
        else
        {
            var localProvider = CreateEndpointProvider(provider);
            try
            {
                var credential = EndpointProviderCredential(provider);
                result = await localProvider.TestModelAsync(credential, model, cancellationToken);
            }
            finally
            {
                (localProvider as IDisposable)?.Dispose();
            }
        }

        SetStatus(result.Message);
        return result;
    }

    public async Task<IReadOnlyList<ProviderTestResult>> TestAllModelsAsync(CancellationToken cancellationToken = default)
    {
        var key = RequireApiKey();
        var models = await _gemini.ListModelsAsync(key, cancellationToken);
        var results = new List<ProviderTestResult>(models.Count);

        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            SetStatus($"Testing {index + 1} of {models.Count}: {model.Name}…");
            results.Add(await _gemini.TestModelAsync(key, model.Name, cancellationToken));
        }

        var working = results.Count(result => result.Success);
        SetStatus($"Model check complete — {working} of {results.Count} worked");
        return results;
    }

    public Task AskTextAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Task.CompletedTask;
        }

        var normalizedPrompt = prompt.Trim();
        _pendingActivation = ActivationKind.Typed;
        _pendingPointer = null;
        MessageAdded?.Invoke(this, new AssistantMessage(AssistantRole.User, normalizedPrompt, DateTimeOffset.Now));
        return RunTurnAsync(normalizedPrompt, null, cancellationToken);
    }

    private CancellationTokenSource? _activeListening;

    /// <summary>Whether Ctrl+Space listening is currently on.</summary>
    public bool IsActivelyListening => _activeListening is { IsCancellationRequested: false };

    public event EventHandler<bool>? ActiveListeningChanged;

    /// <summary>
    /// How long each stretch of speech is before it is transcribed and checked
    /// for the wake word. Long enough to contain the name and a short question
    /// after it, short enough that Metis does not answer several seconds late.
    /// </summary>
    private static readonly TimeSpan ListeningSegment = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Turns continuous listening on or off. Bound to Ctrl+Space, and also
    /// switched off by anything that takes the microphone for a normal request,
    /// because both cannot record at once.
    /// </summary>
    public void ToggleActiveListening()
    {
        if (IsActivelyListening)
        {
            StopActiveListening("Stopped listening.");
            return;
        }

        if (!Settings.SpeechEnabled && Settings.SpeechToTextProvider == "Native")
        {
            SetStatus("Set up speech to text in Setup before using continuous listening.");
            return;
        }

        var listening = new CancellationTokenSource();
        _activeListening = listening;
        _listeningFailures = 0;
        ActiveListeningChanged?.Invoke(this, true);
        PlayCue(MetisSound.RecordingStarted);
        SetActivity(MetisActivityKind.Listening, $"Listening for “{WakeWordListener.Normalize(Settings.WakeWord)}”");
        SetStatus($"Listening — say “{WakeWordListener.Normalize(Settings.WakeWord)}”, or press Ctrl+Space to stop");
        _log.Info("Continuous listening started.");
        _ = ListenContinuouslyAsync(listening.Token);
    }

    private void StopActiveListening(string status)
    {
        var listening = _activeListening;
        _activeListening = null;
        if (listening is null)
        {
            return;
        }

        listening.Cancel();
        listening.Dispose();
        ActiveListeningChanged?.Invoke(this, false);
        SetActivity(MetisActivityKind.Idle, string.Empty);
        SetStatus(status);
        _log.Info("Continuous listening stopped.");
    }

    /// <summary>
    /// Listens in short stretches, transcribing each and watching for the wake
    /// word.
    ///
    /// Nothing is sent anywhere until the wake word is heard: each stretch is
    /// transcribed and then discarded, and only what follows the name becomes a
    /// request. That matters because this is a microphone left on in someone's
    /// room — the loop is built so that ordinary conversation costs a
    /// transcription and nothing else.
    /// </summary>
    private async Task ListenContinuouslyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // A turn started by other means owns the microphone, so wait
                // rather than fighting it for the device.
                if (_recorder.IsRecording)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
                    continue;
                }

                var heard = await ListenForOneSegmentAsync(cancellationToken);
                if (heard is null)
                {
                    continue;
                }

                var request = heard.Request;
                if (request.Length == 0)
                {
                    // The name on its own: the user is about to say what they
                    // want, so listen once more and use that.
                    SetActivity(MetisActivityKind.Listening, "Go ahead");
                    PlayCue(MetisSound.InspectPressed);
                    var followUp = await CaptureSegmentAsync(cancellationToken);
                    request = followUp ?? string.Empty;
                }

                if (request.Length == 0)
                {
                    SetActivity(MetisActivityKind.Listening, "Still listening");
                    continue;
                }

                _log.Info($"Wake word heard, request: {Shorten(request, 120)}");
                RecordChatTurn("user", request, null);
                await RunTurnAsync(request, null, cancellationToken);
                SetActivity(MetisActivityKind.Listening, "Still listening");
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+Space again, or shutdown.
        }
        catch (Exception exception)
        {
            _log.Error("Continuous listening stopped after an error.", exception);
            StopActiveListening("Listening stopped after a problem. Press Ctrl+Space to start again.");
        }
    }

    private async Task<WakeWordMatch?> ListenForOneSegmentAsync(CancellationToken cancellationToken)
    {
        var transcript = await CaptureSegmentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        var match = WakeWordListener.Listen(transcript, Settings.WakeWord);
        return match.Heard ? match : null;
    }

    /// <summary>
    /// Records one stretch and returns what was said, or null if nothing was.
    /// </summary>
    /// <summary>
    /// Consecutive segments that could not be transcribed at all. A missing
    /// speech engine fails identically every five seconds forever, and a loop
    /// that retries it writes an error a minute and holds the microphone open
    /// while never once being able to hear the wake word. Counting the failures
    /// lets it give up and say why.
    /// </summary>
    private int _listeningFailures;

    private const int MaximumListeningFailures = 3;

    private async Task<string?> CaptureSegmentAsync(CancellationToken cancellationToken)
    {
        try
        {
            _recorder.Start(Settings.PreferredMicrophoneId);
            await Task.Delay(ListeningSegment, cancellationToken);
            var recording = await _recorder.StopAsync(cancellationToken);
            if (recording is null || recording.Duration < TimeSpan.FromMilliseconds(400))
            {
                return null;
            }

            var transcript = await TranscribeAsync(recording, cancellationToken);
            _listeningFailures = 0;
            return transcript;
        }
        catch (OperationCanceledException)
        {
            _recorder.Cancel();
            throw;
        }
        catch (Exception exception)
        {
            _recorder.Cancel();
            _listeningFailures++;

            // Logged once with the reason, then only counted. Repeating the
            // same stack every five seconds buries everything else in the log.
            if (_listeningFailures == 1)
            {
                _log.Error("A listening segment could not be transcribed.", exception);
            }

            if (_listeningFailures >= MaximumListeningFailures)
            {
                StopActiveListening(
                    "Listening needs speech to text set up — open Setup and choose AssemblyAI, or install Whisper.");
            }

            return null;
        }
    }

    private async Task<string?> TranscribeAsync(RecordedAudio recording, CancellationToken cancellationToken)
    {
        if (Settings.SpeechToTextProvider == "AssemblyAI")
        {
            var result = await _assemblyAi.TranscribeAsync(
                RequireAssemblyAiApiKey(), Settings.AssemblyAiModel, recording, cancellationToken);
            return result.Text;
        }

        var whisper = await _whisperCpp.TranscribeAsync(
            ResolveLocalPath(Settings.WhisperCppExecutablePath),
            ResolveLocalPath(Settings.WhisperCppModelPath),
            recording,
            cancellationToken);
        return whisper.Text;
    }

    public async Task ClearMemoryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _memory.ClearAsync(cancellationToken);
        _taskContext.Complete();
        SetStatus("Cleared what Metis remembered about your skills and tasks");
        _log.Info("Skill and task memory cleared at the user's request.");
    }

    public void CancelCurrentTurn()
    {
        _turnCancellation?.Cancel();
        _recorder.Cancel();
        _audioPlayback.Stop();
        GuidanceOverlayRequested?.Invoke(this, GuidanceOverlayRequest.Clear);
        SetActivity(MetisActivityKind.Stopped, "Stopped");
        PlayCue(MetisSound.Stopped);
        State.Force(AssistantState.Idle);
        SetStatus("Stopped");
    }

    private void OnActiveListeningToggled(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            ToggleActiveListening();
        }
    }

    private void OnPushToTalkPressed(object? sender, EventArgs e)
    {
        if (_disposed || _recorder.IsRecording || _turnGate.CurrentCount == 0)
        {
            return;
        }

        if (!HasConfiguredProviderKey())
        {
            ReportError($"Add a {ProviderDisplayName(Settings.AiProvider)} API key in Setup before using voice input.");
            return;
        }

        try
        {
            _pendingPointer = null;
            PlayCue(MetisSound.RecordingStarted);
            _recorder.Start(Settings.PreferredMicrophoneId);
            State.Force(AssistantState.Listening);
            SetActivity(MetisActivityKind.Listening, "Listening");
            SetStatus("Listening — release Ctrl+Shift+1 to ask");
        }
        catch (Exception exception)
        {
            ReportException("Metis could not start the microphone", exception);
        }
    }

    private void OnPushToTalkReleased(object? sender, EventArgs e)
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        _pendingActivation = ActivationKind.PushToTalk;
        _pendingPointer = null;
        _ = CompleteVoiceTurnAsync();
    }

    /// <summary>
    /// Ctrl+Alt starts a context request. The pointer position is captured at
    /// press time, before the user moves the mouse toward whatever they are
    /// about to describe.
    /// </summary>
    private void OnContextActivationPressed(object? sender, ActivationKind activation)
    {
        if (_disposed || _recorder.IsRecording || _turnGate.CurrentCount == 0)
        {
            return;
        }

        if (!HasConfiguredProviderKey())
        {
            ReportError($"Add a {ProviderDisplayName(Settings.AiProvider)} API key in Setup before using voice input.");
            return;
        }

        try
        {
            var (pointerX, pointerY) = Cursor.GetPosition();
            _pendingPointer = new PointerContext(pointerX, pointerY, 0, 0);
            if (activation == ActivationKind.Inspect)
            {
                // Tracing owns this chord. The microphone stays shut: the
                // surface now waits for an area to be marked, and a recording
                // started here would fire a turn the moment the keys came up,
                // long before the user had drawn anything.
                // No ring at the cursor: the user is about to draw their own
                // shape, so marking where the pointer happens to rest is noise
                // — and collides with the toolbar when they reach for it.
                PlayCue(MetisSound.InspectPressed);
                TraceArmRequested?.Invoke(this, EventArgs.Empty);
                SetActivity(MetisActivityKind.Listening, "Mark an area");
                SetStatus("Mark an area on screen, or press Esc");
                return;
            }

            // Ctrl+Alt without Shift is still hold-to-talk about the screen.
            PlayCue(MetisSound.RecordingStarted);
            _recorder.Start(Settings.PreferredMicrophoneId);
            State.Force(AssistantState.Listening);
            SetActivity(MetisActivityKind.Listening, "Listening");
            SetStatus("Listening — release Ctrl+Alt to ask about what you see");
        }
        catch (Exception exception)
        {
            ReportException("Metis could not start the microphone", exception);
        }
    }

    /// <summary>Asks the trace surface to arm.</summary>
    public event EventHandler? TraceArmRequested;

    /// <summary>
    /// Raised when Escape is pressed while a trace is on screen. Routed through
    /// the global hook because the trace surface is never activated and so can
    /// never receive a key press of its own.
    /// </summary>
    public event EventHandler? TraceCancelKeyPressed;

    /// <summary>
    /// Hands Escape to Metis for as long as a trace surface is up, and gives it
    /// back the moment that surface goes away.
    /// </summary>
    public void SetTraceCancelKeyEnabled(bool enabled) => _pushToTalk.CancelKeyEnabled = enabled;

    /// <summary>Asks the trace surface to end the gesture and report it.</summary>
    public event EventHandler? TraceCommitRequested;

    /// <summary>
    /// Accepts a region the user traced. It becomes the focus of the request in
    /// flight: the screenshot is cropped to it and the answer must concern it.
    /// </summary>
    public void SetTracedRegion(IReadOnlyList<GuidancePoint> path)
    {
        if (path is null || path.Count < 3)
        {
            return;
        }

        _pendingTrace = path;

        var (x, y, width, height) = TracePath.Bounds(path, padding: 6);
        var centre = TracePath.Centre(path);
        _pendingPointer = new PointerContext(centre.ScreenX, centre.ScreenY, 0, 0);

        // Echo the loop back in Metis's own ink so the user sees exactly what
        // was captured before they finish speaking.
        if (Settings.VisualGuidanceEnabled)
        {
            GuidanceOverlayRequested?.Invoke(
                this,
                new GuidanceOverlayRequest(
                    [new GuidanceMark(GuidanceMarkKind.Lasso, centre.ScreenX, centre.ScreenY, width, height, null, 0, path)],
                    DimBackground: false,
                    TimeSpan.FromMinutes(5)));
        }

        _log.Info($"Traced region {width}x{height} at {x},{y} with {path.Count} points.");

        // Marking an area is the question. Metis continues by itself rather
        // than waiting for the user to say the obvious thing out loud.
        _pendingActivation = ActivationKind.Inspect;
        _ = RunTurnAsync("Explain what is in the area I marked on screen.", null, CancellationToken.None);
    }

    /// <summary>
    /// Shift arrived after the hold began, so what started as a voice request
    /// is really a trace. The microphone is closed and discarded, and the pen
    /// comes out — otherwise the chord's behaviour would depend on which of
    /// three keys the user happened to press a few milliseconds first.
    /// </summary>
    private void OnContextActivationUpgraded(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_recorder.IsRecording)
        {
            _recorder.Cancel();
        }

        _pendingActivation = ActivationKind.Inspect;
        var (pointerX, pointerY) = Cursor.GetPosition();
        _pendingPointer = new PointerContext(pointerX, pointerY, 0, 0);

        PlayCue(MetisSound.InspectPressed);
        TraceArmRequested?.Invoke(this, EventArgs.Empty);
        State.Force(AssistantState.Idle);
        SetActivity(MetisActivityKind.Listening, "Mark an area");
        SetStatus("Mark an area on screen, or press Esc");
    }

    private void OnContextActivationReleased(object? sender, ActivationKind activation)
    {
        if (activation == ActivationKind.Inspect)
        {
            // Tracing owns this chord; the surface stays up until an area is
            // marked, so releasing the keys does nothing at all.
            PlayCue(MetisSound.InspectReleased);
            return;
        }

        if (!_recorder.IsRecording)
        {
            return;
        }

        if (activation == ActivationKind.Inspect)
        {
            PlayCue(MetisSound.InspectReleased);
            TraceCommitRequested?.Invoke(this, EventArgs.Empty);
        }

        _pendingActivation = activation;
        if (activation != ActivationKind.Inspect)
        {
            // Only Inspect resolves the exact control; a plain context request
            // is about the screen as a whole.
            _pendingPointer = null;
        }

        _ = CompleteVoiceTurnAsync();
    }

    private void OnEmergencyStopPressed(object? sender, EventArgs e)
    {
        // Keep the low-level hook callback extremely short: cancel and drain
        // the action path immediately, then perform UI/audio cleanup off-hook.
        _turnCancellation?.Cancel();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            PlayCue(MetisSound.Stopped);
            SetActivity(MetisActivityKind.Stopped, "Emergency stop");
            _recorder.Cancel();
            _audioPlayback.Stop();
            State.Force(AssistantState.Paused);
            SetStatus("Emergency stop — automation queue cleared. Start a new request to resume.");
            _log.Info("F12 emergency stop cleared and cancelled desktop automation.");
        });
    }

    private async Task CompleteVoiceTurnAsync()
    {
        try
        {
            var recording = await _recorder.StopAsync();
            if (recording is null || recording.Duration < TimeSpan.FromMilliseconds(250))
            {
                State.Force(AssistantState.Idle);
                SetStatus("No speech captured — hold the shortcut a little longer");
                return;
            }

            // The send cue waits until the recording passes the length check, so
            // a stray Ctrl+Alt tap stays silent instead of chirping twice.
            PlayCue(MetisSound.RequestSent);
            MessageAdded?.Invoke(
                this,
                new AssistantMessage(
                    AssistantRole.User,
                    $"Voice request ({recording.Duration.TotalSeconds:0.0}s)",
                    DateTimeOffset.Now));
            await RunTurnAsync(
                SpokenRequest.Placeholder,
                recording,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            ReportException("Metis could not finish the voice request", exception);
        }
    }

    private async Task RunTurnAsync(string prompt, RecordedAudio? recording, CancellationToken externalCancellation)
    {
        if (!await _turnGate.WaitAsync(0, externalCancellation))
        {
            SetStatus("Metis is already answering — stop or wait for the current reply");
            return;
        }

        _turnCancellation?.Dispose();
        _turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _turnCancellation.CancelAfter(recording is null
            ? TimeSpan.FromSeconds(75)
            : TimeSpan.FromSeconds(120));
        var cancellationToken = _turnCancellation.Token;
        var activation = _pendingActivation;
        var pendingPointer = _pendingPointer;
        var pendingTrace = _pendingTrace;
        _pendingTrace = null;

        // Metis only ever teaches now. The mode is a vestige the task and
        // memory records still carry, pinned to its one remaining value.
        const OperatingMode mode = OperatingMode.Guide;

        try
        {
            State.Force(AssistantState.Thinking);
            SetStatus(recording is null ? "Thinking…" : "Understanding your voice and screen…");

            Task<TranscriptionResult>? transcriptionTask = null;
            if (recording is not null && Settings.SpeechToTextProvider == "AssemblyAI")
            {
                // Transcription is independent from screen capture. Starting it
                // now overlaps its upload/polling with capture and the UIA scan.
                transcriptionTask = _assemblyAi.TranscribeAsync(
                    RequireAssemblyAiApiKey(),
                    Settings.AssemblyAiModel,
                    recording,
                    cancellationToken);
            }
            else if (recording is not null && Settings.SpeechToTextProvider == "Whisper.cpp")
            {
                transcriptionTask = _whisperCpp.TranscribeAsync(
                    ResolveLocalPath(Settings.WhisperCppExecutablePath),
                    ResolveLocalPath(Settings.WhisperCppModelPath),
                    recording,
                    cancellationToken);
            }

            ScreenCapture? screenshot = null;
            // A fresh, complete virtual-desktop frame accompanies every turn
            // while screen context is enabled. Keyword gating made ordinary
            // follow-up prompts silently lose the screen.
            var shouldCaptureScreen = Settings.CaptureActiveWindow;
            if (shouldCaptureScreen)
            {
                try
                {
                    SetActivity(MetisActivityKind.Capturing, "Capturing screen");
                    screenshot = await _capture.CaptureActiveWindowAsync(cancellationToken);
                    if (screenshot is not null)
                    {
                        SetActivity(MetisActivityKind.Capturing, "Screen captured");
                        _log.Info($"Captured screen context with {screenshot.CaptureBackend} " +
                                  $"at encoded {screenshot.Width}x{screenshot.Height}; " +
                                  $"full bounds left={screenshot.ScreenLeft}, top={screenshot.ScreenTop}, " +
                                  $"width={screenshot.SourceWidth}, height={screenshot.SourceHeight}; " +
                                  $"{screenshot.ImageBytes.Length / 1024d:0.0} KiB {screenshot.ImageMimeType}.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception captureError)
                {
                    _log.Error("Full-desktop capture failed; continuing without an image.", captureError);
                    SetStatus("Full-screen capture was unavailable; asking from voice/text only…");
                }
            }

            if (RequiresScreenObservation(prompt) && screenshot is null)
            {
                throw new InvalidOperationException(
                    "Metis could not capture the application behind its window, so it will not guess what is on your screen. " +
                    "Make sure Screen context is enabled and keep the target application open.");
            }

            string? automationContext = null;
            if (screenshot is not null)
            {
                try
                {
                    automationContext = await _uiAutomation.DescribeWindowAsync(screenshot, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception automationContextError)
                {
                    _log.Error("UI Automation context was unavailable; continuing with vision only.", automationContextError);
                }
            }

            var effectivePrompt = prompt;
            var providerRecording = recording;
            if (transcriptionTask is not null)
            {
                SetStatus(Settings.SpeechToTextProvider == "Whisper.cpp"
                    ? "whisper.cpp is transcribing locally…"
                    : "AssemblyAI is transcribing your voice…");
                var transcript = await transcriptionTask;
                effectivePrompt = transcript.Text;
                providerRecording = null;
                _log.Info($"Voice request transcribed with {transcript.Provider} {transcript.Model}.");
            }
            else if (recording is not null &&
                     Settings.AiProvider is "Claude" or "OpenClaw" or "Ollama")
            {
                throw new InvalidOperationException(
                    $"{Settings.AiProvider} does not transcribe Metis's WAV recording directly. " +
                    "Choose AssemblyAI under Voice & input, then try the shortcut again.");
            }

            var pointer = await BuildPointerContextAsync(
                pendingPointer,
                activation,
                screenshot,
                cancellationToken);
            var task = _taskContext.BeginTurn(
                effectivePrompt,
                screenshot?.WindowTitle ?? "unknown application",
                mode);
            var skillContext = await DescribeSkillsAsync(screenshot?.WindowTitle, cancellationToken);

            // A change of subject starts its own chat, so recall later finds
            // "the time we worked on the video" rather than one endless thread.
            if (Settings.ChatMemoryEnabled && ChatRecall.StartsNewSubject(_currentChat, effectivePrompt))
            {
                StartNewChat(screenshot?.WindowTitle);
            }

            RecordChatTurn("user", effectivePrompt, screenshot?.WindowTitle);

            var region = BuildRegion(pendingTrace, screenshot);
            if (region is not null && screenshot is not null)
            {
                // Send only what was circled. Sharper answers, and a fraction
                // of the image tokens of a full desktop.
                screenshot = ScreenCaptureCropper.Crop(screenshot, region, _log.Info);
            }
            var selectedSkills = SkillLibrary.Select(_userSkills, screenshot?.WindowTitle, effectivePrompt);
            var taughtSkills = SkillLibrary.Describe(selectedSkills);

            // Drawing an idea on a blank canvas and pointing at something on the
            // real screen are opposite jobs, and the canvas one tells the model
            // to ignore the screen entirely. So it has to be the rarer, narrower
            // case, or it swallows every question the user asks about what is in
            // front of them.
            //
            // Two things are required before Metis abandons the screen. The
            // subject has to come from the user's own words, not from whatever
            // window happens to be open — a video about triangles must not turn
            // "what is this button" into a geometry lesson. And the request must
            // not be about the screen at all, because a request that is has an
            // answer on the screen, and that answer is a mark on it.
            var academicByRequest = SkillLibrary
                .Select(_userSkills, application: null, effectivePrompt)
                .Any(skill => skill.Domain == SkillDomain.Academic);

            _academicTeaching = LessonStepRouting.ShouldIllustrateSubject(
                academicByRequest,
                RequiresScreenObservation(effectivePrompt));

            if (academicByRequest && !_academicTeaching)
            {
                _log.Info(
                    "The request names a subject Metis can draw, but it also asks about the screen, " +
                    "so it is answered by marking the screen rather than by drawing a diagram.");
            }
            var recall = Settings.ChatMemoryEnabled
                ? ChatRecall.Describe(_chatSessions, _currentChat.Id, effectivePrompt, screenshot?.WindowTitle)
                : null;

            var request = new GeminiRequest(
                effectivePrompt,
                screenshot?.ImageBytes,
                providerRecording?.WavBytes,
                screenshot?.WindowTitle,
                automationContext,
                screenshot?.ImageMimeType ?? "image/png",
                screenshot?.Width ?? 0,
                screenshot?.Height ?? 0,
                screenshot?.ScreenLeft ?? 0,
                screenshot?.ScreenTop ?? 0,
                screenshot?.SourceWidth ?? 0,
                screenshot?.SourceHeight ?? 0,
                mode,
                activation,
                pointer,
                _taskContext.Describe(),
                skillContext,
                taughtSkills,
                recall,
                region,
                _academicTeaching);
            SetActivity(MetisActivityKind.Thinking, "Thinking");
            var response = await GenerateWithSelectedProviderAsync(request, cancellationToken);

            MessageAdded?.Invoke(
                this,
                new AssistantMessage(AssistantRole.Metis, response.Text, DateTimeOffset.Now));
            RecordChatTurn("metis", response.Text, screenshot?.WindowTitle);

            var finalStatus = $"Answered with {response.Provider} {response.Model}";

            // Counted here rather than at each provider call site, so every
            // path lands in the same ledger whichever one answered.
            ModelUsage.Record(response.Model, DateTimeOffset.Now);

            var rawPlan = response.Plan ?? AssistantPlan.SpeechOnly(response.Text);

            // On the direct-audio path effectivePrompt is still Metis's own
            // stand-in — the real words only exist in what the model reported
            // hearing. Everything Metis does is teaching now, so these words no
            // longer decide what it is allowed to do; they still decide whether
            // the honest answer is a mark on the screen or a sentence about it.
            var spokenRequest = (SpokenRequest.IsPlaceholder(effectivePrompt)
                ? rawPlan.HeardText
                : effectivePrompt) ?? string.Empty;

            if (SpokenRequest.IsPlaceholder(effectivePrompt))
            {
                _log.Info(string.IsNullOrWhiteSpace(spokenRequest)
                    ? "No heard_text came back for a spoken request, so Metis has only the recording to go on."
                    : $"Spoken request heard as: \"{spokenRequest}\"");
            }

            var plan = rawPlan;

            // A screen answer has to be grounded in a real screenshot. Either
            // the request needs the screen, or the reply put a mark on it, or a
            // lesson step points somewhere — any of those and Metis must have
            // actually looked, and the model must confirm it did.
            var pointsAtScreen = plan.HasAnnotation ||
                                 plan.LessonSteps.Any(step => step.HasTarget);
            var screenGroundingRequired = RequestIntent.RequiresScreenObservation(spokenRequest) ||
                                          pointsAtScreen;
            if (screenGroundingRequired && screenshot is null)
            {
                throw new InvalidOperationException(
                    "Metis could not capture the target window, so it refused to invent screen details or coordinates.");
            }

            if (screenGroundingRequired && !plan.ScreenObserved)
            {
                throw new InvalidOperationException(
                    "The AI did not confirm that it inspected Metis's current screenshot, so no screen answer was trusted.");
            }

            var bubbleCue = string.IsNullOrWhiteSpace(plan.BubbleCue) ? string.Empty : plan.BubbleCue.Trim();
            _log.Info($"Assistant reply received: {plan.LessonSteps.Count} lesson step(s), " +
                      $"{(plan.HasAnnotation ? "an annotation" : "no annotation")}, " +
                      $"screen context {(screenshot is null ? "unavailable" : "available")}.");

            // An answer carrying steps becomes a lesson Metis walks through,
            // marking the screen for each one, rather than a single reply said
            // once and forgotten.
            if (plan.LessonSteps.Count > 0)
            {
                _lastLessonCapture = screenshot;
                _lessonFallbackTarget = plan.HasAnnotation ? plan.ToAnnotationTarget() : null;
                await RecordTurnMemoryAsync(task, plan, screenshot?.WindowTitle, true, CancellationToken.None);
                await RunLessonAsync(
                    new LessonState(plan.Goal ?? spokenRequest, plan.LessonSteps),
                    cancellationToken);

                SetActivity(MetisActivityKind.Idle, string.Empty);
                State.Force(AssistantState.Idle);
                return;
            }

            // "Show me where X is" is answered by a mark, not by prose. When the
            // reply named a spot, or the model answered in words and the request
            // was asking where something is, Metis finds the control itself and
            // points at it.
            var companionResponseStarted = false;
            var willPoint = plan.HasAnnotation ||
                            (RequestIntent.IsPointingRequest(spokenRequest) && screenshot is not null);

            // Answer in the way you were asked. A typed question gets a written
            // reply beside the cursor; speaking to Metis gets speech back.
            var speakReply = Settings.SpeechEnabled && activation != ActivationKind.Typed;
            if (speakReply)
            {
                SetStatus("Preparing Metis's voice…");
                try
                {
                    var audio = await SynthesizeWithProviderAsync(response, cancellationToken);
                    if (audio is not null)
                    {
                        State.Force(AssistantState.Speaking);
                        SetActivity(MetisActivityKind.Speaking, "Speaking");
                        SetStatus("Speaking…");
                        var spokenLine = CompanionSpeech.ChooseLine(plan.SpokenText, bubbleCue);
                        if (spokenLine is not null)
                        {
                            StartCompanionResponse(spokenLine, GetAudioDuration(audio), showBubble: true);
                            companionResponseStarted = true;
                        }

                        await _audioPlayback.PlayAsync(audio, AudioPriority.Speech, cancellationToken);
                    }
                    else
                    {
                        _log.Info(
                            $"No speech audio came back for the reply (voice '{Settings.TextToSpeechProvider}', " +
                            $"provider '{response.Provider}'), so the answer was shown but not spoken.");
                        finalStatus += " — voice was unavailable";
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception speechError)
                {
                    _log.Error("Speech output failed; the text answer remains available.", speechError);
                    finalStatus += " — speech failed, text still works";
                }
            }

            // Point at what the reply was about, after the sentence rather than
            // over it. This is the whole of what Metis does to the screen now:
            // it marks, and the user acts.
            if (screenshot is not null && willPoint)
            {
                await PointAtAnswerAsync(spokenRequest, plan, screenshot, cancellationToken);
            }

            // With no voice, the written answer is the answer. A typed turn
            // always gets it beside the cursor, paced as if it were being read.
            if (!companionResponseStarted && !willPoint)
            {
                var writtenLine = CompanionSpeech.ChooseWrittenLine(plan.SpokenText, bubbleCue);
                StartCompanionResponse(
                    writtenLine ?? string.Empty,
                    CompanionSpeech.ReadingDuration(writtenLine),
                    writtenLine is not null);
            }

            await RecordTurnMemoryAsync(task, plan, screenshot?.WindowTitle, true, CancellationToken.None);

            State.Force(AssistantState.Success);
            SetActivity(MetisActivityKind.Complete, "Done");
            PlayCue(MetisSound.TaskComplete);
            SetStatus(finalStatus);
            await Task.Delay(GuidanceTuning.Scale(TimeSpan.FromSeconds(1.2)), cancellationToken);
            SetActivity(MetisActivityKind.Idle, string.Empty);
            State.Force(AssistantState.Idle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State.Force(AssistantState.Idle);
            SetStatus("Request stopped or timed out");
        }
        catch (Exception exception)
        {
            ReportException("Metis could not get an answer", exception);
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private string RequireApiKey()
    {
        var key = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No Gemini API key is saved. Open Setup and add one first.");
        }

        return key;
    }

    /// <summary>
    /// Resolves the pointer into capture-normalized coordinates and, for an
    /// Inspect activation, the control it is over. Returns null rather than a
    /// guess when the coordinate falls outside the captured desktop.
    /// </summary>
    private async Task<PointerContext?> BuildPointerContextAsync(
        PointerContext? pointer,
        ActivationKind activation,
        ScreenCapture? capture,
        CancellationToken cancellationToken)
    {
        if (pointer is null || capture is null)
        {
            return null;
        }

        var sourceWidth = capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;
        var sourceHeight = capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var normalizedX = (int)Math.Round((pointer.ScreenX - capture.ScreenLeft) / (double)sourceWidth * 1000d);
        var normalizedY = (int)Math.Round((pointer.ScreenY - capture.ScreenTop) / (double)sourceHeight * 1000d);
        if (normalizedX is < 0 or > 1000 || normalizedY is < 0 or > 1000)
        {
            return null;
        }

        string? hovered = null;
        if (activation == ActivationKind.Inspect)
        {
            try
            {
                hovered = await _uiAutomation.DescribeElementAtAsync(
                    pointer.ScreenX,
                    pointer.ScreenY,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _log.Error("The control under the pointer could not be read; continuing with vision only.", exception);
            }

            // Draw toward the pointer so the user can see exactly what Metis
            // resolved "this" to before it answers. The same hand-drawn arrow
            // is used when Metis points something out, so the gesture means the
            // same thing in both directions.
            ShowPointerArrow(
                pointer.ScreenX,
                pointer.ScreenY,
                hovered is null ? "Looking here" : null);
        }

        return pointer with { NormalizedX = normalizedX, NormalizedY = normalizedY, HoveredElement = hovered };
    }

    private async Task<string?> DescribeSkillsAsync(string? application, CancellationToken cancellationToken)
    {
        if (!Settings.MemoryEnabled)
        {
            return null;
        }

        try
        {
            var document = await _memory.LoadAsync(cancellationToken);
            return document.Skills.Count == 0
                ? null
                : SkillMemoryEngine.Describe(document.Skills, application);
        }
        catch (Exception exception)
        {
            _log.Error("Metis could not read its skill memory; continuing without it.", exception);
            return null;
        }
    }

    /// <summary>
    /// Records what the turn taught and what it accomplished. Only the goal and
    /// skill names are stored; screen content never reaches memory.
    /// </summary>
    private async Task RecordTurnMemoryAsync(
        AgentTaskState task,
        AssistantPlan plan,
        string? application,
        bool success,
        CancellationToken cancellationToken)
    {
        if (!Settings.MemoryEnabled)
        {
            return;
        }

        try
        {
            _taskContext.RecordProgress(
                plan.LessonSteps.Count == 0 ? null : $"{plan.LessonSteps.Count} step lesson",
                plan.BubbleCue ?? plan.Goal);

            await _memory.RecordTaskOutcomeAsync(
                _taskContext.Current ?? task,
                success,
                plan.SpokenText,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(plan.Goal))
            {
                // The user performed the step themselves, so the guidance flag
                // records that Metis had to show them.
                var progress = await _memory.RecordSkillUseAsync(
                    application ?? "Windows",
                    plan.Goal!,
                    success,
                    neededGuidance: true,
                    cancellationToken);
                AnnounceProgress(progress);
            }
        }
        catch (Exception exception)
        {
            _log.Error("Metis could not update its memory for this turn.", exception);
        }
    }

    /// <summary>
    /// Runs a lesson: show one step, wait for the learner to do it, confirm it
    /// on screen, then move on. Metis performs nothing itself — the screen is
    /// watched for the learner's work rather than for its own.
    /// </summary>
    private async Task RunLessonAsync(LessonState lesson, CancellationToken cancellationToken)
    {
        CompanionDetachRequested?.Invoke(this, true);
        try
        {
            while (!lesson.IsFinished && !cancellationToken.IsCancellationRequested)
            {
                var step = lesson.Current;
                if (step is null)
                {
                    break;
                }

                LessonChanged?.Invoke(this, lesson);
                var held = await PresentLessonStepAsync(lesson, step, cancellationToken);

                // Metis does not wait to be caught up with. It says the step,
                // holds the mark long enough to be followed, then carries on —
                // because a walkthrough that stops until you have done each
                // thing cannot be listened to while you work, which is the
                // whole point of being talked through something.
                await Task.Delay(held, cancellationToken);

                // Read the screen again before marking the next step. Whatever
                // has happened in between — the user acting, a dialog opening,
                // the app moving on — the next mark is placed against what is
                // there now rather than against a screenshot from a minute ago.
                lesson = lesson.Advance();
                if (!lesson.IsFinished)
                {
                    await RefreshLessonCaptureAsync(cancellationToken);
                }
            }

            if (lesson.IsFinished && !cancellationToken.IsCancellationRequested)
            {
                LessonChanged?.Invoke(this, lesson);
                SetActivity(MetisActivityKind.Complete, "Lesson complete");
                await SpeakLessonLineAsync($"That's it. You've finished {lesson.Goal}.", cancellationToken);
                await RecordLessonSkillAsync(lesson, cancellationToken);
            }
        }
        finally
        {
            CompanionDetachRequested?.Invoke(this, false);
            GuidanceOverlayRequested?.Invoke(this, GuidanceOverlayRequest.Clear);
        }
    }

    /// <summary>
    /// Takes a fresh screenshot for the next step's annotation to be placed
    /// against. A failure here is not fatal: the previous capture is kept, so
    /// the next mark is placed against a slightly older screen rather than not
    /// drawn at all.
    /// </summary>
    private async Task RefreshLessonCaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            var capture = await _capture.CaptureActiveWindowAsync(cancellationToken);
            if (capture is not null)
            {
                _lastLessonCapture = capture;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Error("Metis could not re-read the screen between steps.", exception);
        }
    }

    /// <summary>
    /// Shows one step: marks the control, sends the companion to it, and says
    /// the instruction together with the reason it matters. Returns how long
    /// the mark should be left up before the walkthrough moves on.
    /// </summary>
    private async Task<TimeSpan> PresentLessonStepAsync(LessonState lesson, LessonStep step, CancellationToken cancellationToken)
    {
        SetActivity(MetisActivityKind.Verifying, step.Instruction, lesson.StepNumber, lesson.Steps.Count);

        // A drawn step goes nowhere near the annotation resolver. That resolver
        // finds real controls, and it would succeed at that even here — marking
        // whatever the user happens to have open underneath an invented shape.
        //
        // A step carrying both is pointing at something real and illustrating it
        // as well, and the real thing wins: a mark on the actual control is the
        // answer the user asked for, where an invented shape drawn over their
        // work is not.
        if (LessonStepRouting.ShouldDrawOnCanvas(step, _academicTeaching))
        {
            return await PresentDiagramStepAsync(lesson, step, cancellationToken);
        }

        var hold = AnnotationDuration.Standard;

        // A step that names its own target uses it. One that does not falls back
        // to the spot the reply named, so a lesson still points at the thing it
        // is describing instead of silently marking nothing.
        var stepTarget = LessonStepRouting.RequiresRealScreenAnnotation(step)
            ? step.ToAnnotationTarget() with
            {
                Label = step.TargetLabel ?? ShortenForLabel(step.Instruction)
            }
            : _lessonFallbackTarget is { } fallback
                ? fallback with { Label = step.TargetLabel ?? ShortenForLabel(step.Instruction) }
                : null;

        if (stepTarget is null || _lastLessonCapture is null)
        {
            // The one path that used to produce no mark, no movement and no
            // trace in the log, which made a step that pointed at nothing look
            // identical to one that was never drawn.
            _log.Info(
                $"Step {lesson.StepNumber} marked nothing (named={step.HasNamedTarget}, coords={step.HasTarget}): " +
                $"{(stepTarget is null ? "the step named no target and the reply named no spot" : "no screen capture was kept for this lesson")}.");
        }

        if (stepTarget is not null && _lastLessonCapture is { } capture)
        {
            // The hold is decided from the target once it has been resolved, so
            // it reflects the control's real size rather than the model's guess
            // at it. Marked first with the standard hold and corrected below,
            // because the overlay needs a duration at the moment it draws.
            var annotation = await AnnotateAsync(
                stepTarget,
                capture,
                lesson.StepNumber,
                cancellationToken);

            if (annotation is not null)
            {
                hold = AnnotationDuration.For(annotation, VirtualScreenArea());
            }

            // Fly the companion to where the annotation actually landed, not to
            // where the model guessed. When the two differ, the mark is right
            // and following the guess would send the cursor beside it.
            var (screenX, screenY) = annotation is null
                ? ToScreenPoint(stepTarget.NormalizedX, stepTarget.NormalizedY, capture)
                : (annotation.ScreenX, annotation.ScreenY);

            if (step.HasGesture)
            {
                // A movement has to be performed, not pointed at. The ghost
                // cursor walks it, then hands the companion back.
                var (endX, endY) = ToScreenPoint(step.DragToX, step.DragToY, capture);
                CompanionDemoRequested?.Invoke(
                    this,
                    new CompanionDemo(
                        [new GuidancePoint(screenX, screenY), new GuidancePoint(endX, endY)],
                        step.TargetLabel,
                        HoldAtEnd: true));
            }
            else
            {
                CompanionGuidanceRequested?.Invoke(
                    this,
                    new CompanionGuidance(screenX, screenY, step.TargetLabel ?? "Here", hold));
            }
        }

        var line = string.IsNullOrWhiteSpace(step.Why) ? step.Instruction : $"{step.Instruction} {step.Why}";
        MessageAdded?.Invoke(
            this,
            new AssistantMessage(AssistantRole.Metis, $"Step {lesson.StepNumber}. {line}", DateTimeOffset.Now));

        // Speaking is awaited, so the hold that follows is time to look at the
        // mark rather than time spent talking over it. A step whose sentence
        // runs longer than the hold has already had its pause.
        var spokenFrom = DateTimeOffset.UtcNow;
        await SpeakLessonLineAsync(line, cancellationToken);
        var spent = DateTimeOffset.UtcNow - spokenFrom;

        var remaining = hold - spent;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Shows one stage of a drawn explanation: adds a shape to the canvas and
    /// says the sentence that goes with it.
    ///
    /// The counterpart to <see cref="PresentLessonStepAsync"/>'s annotation
    /// branch, and deliberately sharing none of it. There is no screenshot to
    /// resolve against, no control to snap to, and no ghost cursor to fly —
    /// there is a blank canvas and a shape to put on it.
    /// </summary>
    /// <summary>
    /// Walks the companion along a shape that has just been drawn, or sends it
    /// to the shape when there is no line to follow.
    ///
    /// A traced path is the point of this: explaining a vector while the pointer
    /// travels its length reads as being shown something, where a pointer that
    /// teleports to the middle and stops reads as a label. Long point lists are
    /// thinned first — the companion glides between each pair in turn, so
    /// walking all forty points of a circle would crawl.
    /// </summary>
    private void SendCompanionAlong(GuidanceMark mark, string? label, TimeSpan hold)
    {
        var path = TraceablePath(mark.Points);
        if (path is { Count: >= 2 })
        {
            CompanionDemoRequested?.Invoke(this, new CompanionDemo(path, label, HoldAtEnd: true));
            return;
        }

        CompanionGuidanceRequested?.Invoke(
            this,
            new CompanionGuidance(mark.ScreenX, mark.ScreenY, label ?? "Here", hold));
    }

    /// <summary>
    /// Thins a shape's outline to a handful of points the companion can sweep
    /// through, keeping the first and last so the trace starts and ends where
    /// the shape does.
    /// </summary>
    private static IReadOnlyList<GuidancePoint>? TraceablePath(IReadOnlyList<GuidancePoint>? points)
    {
        const int mostLegs = 8;
        if (points is null || points.Count < 2)
        {
            return null;
        }

        if (points.Count <= mostLegs)
        {
            return points;
        }

        var stride = (double)(points.Count - 1) / (mostLegs - 1);
        var thinned = new List<GuidancePoint>(mostLegs);
        for (var index = 0; index < mostLegs; index++)
        {
            thinned.Add(points[(int)Math.Round(index * stride)]);
        }

        return thinned;
    }

    private async Task<TimeSpan> PresentDiagramStepAsync(
        LessonState lesson,
        LessonStep step,
        CancellationToken cancellationToken)
    {
        var hold = DiagramStepDuration.For(step);

        if (Settings.VisualGuidanceEnabled)
        {
            var canvas = CurrentDiagramCanvas();
            var mark = DiagramMarkBuilder.Build(step, canvas);
            if (mark is not null)
            {
                // The first shape replaces whatever was on screen; the ones
                // after it join what the earlier steps drew, which is what makes
                // a diagram build up rather than flicker between stages.
                var accumulate = lesson.CurrentIndex > 0 &&
                                 lesson.Steps[lesson.CurrentIndex - 1].HasDiagram;

                GuidanceOverlayRequested?.Invoke(
                    this,
                    new GuidanceOverlayRequest([mark], DimBackground: false, hold, accumulate));

                // Follow the shape as it appears. A teacher does not draw a
                // vector and then stand still — the hand travels along what is
                // being described, and that movement is half of the
                // explanation. Without this the diagram grew while the pointer
                // sat wherever it had been left.
                SendCompanionAlong(mark, step.TargetLabel ?? ShortenForLabel(step.Instruction), hold);

                _log.Info(
                    $"Diagram step {lesson.StepNumber}: drew {DiagramShapeKinds.Name(step.Diagram)} " +
                    $"at {mark.ScreenX},{mark.ScreenY} on a {canvas.Side}px canvas, " +
                    $"{(accumulate ? "added to" : "replacing")} the canvas, held {hold.TotalSeconds:0.0}s.");
            }
        }

        var line = string.IsNullOrWhiteSpace(step.Why) ? step.Instruction : $"{step.Instruction} {step.Why}";
        MessageAdded?.Invoke(
            this,
            new AssistantMessage(AssistantRole.Metis, $"Step {lesson.StepNumber}. {line}", DateTimeOffset.Now));

        var spokenFrom = DateTimeOffset.UtcNow;
        await SpeakLessonLineAsync(line, cancellationToken);
        var spent = DateTimeOffset.UtcNow - spokenFrom;

        var remaining = hold - spent;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Where a diagram is drawn: a square on the primary monitor.
    ///
    /// Square so shapes keep their proportions, and one monitor so a shape
    /// never straddles the gap between two screens. Deliberately unrelated to
    /// whatever window was last captured — a diagram has nothing to do with the
    /// app the user happens to have open.
    /// </summary>
    private static DiagramCanvas CurrentDiagramCanvas() => DiagramCanvas.Centred(
        0,
        0,
        (int)System.Windows.SystemParameters.PrimaryScreenWidth,
        (int)System.Windows.SystemParameters.PrimaryScreenHeight,
        0.6);

    private async Task SpeakLessonLineAsync(string line, CancellationToken cancellationToken)
    {
        // With speech off there is no clip to pace against, so the words reveal
        // at the reading estimate, as before.
        if (!Settings.SpeechEnabled)
        {
            StartCompanionResponse(line, null, showBubble: true);
            return;
        }

        try
        {
            // Synthesise first so the words on screen can be paced to the real
            // length of the clip. The lesson path used to show the text with no
            // duration — a fixed rate per word, unrelated to how long the
            // sentence actually takes to say — so the voice and the text drifted
            // apart. Now the reveal and the audio start together and finish
            // together.
            var audio = await SynthesizeTextAsync(line, null, cancellationToken);
            if (audio is not null)
            {
                StartCompanionResponse(line, GetAudioDuration(audio), showBubble: true);
                await _audioPlayback.PlayAsync(audio, AudioPriority.Speech, cancellationToken);
            }
            else
            {
                StartCompanionResponse(line, null, showBubble: true);
                _log.Info($"No speech audio came back for a lesson step, so it was shown but not spoken: \"{line}\"");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StartCompanionResponse(line, null, showBubble: true);
            _log.Error("Metis could not speak a lesson line.", exception);
        }
    }

    private async Task RecordLessonSkillAsync(LessonState lesson, CancellationToken cancellationToken)
    {
        if (!Settings.MemoryEnabled)
        {
            return;
        }

        // The learner did every step themselves but was shown each one, so this
        // counts as guided practice rather than a mastered skill.
        var progress = await _memory.RecordSkillUseAsync(
            _lastLessonCapture?.WindowTitle ?? "Windows",
            lesson.Goal,
            succeeded: true,
            neededGuidance: true,
            cancellationToken);
        AnnounceProgress(progress);
    }

    /// <summary>
    /// Trims an instruction down to something that fits on a screen badge.
    /// </summary>
    private static string ShortenForLabel(string text)
    {
        const int limit = 42;
        var trimmed = text.Trim();
        if (trimmed.Length <= limit)
        {
            return trimmed;
        }

        var cut = trimmed[..limit];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > limit / 2 ? cut[..lastSpace] : cut) + "…";
    }

    /// <summary>
    /// Converts a normalized size into screen pixels, with a floor so a mark
    /// around something tiny is still visible.
    /// </summary>
    /// <summary>
    /// Converts a traced path into normalized capture space. Returns null when
    /// there is no trace or no capture to measure it against, so the turn
    /// simply proceeds without a region rather than failing.
    /// </summary>
    private static ScreenRegion? BuildRegion(IReadOnlyList<GuidancePoint>? path, ScreenCapture? capture)
    {
        if (path is null || path.Count < 3 || capture is null)
        {
            return null;
        }

        var sourceWidth = capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;
        var sourceHeight = capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var (x, y, width, height) = TracePath.Bounds(path, padding: 6);
        var normalizedX = (int)Math.Round((x - capture.ScreenLeft) / (double)sourceWidth * 1000d);
        var normalizedY = (int)Math.Round((y - capture.ScreenTop) / (double)sourceHeight * 1000d);
        var normalizedWidth = (int)Math.Round(width / (double)sourceWidth * 1000d);
        var normalizedHeight = (int)Math.Round(height / (double)sourceHeight * 1000d);

        if (normalizedWidth <= 0 || normalizedHeight <= 0)
        {
            return null;
        }

        return new ScreenRegion(
            Math.Clamp(normalizedX, 0, 1000),
            Math.Clamp(normalizedY, 0, 1000),
            Math.Clamp(normalizedWidth, 1, 1000),
            Math.Clamp(normalizedHeight, 1, 1000),
            path);
    }

    private static (int Width, int Height) ToScreenSize(int normalizedWidth, int normalizedHeight, ScreenCapture capture)
    {
        var sourceWidth = capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;
        var sourceHeight = capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;
        return (
            Math.Max(28, (int)Math.Round(normalizedWidth / 1000d * sourceWidth)),
            Math.Max(24, (int)Math.Round(normalizedHeight / 1000d * sourceHeight)));
    }

    private static (int X, int Y) ToScreenPoint(int normalizedX, int normalizedY, ScreenCapture capture)
    {
        var width = capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;
        var height = capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;
        return (
            capture.ScreenLeft + (int)Math.Round(normalizedX / 1000d * width),
            capture.ScreenTop + (int)Math.Round(normalizedY / 1000d * height));
    }

    private static string Shorten(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + "…";

    /// <summary>
    /// Draws the hand-drawn arrow at a screen point. Used for the Inspect
    /// chord and whenever Metis points at a control, so pointing looks the same
    /// whoever initiated it.
    /// </summary>
    /// <summary>
    /// Marks what the Inspect chord is pointing at while the user is still
    /// holding it, so they can see what "this" resolved to before they speak.
    /// The hold is long because it must survive the whole request.
    /// </summary>
    private void ShowInspectTarget(int screenX, int screenY)
    {
        if (!Settings.VisualGuidanceEnabled)
        {
            return;
        }

        GuidanceOverlayRequested?.Invoke(
            this,
            new GuidanceOverlayRequest(
                [new GuidanceMark(GuidanceMarkKind.FocusRing, screenX, screenY, Width: 64, Height: 64)],
                DimBackground: false,
                TimeSpan.FromSeconds(30)));

        CompanionGuidanceRequested?.Invoke(
            this,
            new CompanionGuidance(screenX, screenY, "Reading this", TimeSpan.FromSeconds(8)));
    }

    private void ShowPointerArrow(int screenX, int screenY, string? label)
    {
        if (!Settings.VisualGuidanceEnabled)
        {
            return;
        }

        GuidanceOverlayRequested?.Invoke(
            this,
            new GuidanceOverlayRequest(
                [new GuidanceMark(GuidanceMarkKind.Arrow, screenX, screenY, Label: label)],
                DimBackground: false,
                TimeSpan.FromSeconds(3.6)));

        // Detach the companion and send it to the target. While pointing, the
        // companion's job is to show the user where to look rather than to
        // trail their cursor, so it leaves the pointer and waits at the mark.
        CompanionGuidanceRequested?.Invoke(
            this,
            new CompanionGuidance(screenX, screenY, label ?? "Look here", TimeSpan.FromSeconds(4)));
    }

    /// <summary>
    /// The area of the whole virtual desktop, for the director's "is this
    /// large?" tests. Marks are drawn in virtual-desktop pixels, so this is the
    /// surface a target's size is meaningful relative to.
    /// </summary>
    private static long VirtualScreenArea() =>
        (long)Math.Max(1d, System.Windows.SystemParameters.VirtualScreenWidth) *
        (long)Math.Max(1d, System.Windows.SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Marks what a single-reply answer was pointing at, and rests the
    /// companion beside it.
    ///
    /// The reply either named a spot itself, or it answered in prose about
    /// something the user asked to be shown — in which case Metis searches the
    /// accessibility tree for the control the words describe, so a mark still
    /// lands on the real thing rather than nowhere. This is the whole of what a
    /// non-lesson turn does to the screen: it points, and the user acts.
    /// </summary>
    private async Task PointAtAnswerAsync(
        string request,
        AssistantPlan plan,
        ScreenCapture capture,
        CancellationToken cancellationToken)
    {
        if (!Settings.VisualGuidanceEnabled)
        {
            // Guidance is switched off: there is nothing to resolve a mark
            // against.
            return;
        }

        ResolvedAnnotation? resolved = null;
        if (plan.HasAnnotation)
        {
            resolved = await AnnotateAsync(plan.ToAnnotationTarget(), capture, 0, cancellationToken);
        }

        // The model answered in words without a spot. Find the control its
        // words describe and point at that, so "where is save?" still gets a
        // mark rather than only a sentence.
        if (resolved is null)
        {
            UiElementHit? hit;
            try
            {
                hit = await _uiAutomation.FindElementAsync(request, cancellationToken)
                      ?? await _uiAutomation.FindElementAsync(plan.SpokenText, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _log.Error("Metis could not search the screen for that control.", exception);
                return;
            }

            if (hit is null)
            {
                return;
            }

            var target = new AnnotationTarget(
                AnnotationScope.Control,
                Label: hit.Name,
                ElementName: hit.Name);
            resolved = await AnnotateAsync(target, capture, 0, cancellationToken);
            if (resolved is null)
            {
                ShowPointerArrow(hit.ScreenX, hit.ScreenY, hit.Name);
                return;
            }
        }

        CompanionGuidanceRequested?.Invoke(
            this,
            new CompanionGuidance(
                resolved.ScreenX,
                resolved.ScreenY,
                plan.Label ?? "Here",
                AnnotationDuration.For(resolved, VirtualScreenArea())));
    }

    /// <summary>
    /// Resolves an annotation against the real screen and draws it.
    ///
    /// Everything that puts a mark on screen goes through here, so the rule
    /// that the subject decides the shape is applied once rather than repeated
    /// at each call site with slightly different arguments.
    /// </summary>
    private async Task<ResolvedAnnotation?> AnnotateAsync(
        AnnotationTarget target,
        ScreenCapture capture,
        int stepNumber,
        CancellationToken cancellationToken)
    {
        if (!Settings.VisualGuidanceEnabled)
        {
            return null;
        }

        ResolvedAnnotation? resolved;
        try
        {
            resolved = await _annotations.ResolveAsync(target, capture, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Error("Metis could not work out where to draw an annotation.", exception);
            resolved = null;
        }

        if (resolved is null)
        {
            return null;
        }

        // The last thing before it is drawn: pin the mark onto the monitor the
        // user can actually see. Everything upstream works in one desktop-wide
        // space, so a valid-looking point can still land in the gap between two
        // screens or past the only one's edge — a highlight drawn where nobody
        // can find it. Both the overlay mark and the companion follow from this
        // one value, so clamping here keeps every kind of mark on screen.
        resolved = ClampToVisibleScreen(resolved);

        // The mark clears itself. Metis no longer waits to be caught up with,
        // so a mark that outlived its sentence would be pointing at something
        // it has stopped talking about.
        var hold = AnnotationDuration.For(resolved, VirtualScreenArea());

        _log.Info(
            $"Annotated {AnnotationScopes.Name(resolved.Scope)} as {resolved.Mark} " +
            $"at {resolved.ScreenX},{resolved.ScreenY} ({resolved.Width}x{resolved.Height}) " +
            $"from {resolved.Source}, held {hold.TotalSeconds:0}s.");

        GuidanceOverlayRequested?.Invoke(
            this,
            new GuidanceOverlayRequest([resolved.ToMark(stepNumber)], DimBackground: false, hold));

        return resolved;
    }

    /// <summary>
    /// Pins a resolved mark inside the visible bounds of the monitor it belongs
    /// to. The monitor list comes from the cursor service, which already knows
    /// how to answer "which screen is this point on" per Win32; a single-point
    /// lookup is enough because the mark's own centre decides its monitor.
    /// </summary>
    private ResolvedAnnotation ClampToVisibleScreen(ResolvedAnnotation resolved)
    {
        // Asking Windows which monitor a point is on can fail, and a mark that
        // cannot be clamped is still worth drawing where it was resolved.
        (int Left, int Top, int Right, int Bottom) area;
        try
        {
            area = Cursor.GetMonitorArea(resolved.ScreenX, resolved.ScreenY);
        }
        catch (Exception exception)
        {
            _log.Error("Metis could not read the monitor bounds, so the mark was left where it was resolved.", exception);
            return resolved;
        }

        var (left, top, right, bottom) = area;
        var monitors = new List<ScreenBoundsClamp.Monitor> { new(left, top, right, bottom) };

        var (x, y, width, height) = ScreenBoundsClamp.ClampRect(
            resolved.ScreenX, resolved.ScreenY, resolved.Width, resolved.Height, monitors);

        if (x == resolved.ScreenX && y == resolved.ScreenY &&
            width == resolved.Width && height == resolved.Height)
        {
            return resolved;
        }

        _log.Info(
            $"Clamped {AnnotationScopes.Name(resolved.Scope)} onto the visible screen: " +
            $"{resolved.ScreenX},{resolved.ScreenY} -> {x},{y}.");

        // A stroke's shape lives in its points, not its centre, so the whole
        // polyline is shifted by however far the centre moved — the drawn line
        // travels with the mark instead of detaching from it.
        var shiftedPoints = resolved.Points;
        if (resolved.Points is { Count: > 0 } && (x != resolved.ScreenX || y != resolved.ScreenY))
        {
            var deltaX = x - resolved.ScreenX;
            var deltaY = y - resolved.ScreenY;
            shiftedPoints = resolved.Points
                .Select(point => new GuidancePoint(point.ScreenX + deltaX, point.ScreenY + deltaY))
                .ToArray();
        }

        return resolved with
        {
            ScreenX = x,
            ScreenY = y,
            Width = width,
            Height = height,
            Points = shiftedPoints
        };
    }

    private static bool RequiresScreenObservation(string text) =>
        RequestIntent.RequiresScreenObservation(text);

    private string RequireOpenAiApiKey()
    {
        var key = _secretStore.ReadOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "No OpenAI API key is saved. Open Setup and add an OpenAI Platform API key first.");
        }

        return key;
    }

    private string RequireClaudeApiKey()
    {
        var key = _secretStore.ReadClaudeApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No Claude API key is saved. Open Setup and add an Anthropic API key first.");
        }

        return key;
    }

    private string RequireAssemblyAiApiKey()
    {
        var key = _secretStore.ReadAssemblyAiApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No AssemblyAI API key is saved. Open Setup and add one first.");
        }

        return key;
    }

    private string RequireElevenLabsApiKey()
    {
        var key = _secretStore.ReadElevenLabsApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No ElevenLabs API key is saved. Open Setup and add one first.");
        }

        return key;
    }

    private bool HasConfiguredProviderKey() => Settings.AiProvider switch
    {
        "OpenAI" => HasOpenAiKey,
        "Claude" => HasClaudeKey,
        "OpenRouter" => HasOpenRouterKey,
        "OpenClaw" or "Ollama" => true,
        "Automatic" => HasAnyApiKey,
        _ => HasGeminiKey
    };

    /// <summary>
    /// The stored secret for a provider Metis reaches over a configurable
    /// endpoint. OpenClaw's token is optional; OpenRouter's key is not.
    /// </summary>
    private string? EndpointProviderCredential(string provider) => provider switch
    {
        "OpenClaw" => _secretStore.ReadOpenClawToken(),
        "OpenRouter" => _secretStore.ReadOpenRouterApiKey(),
        _ => null
    };

    private static string ProviderDisplayName(string provider) => provider switch
    {
        "OpenAI" => "OpenAI",
        "Claude" => "Claude",
        "OpenClaw" => "OpenClaw gateway",
        "OpenRouter" => "OpenRouter",
        "Ollama" => "local Ollama",
        "Automatic" => "Gemini, OpenAI, or Claude",
        _ => "Gemini"
    };

    private IReasoningProvider CreateEndpointProvider(string provider) => provider switch
    {
        "OpenClaw" => new OpenClawReasoningProvider(
            endpoint: new Uri(Settings.OpenClawEndpoint, UriKind.Absolute)),
        "OpenRouter" => new OpenRouterReasoningProvider(
            endpoint: new Uri(Settings.OpenRouterEndpoint, UriKind.Absolute)),
        "Ollama" => new OllamaReasoningProvider(
            endpoint: new Uri(Settings.OllamaEndpoint, UriKind.Absolute),
            contextTokens: Settings.LocalContextTokens,
            enableThinking: !Settings.OllamaModel.Contains("instruct", StringComparison.OrdinalIgnoreCase)),
        _ => throw new InvalidOperationException($"{provider} is not a configurable endpoint provider.")
    };

    private async Task<ProviderTurnResult> GenerateWithSelectedProviderAsync(
        GeminiRequest request,
        CancellationToken cancellationToken)
    {
        if (Settings.AiProvider == "OpenAI")
        {
            return await GenerateWithOpenAiAsync(request, cancellationToken);
        }

        if (Settings.AiProvider == "Gemini")
        {
            return await GenerateWithGeminiAsync(request, cancellationToken);
        }

        if (Settings.AiProvider == "Claude")
        {
            return await GenerateWithClaudeAsync(request, cancellationToken);
        }

        if (Settings.AiProvider is "OpenClaw" or "Ollama")
        {
            return await GenerateWithEndpointProviderAsync(Settings.AiProvider, request, cancellationToken);
        }

        Exception? geminiError = null;
        Exception? openAiError = null;
        if (HasGeminiKey)
        {
            try
            {
                return await GenerateWithGeminiAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                geminiError = exception;
                _log.Error("Gemini failed in Automatic mode; trying OpenAI.", exception);
                SetStatus("Gemini was unavailable — trying OpenAI…");
            }
        }

        if (HasOpenAiKey)
        {
            try
            {
                return await GenerateWithOpenAiAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                openAiError = exception;
                _log.Error("OpenAI failed in Automatic mode; trying Claude.", exception);
            }
        }

        if (HasClaudeKey)
        {
            try
            {
                return await GenerateWithClaudeAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception claudeError)
            {
                var details = string.Join(
                    " ",
                    new[]
                    {
                        geminiError is null ? null : $"Gemini: {geminiError.Message}",
                        openAiError is null ? null : $"OpenAI: {openAiError.Message}",
                        $"Claude: {claudeError.Message}"
                    }.Where(value => value is not null));
                throw new InvalidOperationException($"Every configured reasoning provider failed. {details}", claudeError);
            }
        }

        if (openAiError is not null)
        {
            throw openAiError;
        }

        if (geminiError is not null)
        {
            throw geminiError;
        }

        throw new InvalidOperationException(
            "Automatic mode needs at least one saved API key. Add a Gemini, OpenAI, or Claude key in Setup.");
    }

    private async Task<ProviderTurnResult> GenerateWithGeminiAsync(
        GeminiRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _gemini.GenerateAsync(
            RequireApiKey(),
            Settings.ReasoningModel,
            request,
            cancellationToken);
        return new ProviderTurnResult("Gemini", response.Model, response.Text, response.Plan);
    }

    private async Task<ProviderTurnResult> GenerateWithOpenAiAsync(
        GeminiRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _openAi.GenerateAsync(
            RequireOpenAiApiKey(),
            Settings.OpenAiReasoningModel,
            Settings.OpenAiTranscriptionModel,
            request,
            cancellationToken);
        return new ProviderTurnResult("OpenAI", response.Model, response.Text, response.Plan);
    }

    private async Task<ProviderTurnResult> GenerateWithClaudeAsync(
        GeminiRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _claude.GenerateAsync(
            RequireClaudeApiKey(),
            Settings.ClaudeReasoningModel,
            request,
            cancellationToken);
        return new ProviderTurnResult("Claude", response.Model, response.Text, response.Plan);
    }

    private async Task<ProviderTurnResult> GenerateWithEndpointProviderAsync(
        string providerName,
        GeminiRequest request,
        CancellationToken cancellationToken)
    {
        var provider = CreateEndpointProvider(providerName);
        try
        {
            var credential = EndpointProviderCredential(providerName);
            var model = providerName == "OpenClaw" ? Settings.OpenClawModel : Settings.OllamaModel;
            var response = await provider.GenerateAsync(credential, model, request, cancellationToken);
            return new ProviderTurnResult(providerName, response.Model, response.Text, response.Plan);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    private Task<SpeechAudio?> SynthesizeWithProviderAsync(
        ProviderTurnResult response,
        CancellationToken cancellationToken) =>
        SynthesizeTextAsync(response.Text, response.Provider, cancellationToken);

    /// <summary>
    /// Speaks any line through whichever voice the user chose.
    ///
    /// This used to exist only for the main reply. Lesson steps and spoken
    /// errors called Piper directly, so on a machine set to any other voice —
    /// which is most machines, since Piper needs a separate download — every
    /// step of a walkthrough and every spoken error was silent, and the log
    /// filled with "the Piper executable was not found". The reply worked, so
    /// it read as the voice cutting out rather than as a setting being ignored.
    /// </summary>
    private async Task<SpeechAudio?> SynthesizeTextAsync(
        string text,
        string? answeringProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var response = new ProviderTurnResult(answeringProvider ?? Settings.AiProvider, string.Empty, text);

        if (Settings.TextToSpeechProvider == "Windows")
        {
            return await _windowsVoice.SynthesizeSpeechAsync(
                Settings.WindowsVoiceName,
                response.Text,
                cancellationToken);
        }

        if (Settings.TextToSpeechProvider == "Piper")
        {
            return await _piper.SynthesizeSpeechAsync(
                ResolveLocalPath(Settings.PiperExecutablePath),
                ResolveLocalPath(Settings.PiperVoiceModelPath),
                response.Text,
                cancellationToken);
        }

        if (Settings.TextToSpeechProvider == "Chatterbox-Nano")
        {
            return await _chatterboxNano.SynthesizeSpeechAsync(
                Settings.ChatterboxEndpoint,
                Settings.ChatterboxModel,
                Settings.ChatterboxVoice,
                response.Text,
                cancellationToken);
        }

        if (Settings.TextToSpeechProvider == "ElevenLabs")
        {
            return await _elevenLabs.SynthesizeSpeechAsync(
                RequireElevenLabsApiKey(),
                Settings.ElevenLabsModel,
                Settings.ElevenLabsVoiceId,
                response.Text,
                cancellationToken);
        }

        if (response.Provider == "OpenAI")
        {
            return await _openAi.SynthesizeSpeechAsync(
                RequireOpenAiApiKey(),
                Settings.OpenAiSpeechModel,
                Settings.OpenAiVoiceName,
                response.Text,
                cancellationToken);
        }

        if (response.Provider == "Gemini" || HasGeminiKey)
        {
            return await _gemini.SynthesizeSpeechAsync(
                RequireApiKey(),
                Settings.SpeechModel,
                Settings.VoiceName,
                response.Text,
                cancellationToken);
        }

        if (HasOpenAiKey)
        {
            return await _openAi.SynthesizeSpeechAsync(
                RequireOpenAiApiKey(),
                Settings.OpenAiSpeechModel,
                Settings.OpenAiVoiceName,
                response.Text,
                cancellationToken);
        }

        // Nothing matched: no offline voice selected, and no cloud key to fall
        // back on. Silent by necessity, but no longer silent about being
        // silent — this path produced no audio and no explanation anywhere.
        _log.Info(
            $"No voice is available: text-to-speech is set to '{Settings.TextToSpeechProvider}' and no usable " +
            $"provider key was found for '{response.Provider}'. Nothing was spoken.");
        return null;
    }

    private void OnAudioLevelChanged(object? sender, float level) => AudioLevelChanged?.Invoke(this, level);

    private void ConfigureCaptureProfile()
    {
        if (_capture is VirtualDesktopCaptureService virtualDesktopCapture)
        {
            virtualDesktopCapture.UseCompactLocalProfile(Settings.AiProvider == "Ollama");
        }
    }

    /// <summary>
    /// Resolves a tool or model path. A relative path is looked for beside the
    /// executable first, then in each parent directory.
    ///
    /// The walk exists because a published build lives in a nested output
    /// folder while the tools sit at the repository root, so the same relative
    /// default that works when running from source silently pointed at nothing
    /// once published — and the only symptom was the voice quietly not working.
    /// </summary>
    private static string ResolveLocalPath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (Path.IsPathFullyQualified(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        var beside = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed));
        if (File.Exists(beside) || Directory.Exists(beside))
        {
            return beside;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory?.Parent is not null; depth++)
        {
            directory = directory.Parent;
            var candidate = Path.GetFullPath(Path.Combine(directory.FullName, trimmed));
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // Nothing found. Returning the beside-the-executable path keeps the
        // error message pointing at the place a user would expect to fix.
        return beside;
    }

    private void StartCompanionResponse(string text, TimeSpan? speechDuration, bool showBubble) =>
        CompanionResponseStarted?.Invoke(this, new CompanionResponse(text, speechDuration, showBubble));

    private static TimeSpan? GetAudioDuration(SpeechAudio audio)
    {
        var bytesPerSecond = audio.SampleRate * audio.Channels * (audio.BitsPerSample / 8d);
        return bytesPerSecond <= 0
            ? null
            : TimeSpan.FromSeconds(audio.PcmData.Length / bytesPerSecond);
    }

    /// <summary>
    /// Publishes what Metis is doing for the notch. Kept separate from
    /// SetStatus because the status line is prose for the window, while this is
    /// a short narrative with optional step progress.
    /// </summary>
    /// <summary>
    /// Tells the user they just got better at something. A tool that claims to
    /// build capability has to show the capability being built; otherwise the
    /// progress only exists in a file nobody opens.
    /// </summary>
    private void AnnounceProgress(SkillProgress? progress)
    {
        if (progress is null || !progress.LevelledUp)
        {
            return;
        }

        var level = SkillMemoryEngine.GuidanceDepth(progress.Record.Level);
        _log.Info($"Skill '{progress.Record.Skill}' moved {progress.Previous} -> {progress.Record.Level}.");

        SkillProgressed?.Invoke(this, progress);
        SetActivity(
            MetisActivityKind.Complete,
            progress.ReachedIndependence
                ? $"You can do {Shorten(progress.Record.Skill, 34)} unaided"
                : $"{Shorten(progress.Record.Skill, 34)} — {progress.Record.Level}");
        SetStatus($"{progress.Record.Skill}: {progress.Record.Level}. Metis will now give {level}.");
    }

    /// <summary>Raised when a practised skill moves up a level.</summary>
    public event EventHandler<SkillProgress>? SkillProgressed;

    private void SetActivity(MetisActivityKind kind, string text, int stepNumber = 0, int stepCount = 0)
    {
        CurrentActivity = new MetisActivity(kind, text, stepNumber, stepCount);
        ActivityChanged?.Invoke(this, CurrentActivity);
    }

    private void SetStatus(string status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    /// <summary>
    /// <paramref name="spokenMessage"/> lets a caller that already knows which
    /// part of the text carries the real diagnosis choose it for the spoken
    /// version, instead of leaving the summariser to guess from a message that
    /// opens with a generic lead-in.
    /// </summary>
    private void ReportError(
        string message,
        AssistantState errorState = AssistantState.Error,
        string? spokenMessage = null)
    {
        State.Force(errorState);
        SetStatus(message);
        MessageAdded?.Invoke(this, new AssistantMessage(AssistantRole.Error, message, DateTimeOffset.Now));
        _log.Error(message);
        SetActivity(MetisActivityKind.Error, "Something went wrong");
        SpeakErrorAloud(spokenMessage ?? message);
    }

    /// <summary>
    /// Speaks a short version of an error through the offline Piper voice.
    /// Deliberately offline and independent of the configured text-to-speech
    /// provider: the errors most worth hearing are the ones where the cloud
    /// provider is unreachable, unauthorised, or out of quota, and a cloud
    /// voice would fail for the same reason.
    /// </summary>
    /// <summary>
    /// Whether the offline voice is actually present. Checked rather than
    /// assumed, because Piper ships separately and its absence is the normal
    /// case rather than a fault.
    /// </summary>
    private bool HasPiperInstalled() =>
        File.Exists(ResolveLocalPath(Settings.PiperExecutablePath)) &&
        File.Exists(ResolveLocalPath(Settings.PiperVoiceModelPath));

    private void SpeakErrorAloud(string message)
    {
        if (_disposed)
        {
            return;
        }

        var spoken = Settings.SpeakErrorsAloud ? SpokenErrorSummarizer.Summarize(message) : string.Empty;
        var cue = Settings.ActivationSoundsEnabled ? ResolveCue(MetisSound.Error) : null;
        if (cue is null && spoken.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Strictly sequential. Starting playback stops whatever is
                // already playing, so overlapping these would mean the sound
                // truncated the sentence explaining what actually went wrong.
                if (cue is not null)
                {
                    await _audioPlayback.PlayAsync(cue, AudioPriority.Cue, CancellationToken.None);
                }

                if (spoken.Length == 0)
                {
                    return;
                }

                // Offline first, for the reason above. But Piper is a separate
                // download most machines do not have, and calling it anyway
                // meant every error logged a second error about the missing
                // executable — noise that buried the fault worth reading.
                // Offline first, for the reason above — and now with a voice
                // that is actually there. Piper when it has been installed,
                // otherwise the one built into Windows, which needs no download
                // and no network. That matters most here: the errors worth
                // hearing aloud are the ones where the cloud is what failed, so
                // a cloud voice would fail the same way and say nothing.
                var audio = HasPiperInstalled()
                    ? await _piper.SynthesizeSpeechAsync(
                        ResolveLocalPath(Settings.PiperExecutablePath),
                        ResolveLocalPath(Settings.PiperVoiceModelPath),
                        spoken,
                        CancellationToken.None)
                    : await _windowsVoice.SynthesizeSpeechAsync(
                        Settings.WindowsVoiceName,
                        spoken,
                        CancellationToken.None);

                if (audio is not null)
                {
                    await _audioPlayback.PlayAsync(audio, AudioPriority.Speech, CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                // Log only. Reporting this as an error would call back into
                // ReportError and start an endless spoken-error loop.
                _log.Error("Metis could not announce the error aloud.", exception);
            }
        });
    }

    /// <summary>
    /// Plays a cue for one interaction moment without blocking the caller, so
    /// the keyboard hook is never held up. Failures are silent: a missing sound
    /// is never worth interrupting the user over.
    /// </summary>
    private void PlayCue(MetisSound sound)
    {
        if (_disposed || !Settings.ActivationSoundsEnabled)
        {
            return;
        }

        var audio = ResolveCue(sound);
        if (audio is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _audioPlayback.PlayAsync(audio, AudioPriority.Cue, CancellationToken.None);
            }
            catch
            {
                // A cue is decoration; losing one must not disturb the turn.
            }
        });
    }

    /// <summary>
    /// Prefers the user's sound pack, falls back to a synthesised cue for the
    /// two moments that have one, and otherwise stays silent. A pack that is
    /// missing a file therefore loses that one sound rather than breaking the
    /// rest of the set.
    /// </summary>
    private SpeechAudio? ResolveCue(MetisSound sound) => _soundPack.TryGet(sound) ?? sound switch
    {
        MetisSound.RecordingStarted or MetisSound.InspectPressed => SoundCueFactory.Pop(),
        MetisSound.RequestSent => SoundCueFactory.Woosh(),
        _ => null
    };

    /// <summary>
    /// Reloads the skills the user has written. Called on start and whenever
    /// Setup is saved, so adding a skill file takes effect without a restart.
    /// </summary>
    public void ReloadUserSkills()
    {
        var resolved = string.IsNullOrWhiteSpace(Settings.SkillsFolder)
            ? null
            : ResolveLocalPath(Settings.SkillsFolder);

        _skillStore = new FileSkillStore(resolved, _log.Info);
        _skillStore.EnsureFolderWithExample();
        _userSkills = Settings.UserSkillsEnabled ? _skillStore.Load() : [];
    }

    /// <summary>
    /// Starts a fresh conversation, saving the current one if it has anything
    /// in it. The old chat stays recallable rather than being discarded.
    /// </summary>
    public void StartNewChat(string? application = null)
    {
        SaveCurrentChat();
        _currentChat = ChatSession.Start(application);
        ChatsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reopens a stored conversation as the current one.</summary>
    public void ResumeChat(string sessionId)
    {
        var match = _chatSessions.FirstOrDefault(session => session.Id == sessionId);
        if (match is null || match.Id == _currentChat.Id)
        {
            return;
        }

        SaveCurrentChat();
        _currentChat = match;
        foreach (var turn in match.Turns)
        {
            MessageAdded?.Invoke(
                this,
                new AssistantMessage(
                    turn.IsUser ? AssistantRole.User : AssistantRole.Metis,
                    turn.Text,
                    turn.At));
        }

        ChatsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAllChats()
    {
        _chatStore.DeleteAll();
        _chatSessions = [];
        _currentChat = ChatSession.Start();
        ChatsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The record of what the user has actually practised. Metis has always
    /// collected this to decide how much guidance to give, but nothing ever
    /// showed it to the person it is about, which is the half that turns
    /// assistance into learning.
    /// </summary>
    public Task<MemoryDocument> LoadMemoryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _memory.LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Erases one stored credential from Windows Credential Manager. The store
    /// has always been able to delete, but nothing exposed it, so a key could
    /// be replaced and never actually removed.
    /// </summary>
    public void DeleteProviderKey(string provider)
    {
        ThrowIfDisposed();

        switch (provider?.Trim().ToLowerInvariant())
        {
            case "gemini": _secretStore.DeleteGeminiApiKey(); break;
            case "openai": _secretStore.DeleteOpenAiApiKey(); break;
            case "claude": _secretStore.DeleteClaudeApiKey(); break;
            case "openclaw": _secretStore.DeleteOpenClawToken(); break;
            case "openrouter": _secretStore.DeleteOpenRouterApiKey(); break;
            case "assemblyai": _secretStore.DeleteAssemblyAiApiKey(); break;
            case "elevenlabs": _secretStore.DeleteElevenLabsApiKey(); break;
            default: throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown credential.");
        }

        _log.Info($"Deleted the stored {provider} credential.");
    }

    private void RecordChatTurn(string role, string text, string? application)
    {
        if (!Settings.ChatMemoryEnabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_currentChat.Application is null && !string.IsNullOrWhiteSpace(application))
        {
            _currentChat = _currentChat with { Application = application };
        }

        _currentChat = _currentChat.Append(new ChatTurn(role, text.Trim(), DateTimeOffset.Now));
        SaveCurrentChat();
    }

    private void SaveCurrentChat()
    {
        if (!Settings.ChatMemoryEnabled || _currentChat.IsEmpty)
        {
            return;
        }

        _chatStore.Save(_currentChat);
        _chatSessions.RemoveAll(session => session.Id == _currentChat.Id);
        _chatSessions.Insert(0, _currentChat);
        ChatsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rebuilds the pack when the folder changes. Decoded audio is cached per
    /// pack, so this is what lets a user drop in new files and hear them after
    /// saving Setup instead of restarting.
    /// </summary>
    private void ReloadSoundPack()
    {
        var resolved = string.IsNullOrWhiteSpace(Settings.SoundPackPath)
            ? null
            : ResolveLocalPath(Settings.SoundPackPath);
        if (string.Equals(resolved, _soundPack.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _soundPack = new SoundPack(resolved, _log.Info);
    }

    private void ReportException(string context, Exception exception)
    {
        // The written form keeps Metis's own context ("Metis could not get an
        // answer"), but that lead-in is the least useful thing to hear. The
        // spoken form starts from the provider's own words instead, so the user
        // hears "Claude rejected the request" rather than a generic apology.
        var detail = Sanitize(exception.Message);
        ReportError($"{context}. {detail}", ClassifyErrorState(exception), spokenMessage: detail);
        _log.Error(context, exception);
    }

    private static AssistantState ClassifyErrorState(Exception exception) => exception switch
    {
        GeminiProviderException { Kind: GeminiErrorKind.Network or GeminiErrorKind.ServiceUnavailable } =>
            AssistantState.NetworkError,
        OpenAiProviderException { Kind: OpenAiErrorKind.Network or OpenAiErrorKind.ServiceUnavailable } =>
            AssistantState.NetworkError,
        ExternalVoiceProviderException { Kind: ExternalVoiceErrorKind.Network or ExternalVoiceErrorKind.ServiceUnavailable } =>
            AssistantState.NetworkError,
        ReasoningProviderException { Kind: ReasoningProviderErrorKind.Network or ReasoningProviderErrorKind.ServiceUnavailable } =>
            AssistantState.NetworkError,
        GeminiProviderException { Kind: GeminiErrorKind.Authentication or GeminiErrorKind.Permission } =>
            AssistantState.AuthenticationError,
        OpenAiProviderException { Kind: OpenAiErrorKind.Authentication or OpenAiErrorKind.Permission } =>
            AssistantState.AuthenticationError,
        ExternalVoiceProviderException { Kind: ExternalVoiceErrorKind.Authentication or ExternalVoiceErrorKind.Permission } =>
            AssistantState.AuthenticationError,
        ReasoningProviderException { Kind: ReasoningProviderErrorKind.Authentication or ReasoningProviderErrorKind.Permission } =>
            AssistantState.AuthenticationError,
        GeminiProviderException { Kind: GeminiErrorKind.QuotaOrRateLimit } => AssistantState.QuotaError,
        OpenAiProviderException { Kind: OpenAiErrorKind.QuotaOrRateLimit } => AssistantState.QuotaError,
        ExternalVoiceProviderException { Kind: ExternalVoiceErrorKind.QuotaOrRateLimit } => AssistantState.QuotaError,
        ReasoningProviderException { Kind: ReasoningProviderErrorKind.QuotaOrRateLimit } => AssistantState.QuotaError,
        _ => AssistantState.Error
    };

    private string Sanitize(string message)
    {
        var sanitized = message;
        string?[] secrets =
        {
            _secretStore.ReadGeminiApiKey(),
            _secretStore.ReadOpenAiApiKey(),
            _secretStore.ReadClaudeApiKey(),
            _secretStore.ReadOpenClawToken(),
            _secretStore.ReadAssemblyAiApiKey(),
            _secretStore.ReadElevenLabsApiKey()
        };
        foreach (var secret in secrets.Where(secret => !string.IsNullOrWhiteSpace(secret)))
        {
            sanitized = sanitized.Replace(secret!, "[redacted]", StringComparison.Ordinal);
        }

        return sanitized;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _turnCancellation?.Cancel();
        _turnCancellation?.Dispose();
        _pushToTalk.Pressed -= OnPushToTalkPressed;
        _pushToTalk.Released -= OnPushToTalkReleased;
        _pushToTalk.EmergencyStopPressed -= OnEmergencyStopPressed;
        _pushToTalk.ContextActivationPressed -= OnContextActivationPressed;
        _pushToTalk.ContextActivationReleased -= OnContextActivationReleased;
        _pushToTalk.ContextActivationUpgraded -= OnContextActivationUpgraded;
        _recorder.LevelChanged -= OnAudioLevelChanged;
        _pushToTalk.Dispose();
        _recorder.Dispose();
        _audioPlayback.Dispose();
        (_gemini as IDisposable)?.Dispose();
        (_openAi as IDisposable)?.Dispose();
        (_claude as IDisposable)?.Dispose();
        (_assemblyAi as IDisposable)?.Dispose();
        (_elevenLabs as IDisposable)?.Dispose();
        (_chatterboxNano as IDisposable)?.Dispose();
        (_settingsStore as IDisposable)?.Dispose();
        (_memory as IDisposable)?.Dispose();
        _turnGate.Dispose();
    }
}

public enum AssistantRole
{
    User,
    Metis,
    Error
}

public sealed record AssistantMessage(AssistantRole Role, string Text, DateTimeOffset CreatedAt);

public sealed record CompanionResponse(string Text, TimeSpan? SpeechDuration, bool ShowBubble = true);

internal sealed record ProviderTurnResult(
    string Provider,
    string Model,
    string Text,
    AssistantPlan? Plan = null);

