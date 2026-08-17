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
    private readonly IDesktopAutomationService _desktopAutomation;
    private readonly IDesktopAutomationPipeline _automationPipeline;
    private readonly IGlobalPushToTalk _pushToTalk;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IMemoryService _memory;
    private readonly ISafetyPolicyEngine _safety = new SafetyPolicyEngine();
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
            new ChatterboxNanoProvider(),
            new WaveAudioRecorder(),
            new WaveAudioPlayback(),
            new VirtualDesktopCaptureService(),
            new FlaUiAutomationService(),
            new DesktopAutomationService(),
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
        IChatterboxNanoProvider chatterboxNano,
        IAudioRecorder recorder,
        IAudioPlayback audioPlayback,
        IScreenCaptureService capture,
        IUiAutomationService uiAutomation,
        IDesktopAutomationService desktopAutomation,
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
        _chatterboxNano = chatterboxNano;
        _recorder = recorder;
        _audioPlayback = audioPlayback;
        _capture = capture;
        _uiAutomation = uiAutomation;
        _desktopAutomation = desktopAutomation;
        _automationPipeline = new DesktopAutomationPipeline(desktopAutomation);
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
    /// The active Learn/Guide/Assist/Autopilot mode. It is read from settings
    /// so it survives restarts, and it gates automation independently of
    /// whatever a reasoning provider returns.
    /// </summary>
    /// <summary>
    /// What Metis decided the most recent request was asking for. It is a
    /// readout, not a setting: nothing outside a turn can change it, and each
    /// turn recomputes it from the user's own words.
    /// </summary>
    public IntentDecision LastIntent { get; private set; } =
        new(AssistanceIntent.Teach, "Nothing has been asked yet.", IsExplicit: false);

    /// <summary>
    /// The ceiling the user chose: whether Metis may operate the computer at
    /// all. Read from settings on every access so a change takes effect on the
    /// next request rather than the next restart.
    /// </summary>
    public AssistanceMode Mode => AssistanceModes.Parse(Settings.OperatingMode);

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
    public event EventHandler<IntentDecision>? IntentChanged;
    public event EventHandler<AssistanceMode>? ModeChanged;

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
    public event Func<PermissionRequest, Task<bool>>? PermissionRequested;
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
        _desktopAutomation.FullControlEnabled = Settings.FullDesktopControl;
        _desktopAutomation.MoveRealCursor = Settings.MoveRealCursor;
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

        var modeChanged = !string.Equals(normalized.OperatingMode, Settings.OperatingMode, StringComparison.Ordinal);
        await _settingsStore.SaveAsync(normalized, cancellationToken);
        Settings = normalized;
        _desktopAutomation.FullControlEnabled = Settings.FullDesktopControl;
        _desktopAutomation.MoveRealCursor = Settings.MoveRealCursor;
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

    /// <summary>
    /// Switches the operating mode and persists it. The mode is a user decision,
    /// so it is never changed by anything a reasoning provider returns.
    /// </summary>
    /// <summary>
    /// Records what this turn was read as, and tells the interface so the notch
    /// and tray can show it. Called from inside a turn only — there is no way
    /// for the user or a provider to set it directly, which is what keeps a
    /// persuasive reply from granting itself permission to act.
    /// </summary>
    /// <summary>
    /// Switches between Learn and Autopilot. This is the one setting a user
    /// changes often enough to want one click away, because it is the answer to
    /// "may Metis touch my computer right now".
    /// </summary>
    public async Task SetModeAsync(AssistanceMode mode, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (mode == Mode)
        {
            return;
        }

        var updated = (Settings with { OperatingMode = AssistanceModes.Name(mode) }).Normalize();
        await _settingsStore.SaveAsync(updated, cancellationToken);
        Settings = updated;
        SettingsChanged?.Invoke(this, Settings);
        ModeChanged?.Invoke(this, mode);
        SetStatus($"{AssistanceModes.Name(mode)} — {AssistanceModes.Describe(mode)}");
        _log.Info($"Mode set to {mode}.");
    }

    /// <summary>
    /// Puts a high-risk action to the user and waits for an answer. Anything
    /// the safety engine does not flag runs without interruption, because a
    /// prompt on every click would train the user to dismiss them unread.
    ///
    /// With no handler attached the answer is no. An unattended Metis declining
    /// to run a command is a stalled task; one that runs it because nobody was
    /// listening is a machine changed without consent.
    /// </summary>
    private async Task<bool> ConfirmIfRequiredAsync(
        DesktopAction action,
        OperatingMode mode,
        CancellationToken cancellationToken)
    {
        if (!_safety.RequiresUserConfirmation(action, mode))
        {
            return true;
        }

        var handler = PermissionRequested;
        if (handler is null)
        {
            _log.Error($"No confirmation is possible, so Metis refused {action.Kind}.", null);
            return false;
        }

        var review = action.Kind == DesktopActionKind.RunCommand
            ? SystemCommandPolicy.Review(action.Text)
            : null;

        var request = new PermissionRequest(
            action,
            action.Kind == DesktopActionKind.RunCommand
                ? "Metis wants to run a system command"
                : $"Metis wants to {DescribeAction(action)}",
            review?.Summary ?? "This step was judged high risk, so Metis is checking first.",
            review?.Command,
            review?.NeedsElevation ?? false);

        SetActivity(MetisActivityKind.Verifying, "Waiting for you to confirm");

        try
        {
            return await handler(request).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _log.Error("Metis could not ask for confirmation, so the step was refused.", exception);
            return false;
        }
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

    private void ApplyIntent(IntentDecision decision)
    {
        LastIntent = decision;
        IntentChanged?.Invoke(this, decision);
        _log.Info($"Intent: {decision.Intent} — {decision.Reason}");
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
        _automationPipeline.CancelSession();
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
        _automationPipeline.EmergencyStop();
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
                "Listen to the attached recording and answer the user's request directly.",
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

        // A first reading of what the user wants, used to shape the request.
        // For typed input this is the final answer; for speech the words are
        // not known until transcription, so it is re-read once they are.
        var mode = IntentPolicy.ToMode(IntentPolicy.Clamp(Mode, IntentDetector.Detect(prompt)).Intent);

        try
        {
            _automationPipeline.StartSession();
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
            var taughtSkills = SkillLibrary.Describe(
                SkillLibrary.Select(_userSkills, screenshot?.WindowTitle, effectivePrompt));
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
                region);
            SetActivity(MetisActivityKind.Thinking, "Thinking");
            var response = await GenerateWithSelectedProviderAsync(request, cancellationToken);

            MessageAdded?.Invoke(
                this,
                new AssistantMessage(AssistantRole.Metis, response.Text, DateTimeOffset.Now));
            RecordChatTurn("metis", response.Text, screenshot?.WindowTitle);

            var finalStatus = $"Answered with {response.Provider} {response.Model}";
            var rawPlan = response.Plan ?? AssistantPlan.SpeechOnly(response.Text);
            // The intent is read again from the words Metis finally heard.
            // Speech is transcribed after the request was built, so the reading
            // taken up front was a guess at the shape of the answer; this one
            // decides what Metis is allowed to do, and must be based on what
            // the user actually said.
            var detectedIntent = IntentDetector.Detect(effectivePrompt);

            // The mode is a ceiling. In Learn this turns a request to act into
            // a request to be shown, whatever the words were.
            var finalIntent = IntentPolicy.Clamp(Mode, detectedIntent);
            if (IntentPolicy.WasClampedByMode(Mode, detectedIntent))
            {
                // Said out loud, because a refusal nobody notices reads as
                // Metis having ignored them.
                finalStatus += " — Learn mode, so Metis showed you instead of doing it";
                _log.Info("Learn mode declined to act and taught instead.");
            }

            var userAskedForAction = finalIntent is
                { Intent: AssistanceIntent.TakeControl, IsExplicit: true };
            ApplyIntent(finalIntent);
            mode = IntentPolicy.ToMode(finalIntent.Intent);

            var plan = ApplyModeAndSafety(rawPlan, mode, userAskedForAction, out var withheldNotice);
            if (withheldNotice is not null)
            {
                finalStatus += $" — {withheldNotice}";
            }

            var screenGroundingRequired = RequiresScreenObservation(effectivePrompt) ||
                                          userAskedForAction ||
                                          plan.Actions.Any(action => action.Kind != DesktopActionKind.Wait);
            if (screenGroundingRequired && screenshot is null)
            {
                throw new InvalidOperationException(
                    "Metis could not capture the target window, so it refused to invent screen details or coordinates.");
            }

            if (screenGroundingRequired && !plan.ScreenObserved)
            {
                throw new InvalidOperationException(
                    "The AI did not confirm that it inspected Metis's current screenshot. No screen answer or companion action was trusted.");
            }

            // A mode that deliberately withheld the steps has not failed, so the
            // "no usable steps" error only applies when the mode would have run them.
            if (userAskedForAction &&
                !IsHighImpactRequest(effectivePrompt) &&
                withheldNotice is null &&
                !plan.Actions.Any(action => action.Kind != DesktopActionKind.Wait))
            {
                throw new AutomationExecutionException(
                    "The AI returned no usable execution steps. Metis did not guess or invent a computer command.");
            }
            // "Show me where X is" is answered by a mark on the screen, not by
            // prose. When the model replies without a target — which it often
            // does — Metis finds the control itself through the accessibility
            // tree, so the user still gets pointed at the thing they asked about.
            if (RequestIntent.IsPointingRequest(effectivePrompt) &&
                plan.Actions.Count == 0 &&
                plan.LessonSteps.Count == 0)
            {
                await PointAtNamedControlAsync(effectivePrompt, plan, cancellationToken);
            }

            var bubbleCue = string.IsNullOrWhiteSpace(plan.BubbleCue) ? string.Empty : plan.BubbleCue.Trim();
            var guidanceOwnsCue = plan.Actions.Any(action => action.Kind != DesktopActionKind.Wait);
            _log.Info($"Assistant plan received: {plan.Actions.Count} desktop action(s), " +
                      $"screen context {(screenshot is null ? "unavailable" : "available")}.");
            // Teaching runs as a sequence the user works through, so any answer
            // carrying steps becomes a lesson Metis follows along with rather
            // than a single reply that is said once and forgotten.
            if (finalIntent.Teaches && plan.LessonSteps.Count > 0)
            {
                _lastLessonCapture = screenshot;
                await RecordTurnMemoryAsync(task, plan, mode, screenshot?.WindowTitle, true, CancellationToken.None);
                await RunLessonAsync(
                    new LessonState(plan.Goal ?? effectivePrompt, plan.LessonSteps),
                    cancellationToken);

                SetActivity(MetisActivityKind.Idle, string.Empty);
                State.Force(AssistantState.Idle);
                return;
            }

            var actionTask = ExecuteClosedLoopPlanAsync(
                plan,
                screenshot,
                effectivePrompt,
                mode,
                userAskedForAction,
                cancellationToken);
            var companionResponseStarted = false;

            // Answer in the way you were asked. A typed question gets a written
            // reply beside the cursor; speaking to Metis gets speech back.
            // Reading a typed answer aloud is intrusive when the user is
            // already looking at the screen and chose not to talk.
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
                        // Write out what is being said, paced to the audio, but
                        // only when it is short enough to read at a glance. A
                        // long explanation stays in the chat window rather than
                        // covering the user's work.
                        var spokenLine = CompanionSpeech.ChooseLine(plan.SpokenText, bubbleCue);
                        if (spokenLine is not null)
                        {
                            StartCompanionResponse(spokenLine, GetAudioDuration(audio), showBubble: true);
                            companionResponseStarted = true;
                        }
                        await _audioPlayback.PlayAsync(audio, cancellationToken);
                    }
                    else
                    {
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

                // Desktop execution starts before speech synthesis so movement can
                // accompany Metis's voice, but it is awaited separately. This keeps
                // automation failures from being mislabeled as audio failures.
                await actionTask;
            }
            else
            {
                await actionTask;
            }

            // A typed turn always gets its answer written beside the cursor,
            // even when there are controls to point at: with no voice, the text
            // is the answer, and the pointing cues follow it a moment later.
            if (!companionResponseStarted && (!guidanceOwnsCue || activation == ActivationKind.Typed))
            {
                // Nothing was spoken, so the bar carries the answer itself. A
                // typed question gets the whole reply beside the cursor, paced
                // as if it were being spoken — the user may be somewhere they
                // cannot listen, and a written answer is all they will get.
                var writtenLine = CompanionSpeech.ChooseWrittenLine(plan.SpokenText, bubbleCue);
                StartCompanionResponse(
                    writtenLine ?? string.Empty,
                    CompanionSpeech.ReadingDuration(writtenLine),
                    writtenLine is not null);
            }

            await RecordTurnMemoryAsync(task, plan, mode, screenshot?.WindowTitle, true, CancellationToken.None);

            State.Force(AssistantState.Success);
            SetActivity(MetisActivityKind.Complete, "Done");
            PlayCue(MetisSound.TaskComplete);
            SetStatus(finalStatus);
            await Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken);
            SetActivity(MetisActivityKind.Idle, string.Empty);
            State.Force(AssistantState.Idle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_automationPipeline.IsEmergencyStopped)
            {
                State.Force(AssistantState.Paused);
                SetStatus("Emergency stop — automation queue cleared. Start a new request to resume.");
            }
            else
            {
                State.Force(AssistantState.Idle);
                SetStatus("Request stopped or timed out");
            }
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

    private async Task ExecuteClosedLoopPlanAsync(
        AssistantPlan plan,
        ScreenCapture? capture,
        string originalPrompt,
        OperatingMode mode,
        bool userAskedForAction,
        CancellationToken cancellationToken)
    {
        if (plan.Actions.Count == 0)
        {
            return;
        }

        if (capture is null)
        {
            _log.Info("The model proposed desktop actions without a current screenshot; Metis ignored them.");
            return;
        }

        var currentPlan = plan;
        var currentCapture = capture;
        const int maxReplans = 8;
        for (var replan = 0; replan <= maxReplans; replan++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(currentPlan.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                throw new AutomationExecutionException(
                    string.IsNullOrWhiteSpace(currentPlan.SpokenText)
                        ? "Metis could not safely continue this task."
                        : currentPlan.SpokenText);
            }

            var outcome = await ExecutePlanBatchAsync(
                currentPlan,
                currentCapture,
                originalPrompt,
                mode,
                cancellationToken);
            if (outcome.Finished ||
                !outcome.NeedsObservation &&
                !string.Equals(currentPlan.Status, "continue", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (replan == maxReplans)
            {
                throw new AutomationExecutionException(
                    "Metis reached its safe replanning limit before it could verify the result.");
            }

            SetStatus("Checking the updated screen and planning the next stepâ€¦");
            if (outcome.WaitBeforeObservation > TimeSpan.Zero)
            {
                await Task.Delay(outcome.WaitBeforeObservation, cancellationToken);
            }

            var freshCapture = await _capture.CaptureActiveWindowAsync(cancellationToken);
            if (freshCapture is null)
            {
                throw new AutomationExecutionException(
                    "Metis could not capture the updated desktop, so it stopped instead of guessing the next step.");
            }

            string? automationContext = null;
            try
            {
                automationContext = await _uiAutomation.DescribeWindowAsync(freshCapture, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception automationContextError)
            {
                _log.Error("Fresh UI Automation context was unavailable; replanning with vision only.", automationContextError);
            }

            var replanPrompt = BuildClosedLoopReplanPrompt(
                originalPrompt,
                currentPlan,
                outcome,
                replan + 1);
            var nextResponse = await GenerateWithSelectedProviderAsync(
                new GeminiRequest(
                    replanPrompt,
                    freshCapture.ImageBytes,
                    null,
                    freshCapture.WindowTitle,
                    automationContext,
                    freshCapture.ImageMimeType,
                    freshCapture.Width,
                    freshCapture.Height,
                    freshCapture.ScreenLeft,
                    freshCapture.ScreenTop,
                    freshCapture.SourceWidth,
                    freshCapture.SourceHeight,
                    mode,
                    ActivationKind.Typed,
                    null,
                    _taskContext.Describe(),
                    null),
                cancellationToken);
            var rawNextPlan = nextResponse.Plan ?? AssistantPlan.SpeechOnly(nextResponse.Text);
            if (!rawNextPlan.ScreenObserved)
            {
                // Stopping beats throwing. The steps already executed were
                // grounded and verified, and discarding only the unconfirmed
                // next batch keeps that work while still refusing to act on a
                // screen the provider will not say it looked at. Throwing here
                // reported the whole task as failed and lost the completed
                // steps with it.
                _log.Info(
                    $"Closed-loop replan {replan + 1} did not confirm it inspected the fresh screenshot; " +
                    "Metis kept the verified steps and stopped there.");
                SetStatus(string.IsNullOrWhiteSpace(rawNextPlan.SpokenText)
                    ? "Metis completed the verified steps and stopped before an unconfirmed one."
                    : rawNextPlan.SpokenText);
                return;
            }

            // Each replan is re-checked against the mode. A long task can never
            // drift into performing steps the current mode does not allow.
            var nextPlan = ApplyModeAndSafety(rawNextPlan, mode, userAskedForAction, out _);

            _log.Info(
                $"Closed-loop replan {replan + 1}: received {nextPlan.Actions.Count} action(s) " +
                $"with status '{nextPlan.Status}'.");
            currentPlan = nextPlan;
            currentCapture = freshCapture;
        }
    }

    private async Task<PlanExecutionOutcome> ExecutePlanBatchAsync(
        AssistantPlan plan,
        ScreenCapture capture,
        string originalPrompt,
        OperatingMode mode,
        CancellationToken cancellationToken)
    {
        if (plan.Actions.Count == 0)
        {
            return new PlanExecutionOutcome(
                [],
                NeedsObservation: string.Equals(plan.Status, "continue", StringComparison.OrdinalIgnoreCase),
                Finished: string.Equals(plan.Status, "done", StringComparison.OrdinalIgnoreCase),
                Checkpoint: "The provider returned no executable actions.",
                PendingActionIds: [],
                WaitBeforeObservation: TimeSpan.Zero);
        }

        var highImpact = IsHighImpactRequest($"{originalPrompt} {plan.SpokenText}");
        var actions = plan.Actions
            .Take(6)
            .Where(action => !highImpact || IsNonMutatingAction(action.Kind))
            .ToArray();
        if (actions.Length == 0)
        {
            SetStatus("Metis found the control but left this high-impact click for you to confirm manually.");
            return new PlanExecutionOutcome(
                [],
                NeedsObservation: false,
                Finished: true,
                Checkpoint: "High-impact actions were withheld for manual confirmation.",
                PendingActionIds: [],
                WaitBeforeObservation: TimeSpan.Zero);
        }

        var actionLabels = actions
            .Select(action => string.IsNullOrWhiteSpace(action.Label) ? DescribeAction(action) : action.Label!)
            .ToArray();
        SetStatus(actions.Length == 1
            ? $"Metis is {actionLabels[0].ToLowerInvariant()}…"
            : $"Metis is carrying out {actions.Length} ordered steps…");

        var feedback = new List<ActionExecutionFeedback>(actions.Length);

        for (var index = 0; index < actions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[index];
            var actionId = action.Id ?? $"step-{index + 1}";
            SetActivity(MetisActivityKind.Acting, actionLabels[index], index + 1, actions.Length);
            var pendingActionIds = actions
                .Skip(index + 1)
                .Select((pending, pendingIndex) => pending.Id ?? $"step-{index + pendingIndex + 2}")
                .ToArray();

            if (action.Kind == DesktopActionKind.Finish)
            {
                if (feedback.Any(result => result.Success && IsDesktopMutation(result.Kind)))
                {
                    return new PlanExecutionOutcome(
                        feedback,
                        NeedsObservation: true,
                        Finished: false,
                        Checkpoint: "The desktop changed in this batch. Capture a fresh screen and verify it before finishing.",
                        PendingActionIds: new[] { actionId }.Concat(pendingActionIds).ToArray(),
                        WaitBeforeObservation: TimeSpan.FromMilliseconds(250));
                }

                feedback.Add(new ActionExecutionFeedback(actionId, action.Kind, true, "The provider marked the verified goal complete."));
                return new PlanExecutionOutcome(
                    feedback,
                    NeedsObservation: false,
                    Finished: true,
                    Checkpoint: action.ExpectedState ?? "Goal complete.",
                    PendingActionIds: pendingActionIds,
                    WaitBeforeObservation: TimeSpan.Zero);
            }

            if (action.Kind is DesktopActionKind.Observe or DesktopActionKind.Verify or
                DesktopActionKind.WaitForWindow or DesktopActionKind.WaitForElement or
                DesktopActionKind.WaitForText)
            {
                var checkpoint = action.Kind switch
                {
                    DesktopActionKind.Verify => $"Verify expected state: {action.ExpectedState ?? action.Text ?? action.Label ?? "requested result"}.",
                    DesktopActionKind.WaitForWindow => $"Wait for window: {action.Text}.",
                    DesktopActionKind.WaitForElement => $"Wait for element: {action.AutomationId ?? action.Text}.",
                    DesktopActionKind.WaitForText => $"Wait for visible text: {action.Text}.",
                    _ => "Observe the updated desktop."
                };
                feedback.Add(new ActionExecutionFeedback(actionId, action.Kind, true, checkpoint));
                var waitMilliseconds = action.Kind is DesktopActionKind.WaitForWindow or
                    DesktopActionKind.WaitForElement or DesktopActionKind.WaitForText
                    ? Math.Clamp(action.TimeoutMilliseconds, 250, 1_000)
                    : 0;
                return new PlanExecutionOutcome(
                    feedback,
                    NeedsObservation: true,
                    Finished: false,
                    Checkpoint: checkpoint,
                    PendingActionIds: pendingActionIds,
                    WaitBeforeObservation: TimeSpan.FromMilliseconds(waitMilliseconds));
            }

            try
            {
                var hasTarget = _desktopAutomation.TryResolveTarget(
                    action,
                    capture,
                    out var targetX,
                    out var targetY,
                    out var targetError);
                // While Metis is working the computer itself, the companion
                // stays where it is and only talks. The real pointer is already
                // crossing the screen doing the job, and sending the companion
                // chasing after it gives the user two things to follow at once
                // and a mark on a control that is about to be clicked anyway.
                // Marks and flights are how Metis shows; speech is how it
                // narrates, and narration is all that is wanted here.
                if (hasTarget && IntentPolicy.For(IntentPolicy.FromMode(mode)).ShowsAnnotations)
                {
                    var cue = CreateGuidanceCue(plan.BubbleCue, action);
                    CompanionGuidanceRequested?.Invoke(
                        this,
                        new CompanionGuidance(targetX, targetY, cue, TimeSpan.FromSeconds(5)));

                    // The action carries the target's extent when the model
                    // could see it, which is what lets the mark take the
                    // control's shape instead of ringing its middle.
                    var (markWidth, markHeight) = action.NormalizedWidth > 0 && action.NormalizedHeight > 0
                        ? CaptureProjection.ToScreenSize(action.NormalizedWidth, action.NormalizedHeight, capture)
                        : (0, 0);
                    ShowGuidanceOverlay(
                        mode, targetX, targetY, cue, index + 1, action, plan.Scope, markWidth, markHeight);
                }
                else if (action.Kind != DesktopActionKind.Wait)
                {
                    _log.Info($"Could not preview desktop target: {targetError}");
                }

                // High-risk actions stop here and wait for a person. The safety
                // engine has always been able to say one needs confirming;
                // until now nothing asked, so it was classified and then run.
                if (!await ConfirmIfRequiredAsync(action, mode, cancellationToken))
                {
                    _log.Info($"Declined by the user: {action.Kind} {Shorten(action.Text ?? string.Empty, 80)}");
                    feedback.Add(new ActionExecutionFeedback(
                        actionId, action.Kind, false, "You declined this step, so Metis stopped."));
                    SetStatus("Stopped — you declined that step.");
                    break;
                }

                var queuedAction = action.Kind == DesktopActionKind.Wait
                    ? action
                    : action with { DelayMilliseconds = Math.Max(action.DelayMilliseconds, 260) };
                var result = await _automationPipeline.EnqueueAsync(queuedAction, capture, cancellationToken);
                _log.Info($"Desktop action {index + 1}/{actions.Length}: {result.Message}");
                feedback.Add(new ActionExecutionFeedback(actionId, action.Kind, result.Success, result.Message));
                if (!result.Success)
                {
                    return new PlanExecutionOutcome(
                        feedback,
                        NeedsObservation: true,
                        Finished: false,
                        Checkpoint: $"Action '{actionId}' failed. Inspect the fresh screen before choosing a recovery step.",
                        PendingActionIds: pendingActionIds,
                        WaitBeforeObservation: TimeSpan.FromMilliseconds(250));
                }

                if (result.ScreenX is { } completedX && result.ScreenY is { } completedY)
                {
                    // Restart the five-second hold after the command completes.
                    CompanionGuidanceRequested?.Invoke(
                        this,
                        new CompanionGuidance(
                            completedX,
                            completedY,
                            CreateGuidanceCue(plan.BubbleCue, action),
                            TimeSpan.FromSeconds(5)));
                }

                if (RequiresFreshObservationAfter(action))
                {
                    return new PlanExecutionOutcome(
                        feedback,
                        NeedsObservation: true,
                        Finished: false,
                        Checkpoint: $"Action '{actionId}' may have changed the desktop. Reobserve before continuing.",
                        PendingActionIds: pendingActionIds,
                        WaitBeforeObservation: TimeSpan.FromMilliseconds(350));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var message = $"Metis could not complete '{actionLabels[index]}'. {exception.Message}";
                _log.Error(message, exception);
                feedback.Add(new ActionExecutionFeedback(actionId, action.Kind, false, message));
                return new PlanExecutionOutcome(
                    feedback,
                    NeedsObservation: true,
                    Finished: false,
                    Checkpoint: $"Action '{actionId}' threw an error. Inspect the fresh screen and recover safely.",
                    PendingActionIds: pendingActionIds,
                    WaitBeforeObservation: TimeSpan.FromMilliseconds(250));
            }
        }

        var needsPostBatchVerification = feedback.Any(result => result.Success && IsDesktopMutation(result.Kind));
        return new PlanExecutionOutcome(
            feedback,
            NeedsObservation: needsPostBatchVerification ||
                              string.Equals(plan.Status, "continue", StringComparison.OrdinalIgnoreCase),
            Finished: !needsPostBatchVerification &&
                      string.Equals(plan.Status, "done", StringComparison.OrdinalIgnoreCase),
            Checkpoint: needsPostBatchVerification
                ? "The current batch changed the desktop. Verify the fresh screen before finishing."
                : "The current action batch completed.",
            PendingActionIds: [],
            WaitBeforeObservation: TimeSpan.Zero);
    }

    private static string BuildClosedLoopReplanPrompt(
        string originalPrompt,
        AssistantPlan previousPlan,
        PlanExecutionOutcome outcome,
        int replanNumber)
    {
        var state = JsonSerializer.Serialize(new
        {
            protocol = "metis.closed_loop.v1",
            original_goal = originalPrompt,
            plan_id = previousPlan.PlanId,
            replan_number = replanNumber,
            previous_status = previousPlan.Status,
            checkpoint = outcome.Checkpoint,
            execution_results = outcome.Feedback.Select(result => new
            {
                action_id = result.ActionId,
                action_type = ToProtocolActionName(result.Kind),
                success = result.Success,
                message = result.Message
            }),
            discarded_unexecuted_action_ids = outcome.PendingActionIds
        });

        return $"""
            closed_loop_replan: yes
            screen_capture_attached: yes
            A new screenshot of the current desktop is attached to this request. Inspect it before answering.
            Because you are inspecting that fresh screenshot, screen_observed must be true in your reply. Returning false here discards the work already completed.
            Continue the original desktop goal using the fresh attached screenshot and the trusted execution feedback below.
            Do not assume that an unexecuted action occurred. Reissue it only if the new screen proves it is still appropriate.
            Return only the next small reliable action batch. Observe again after any screen-changing action and finish only after visible verification.
            If the goal already looks complete in the attached screenshot, return a finish action with status done rather than no actions at all.

            closed_loop_state_json:
            {state}
            """;
    }

    /// <summary>
    /// Applies the operating mode and the safety engine to a plan the provider
    /// returned. Non-mutating steps such as pointing survive in every mode, so
    /// Learn and Guide still show the user where to go.
    /// </summary>
    private AssistantPlan ApplyModeAndSafety(
        AssistantPlan plan,
        OperatingMode mode,
        bool userAskedForAction,
        out string? withheldNotice)
    {
        withheldNotice = null;
        if (plan.Actions.Count == 0)
        {
            return plan;
        }

        var permitted = new List<DesktopAction>(plan.Actions.Count);
        string? firstRefusal = null;
        foreach (var action in plan.Actions)
        {
            if (_safety.IsPermitted(action, mode, userAskedForAction, out var reason))
            {
                permitted.Add(action);
                continue;
            }

            firstRefusal ??= reason;
            _log.Info($"Withheld {action.Kind} in {mode} mode: {reason}");
        }

        var capabilities = ModePolicy.For(mode);
        var trimmed = permitted.Take(capabilities.MaxActionsPerBatch).ToArray();
        if (firstRefusal is not null)
        {
            withheldNotice = firstRefusal;
        }

        return plan with { Actions = trimmed };
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
        // Skills are only worth tracking while Metis is teaching; when it does
        // the work itself the user practised nothing.
        if (!Settings.MemoryEnabled || !IntentPolicy.For(LastIntent.Intent).TrackSkills)
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
        OperatingMode mode,
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
                plan.Actions.Count == 0 ? null : $"{plan.Actions.Count} step(s) in {mode} mode",
                plan.BubbleCue ?? plan.Goal);

            await _memory.RecordTaskOutcomeAsync(
                _taskContext.Current ?? task,
                success,
                plan.SpokenText,
                cancellationToken);

            if (ModePolicy.For(mode).TrackSkills && !string.IsNullOrWhiteSpace(plan.Goal))
            {
                // In Learn and Guide the user performed the step themselves, so
                // the guidance flag records whether Metis had to show them.
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
    /// Draws the overlay for one step. Learn mode dims the rest of the screen
    /// because attention matters more than context while learning.
    /// </summary>
    /// <summary>
    /// Runs a lesson: show one step, wait for the learner to do it, confirm it
    /// on screen, then move on. This is the inverse of the Autopilot loop —
    /// Metis performs nothing, and the screen is watched for the learner's work
    /// rather than for its own.
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

        var hold = AnnotationDuration.Standard;

        if (step.HasTarget && _lastLessonCapture is { } capture)
        {
            // The hold is decided from the target once it has been resolved, so
            // it reflects the control's real size rather than the model's guess
            // at it. Marked first with the standard hold and corrected below,
            // because the overlay needs a duration at the moment it draws.
            var annotation = await AnnotateAsync(
                step.ToAnnotationTarget() with
                {
                    Label = step.TargetLabel ?? ShortenForLabel(step.Instruction)
                },
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
                ? ToScreenPoint(step.TargetX, step.TargetY, capture)
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

    private async Task SpeakLessonLineAsync(string line, CancellationToken cancellationToken)
    {
        StartCompanionResponse(line, null, showBubble: true);
        if (!Settings.SpeechEnabled)
        {
            return;
        }

        try
        {
            var audio = await _piper.SynthesizeSpeechAsync(
                ResolveLocalPath(Settings.PiperExecutablePath),
                ResolveLocalPath(Settings.PiperVoiceModelPath),
                line,
                cancellationToken);
            if (audio is not null)
            {
                await _audioPlayback.PlayAsync(audio, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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

    /// <summary>
    /// Locates the control the user asked about and marks it with its real
    /// bounds. Windows knows exactly where its own controls are, so this is
    /// both more reliable than a model's coordinate guess and exact enough for
    /// the highlight to take the control's shape.
    /// </summary>
    private async Task PointAtNamedControlAsync(
        string request,
        AssistantPlan plan,
        CancellationToken cancellationToken)
    {
        if (!Settings.VisualGuidanceEnabled)
        {
            return;
        }

        UiElementHit? hit;
        try
        {
            // The model's own words usually name the control better than the
            // raw request does, so both are searched.
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
            _log.Info($"No control on screen matched '{Shorten(request, 80)}', so nothing was highlighted.");
            return;
        }

        _log.Info($"Pointing at '{hit.Name}' ({hit.ControlType}) at {hit.ScreenX},{hit.ScreenY}.");

        // These bounds came from Windows, so the mark can be chosen from the
        // control's true proportions rather than from anybody's estimate.
        var annotation = AnnotationDirector.Resolve(
            plan.Scope,
            hit.ScreenX,
            hit.ScreenY,
            hit.Width,
            hit.Height,
            hit.Name,
            AnnotationSource.Element,
            VirtualScreenArea());

        GuidanceOverlayRequested?.Invoke(
            this,
            new GuidanceOverlayRequest(
                [annotation.ToMark()],
                DimBackground: false,
                TimeSpan.FromSeconds(8)));

        CompanionGuidanceRequested?.Invoke(
            this,
            new CompanionGuidance(hit.ScreenX, hit.ScreenY, hit.Name, TimeSpan.FromSeconds(8)));
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

    private void ShowGuidanceOverlay(
        OperatingMode mode,
        int screenX,
        int screenY,
        string? label,
        int stepNumber,
        DesktopAction? action,
        AnnotationScope scope = AnnotationScope.Control,
        int width = 0,
        int height = 0)
    {
        if (!Settings.VisualGuidanceEnabled ||
            !IntentPolicy.For(IntentPolicy.FromMode(mode)).ShowsAnnotations)
        {
            return;
        }

        // Fall back to a modest square only when the model gave no extent. The
        // director then has something to choose a shape from either way.
        var annotation = AnnotationDirector.Resolve(
            scope,
            screenX,
            screenY,
            width > 0 ? width : 56,
            height > 0 ? height : 56,
            label,
            AnnotationSource.Estimated,
            VirtualScreenArea());

        var marks = new List<GuidanceMark> { annotation.ToMark(stepNumber) };

        // An arrow as well as the mark, but only when the mark is small enough
        // to be missed. Sweeping an arrow at a bracketed window points at
        // something the user cannot fail to see and clutters the screen doing it.
        if (action?.Kind == DesktopActionKind.MovePointer &&
            annotation.Mark is GuidanceMarkKind.FocusRing or GuidanceMarkKind.Capsule)
        {
            marks.Add(new GuidanceMark(GuidanceMarkKind.Arrow, screenX, screenY));
        }

        GuidanceOverlayRequested?.Invoke(
            this,
            new GuidanceOverlayRequest(marks, IntentPolicy.FromMode(mode) == AssistanceIntent.Teach, TimeSpan.FromSeconds(5)));
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

    private static bool RequiresFreshObservationAfter(DesktopAction action) => action.Kind switch
    {
        DesktopActionKind.LeftClick or DesktopActionKind.DoubleClick or DesktopActionKind.RightClick or
        DesktopActionKind.OpenApp or DesktopActionKind.OpenUrl => true,
        DesktopActionKind.KeyPress when action.Key is not null =>
            action.Key.Equals("enter", StringComparison.OrdinalIgnoreCase) ||
            action.Key.Equals("return", StringComparison.OrdinalIgnoreCase) ||
            action.Key.StartsWith("alt+", StringComparison.OrdinalIgnoreCase) ||
            action.Key.StartsWith("win+", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static bool IsNonMutatingAction(DesktopActionKind kind) => kind is
        DesktopActionKind.MovePointer or DesktopActionKind.Wait or DesktopActionKind.WaitForWindow or
        DesktopActionKind.WaitForElement or DesktopActionKind.WaitForText or DesktopActionKind.Observe or
        DesktopActionKind.Verify or DesktopActionKind.Finish;

    private static bool IsDesktopMutation(DesktopActionKind kind) => kind is
        DesktopActionKind.LeftClick or DesktopActionKind.DoubleClick or DesktopActionKind.RightClick or
        DesktopActionKind.TypeText or DesktopActionKind.KeyPress or DesktopActionKind.OpenApp or
        DesktopActionKind.OpenUrl;

    private static string ToProtocolActionName(DesktopActionKind kind) => kind switch
    {
        DesktopActionKind.MovePointer => "move_pointer",
        DesktopActionKind.LeftClick => "left_click",
        DesktopActionKind.DoubleClick => "double_click",
        DesktopActionKind.RightClick => "right_click",
        DesktopActionKind.TypeText => "type_text",
        DesktopActionKind.KeyPress => "key_press",
        DesktopActionKind.OpenApp => "open_app",
        DesktopActionKind.OpenUrl => "open_url",
        DesktopActionKind.Wait => "wait",
        DesktopActionKind.WaitForWindow => "wait_for_window",
        DesktopActionKind.WaitForElement => "wait_for_element",
        DesktopActionKind.WaitForText => "wait_for_text",
        DesktopActionKind.Observe => "observe",
        DesktopActionKind.Verify => "verify",
        DesktopActionKind.Finish => "finish",
        _ => kind.ToString().ToLowerInvariant()
    };

    private static bool IsHighImpactRequest(string text)
    {
        string[] blockedTerms =
        [
            "buy", "purchase", "pay", "order", "delete", "remove", "submit", "send",
            "password", "security", "permission", "authorize", "admin", "install", "uninstall", "download",
            "sign in", "log in", "bank", "transfer", "confirm payment"
        ];
        return blockedTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresScreenObservation(string text) =>
        RequestIntent.RequiresScreenObservation(text);

    private static bool IsDesktopActionRequest(string text) =>
        RequestIntent.IsComputerActionRequest(text);

    private static string DescribeAction(DesktopAction action) => action.Kind switch
    {
        DesktopActionKind.MovePointer => "Moving Metis to the control",
        DesktopActionKind.LeftClick => "Clicking the control",
        DesktopActionKind.DoubleClick => "Opening the control",
        DesktopActionKind.RightClick => "Opening the context menu",
        DesktopActionKind.TypeText => "Typing the requested text",
        DesktopActionKind.KeyPress => "Pressing the navigation key",
        DesktopActionKind.OpenApp => "Opening the requested app",
        DesktopActionKind.OpenUrl => "Opening the requested page",
        DesktopActionKind.Wait => "Waiting for the screen",
        DesktopActionKind.WaitForWindow => "Waiting for the requested window",
        DesktopActionKind.WaitForElement => "Waiting for the requested control",
        DesktopActionKind.WaitForText => "Waiting for the requested text",
        DesktopActionKind.Observe => "Checking the updated screen",
        DesktopActionKind.Verify => "Verifying the result",
        DesktopActionKind.Finish => "Finishing the task",
        _ => "Working"
    };

    private static string CreateGuidanceCue(string? bubbleCue, DesktopAction action)
    {
        var cue = !string.IsNullOrWhiteSpace(bubbleCue)
            ? bubbleCue.Trim()
            : !string.IsNullOrWhiteSpace(action.Label)
                ? action.Label.Trim()
                : action.Kind == DesktopActionKind.MovePointer
                    ? "Press here"
                    : "Working here";
        return cue.Length <= 32 ? cue : cue[..32];
    }

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

    private async Task<SpeechAudio?> SynthesizeWithProviderAsync(
        ProviderTurnResult response,
        CancellationToken cancellationToken)
    {
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
                    await _audioPlayback.PlayAsync(cue, CancellationToken.None);
                }

                if (spoken.Length == 0)
                {
                    return;
                }

                var audio = await _piper.SynthesizeSpeechAsync(
                    ResolveLocalPath(Settings.PiperExecutablePath),
                    ResolveLocalPath(Settings.PiperVoiceModelPath),
                    spoken,
                    CancellationToken.None);
                if (audio is not null)
                {
                    await _audioPlayback.PlayAsync(audio, CancellationToken.None);
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
                await _audioPlayback.PlayAsync(audio, CancellationToken.None);
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
        AutomationExecutionException => AssistantState.AutomationError,
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
        _automationPipeline.Dispose();
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

internal sealed record PlanExecutionOutcome(
    IReadOnlyList<ActionExecutionFeedback> Feedback,
    bool NeedsObservation,
    bool Finished,
    string Checkpoint,
    IReadOnlyList<string> PendingActionIds,
    TimeSpan WaitBeforeObservation);

internal sealed class AutomationExecutionException : Exception
{
    public AutomationExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
