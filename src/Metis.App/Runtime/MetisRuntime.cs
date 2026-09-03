using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Metis.AI;
using Metis.AI.Agents;
using Metis.Core.Agents;
using Metis.Core.Agents.Tools;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;
using Metis.Core.State;
using Metis.Data;
using Metis.Windows;
using NAudio.Wave;

namespace Metis.App.Runtime;

public sealed class MetisRuntime : IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;

    /// <summary>
    /// Where the last signed entitlement snapshot lives. Constructed directly
    /// rather than injected because it has exactly one implementation and the
    /// application has no container; the interface exists so a test can hand in
    /// a different one, not so this line can vary.
    /// </summary>
    private readonly IEntitlementCache _entitlementCache = new CredentialEntitlementCache();
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

    /// <summary>
    /// Counts turns so a turn can tell whether it is still the one being waited
    /// on. Needed because the gate is released once the answer is on screen,
    /// which lets a turn's delivery — speech, marks, a lesson — outlive the
    /// start of the next one.
    /// </summary>
    private int _turnSequence;

    /// <summary>
    /// The skill-memory document, held between turns. Null means "read it".
    /// See <see cref="DescribeSkillsAsync"/>.
    /// </summary>
    private MemoryDocument? _cachedMemory;

    /// <summary>Serialises the background chat-session writes. See <see cref="SaveCurrentChat"/>.</summary>
    private readonly object _chatSaveGate = new();
    private Task _chatSaveChain = Task.CompletedTask;
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
    private string? _lastVoiceError;

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
        // The log matters here more than usual: a toast that Windows drops
        // reports nothing at all, so this is the only place a broken
        // notification system can announce itself.
        var notifications = new WindowsNotificationService(message => _log.Info($"Notifications: {message}"));

        // A toast that offers Approve and Deny and then does nothing when they
        // are pressed is worse than a toast with no buttons at all, so the
        // arguments those buttons carry are acted on here.
        notifications.Activated += (_, arguments) => HandleNotificationAction(arguments);
        // Environment.ProcessPath is the only reliable answer in a single-file
        // app: Assembly.Location is an empty string there, so a "?? Location"
        // fallback would hand Register a blank path rather than fall through.
        notifications.Register(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Metis.exe"));
        _notifications = notifications;
        // Agents can now run on Metis's own AI, for the many people who have no
        // key of their own. The access is a function rather than a value because
        // a task may run for many minutes and the session token will not: handing
        // it one at construction would give a long job a credential that expires
        // halfway through.
        AgentTasks = new AgentTaskManager(new AgentReasoningClient(
            _settingsStore,
            _secretStore,
            httpClient: null,
            gatewayAccess: () => MetisBackend.HasGateway(Settings.MetisGatewayUrl)
                ? (new Uri(MetisBackend.ResolveGatewayUrl(Settings.MetisGatewayUrl), UriKind.Absolute),
                   SessionAccessToken)
                : (null, null)));
        AgentTasks.GetAutonomyMode = () => Settings.AgentAutonomyMode;
        AgentTasks.MaxTurnsPerTask = Settings.AgentMaxTurns;

        AgentTasks.TaskCreated += (_, task) =>
        {
            if (Settings.AgentWindowsNotificationsEnabled)
            {
                _notifications.ShowNotification("⚡ Agent Started", $"Agent [{task.Id}] started:\n\"{task.Goal}\"", "Metis Autonomous Agent");
            }
        };

        AgentTasks.ApprovalRequested += (_, req) =>
        {
            if (Settings.AgentWindowsNotificationsEnabled)
            {
                _notifications.ShowActionableNotification(
                    "Metis agent needs permission",
                    $"{req.ToolName} — {req.Reason}",
                    $"agent:{req.TaskId}",
                    [
                        ("Approve", $"approve:{req.TaskId}"),
                        ("Deny", $"deny:{req.TaskId}")
                    ]);
            }
        };

        AgentTasks.TaskCompleted += (_, task) =>
        {
            if (Settings.AgentWindowsNotificationsEnabled)
            {
                var summary = string.IsNullOrWhiteSpace(task.ResultSummary) ? "Completed successfully." : task.ResultSummary;
                _notifications.ShowNotification("✅ Agent Finished", $"Agent [{task.Id}] completed:\n\"{task.Goal}\"\n\n{summary}", "Metis Autonomous Agent");
            }
        };

        AgentTasks.TaskFailed += (_, task) =>
        {
            if (Settings.AgentWindowsNotificationsEnabled)
            {
                var error = string.IsNullOrWhiteSpace(task.ErrorMessage) ? "Task failed." : task.ErrorMessage;
                _notifications.ShowNotification("❌ Agent Failed", $"Agent [{task.Id}] failed:\n\"{task.Goal}\"\n\n{error}", "Click Retry in Notch Agent Drawer");
            }
        };

        TeachingSessions = new TeachingSessionManager();
        TeachingSessions.LessonStepChanged += (_, lesson) => LessonChanged?.Invoke(this, lesson);
        TeachingSessions.LessonStarted += (_, lesson) => LessonChanged?.Invoke(this, lesson);
        TeachingSessions.LessonCompleted += (_, lesson) => LessonChanged?.Invoke(this, lesson);

        var teachingHooks = new CompanionTeachingHooks
        {
            ResolveAnnotationAsync = async target =>
            {
                var cap = await _capture.CaptureActiveWindowAsync();
                if (cap is null) return null;
                return await _annotations.ResolveAsync(target, cap);
            },
            ShowOverlay = req => GuidanceOverlayRequested?.Invoke(this, req),
            ShowCompanionGuidance = g => CompanionGuidanceRequested?.Invoke(this, g),
            ShowCompanionDemo = d => CompanionDemoRequested?.Invoke(this, d),
            ClearOverlay = () => GuidanceOverlayRequested?.Invoke(this, GuidanceOverlayRequest.Clear),
            GetDiagramCanvas = () => DiagramCanvas.Centred(0, 0, 1920, 1080, 0.7)
        };

        var observationHooks = new CompanionObservationHooks
        {
            CaptureScreenAsync = ct => _capture.CaptureActiveWindowAsync(ct),
            FindUiElementAsync = (q, ct) => _uiAutomation.FindElementAsync(q, ct),
            DescribeWindowAsync = (c, ct) => _uiAutomation.DescribeWindowAsync(c, ct),
            DescribeElementAtAsync = (x, y, ct) => _uiAutomation.DescribeElementAtAsync(x, y, ct)
        };

        var orchestrationHooks = new SubAgentOrchestrationHooks
        {
            // A helper inherits its parent's depth plus one, so the chain is
            // bounded. It is also marked SubAgent, which holds it at
            // AskApproval however the autonomy setting is configured: a goal one
            // agent wrote for another is the furthest thing from the user's own
            // words that this system produces.
            SpawnWorkerAsync = (goal, templateId, dir, parentId) =>
            {
                var parentDepth = AgentTasks.GetTask(parentId)?.Depth ?? 0;
                return Task.FromResult(AgentTasks.SpawnTask(
                    goal, templateId, dir, maxTurns: null,
                    origin: AgentSpawnOrigin.SubAgent,
                    depth: parentDepth + 1));
            },
            GetWorkerStatus = id => AgentTasks.GetTask(id),
            ListActiveWorkers = () => AgentTasks.GetActiveTasks(),
            CancelWorker = id => AgentTasks.CancelTask(id)
        };

        AgentTasks.RegisterCompanionTools(teachingHooks, observationHooks, orchestrationHooks);

        // Gives agents a real, visible browser. The browser itself is not
        // launched here -- only when an agent first asks for one -- because the
        // very first launch may have to download Chromium, and most tasks never
        // need a browser at all.
        //
        // The two delegates are the plan gate, asked at the moment a tool runs
        // rather than now: a browser opened on Metis's behalf is a capability
        // the pricing page sells, and this is called once at start-up while the
        // plan can change under it at any point afterwards.
        AgentTasks.UseBrowser(
            new Metis.Windows.PlaywrightBrowserFactory(message => _log.Info($"Browser: {message}")),
            () => Can(MetisFeature.BrowserAssistance),
            () => ExplainCapability(MetisFeature.BrowserAssistance));
    }

    private readonly IWindowsNotificationService _notifications;
    public AgentTaskManager AgentTasks { get; }
    public TeachingSessionManager TeachingSessions { get; }
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
    /// What the server last said this account may do, or null when it has never
    /// been asked.
    ///
    /// Null is not the same as "nothing allowed". It means the answer is
    /// unknown, and the callers below resolve that against the compiled-in rules
    /// rather than against a guess in either direction.
    /// </summary>
    public EntitlementSnapshot? Entitlements { get; private set; }

    public event EventHandler<EntitlementSnapshot?>? EntitlementsChanged;

    /// <summary>
    /// The current Supabase access token, and when it stops being one.
    ///
    /// Held only in memory. The refresh token is what survives a restart, in
    /// Windows Credential Manager; this is the short-lived thing traded for it,
    /// and writing it anywhere would be storing a bearer credential to save a
    /// round trip that takes a fraction of a second.
    /// </summary>
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Adopts a session established by the sign-in panel.
    /// </summary>
    public void SignIn(MetisAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var email = !string.IsNullOrWhiteSpace(account.Email) ? account.Email : Settings.UserEmail;
        var name = !string.IsNullOrWhiteSpace(account.DisplayName) ? account.DisplayName : Settings.UserName;
        var avatar = !string.IsNullOrWhiteSpace(account.Avatar) ? account.Avatar : Settings.UserAvatar;

        // The stored plan override is a staff testing tool, so only a staff
        // account may be signed in under it. It used to apply to everybody, and
        // that single line made settings.json the authority on what a person had
        // paid for: RefreshEntitlementsAsync would fetch the real plan from the
        // gateway, hand it here, and this would throw it away and reinstate
        // whatever the local file said. Account.Plan and Entitlements.Plan then
        // disagreed for the rest of the session — and Account.Plan is what the
        // interface offers from.
        var plan = account.IsStaff
                   && !string.IsNullOrWhiteSpace(Settings.TestPlanTier)
                   && Enum.TryParse<PlanTier>(Settings.TestPlanTier, true, out var testPlan)
            ? testPlan
            : account.Plan;

        Account = account with { Email = email, DisplayName = name, Avatar = avatar, Plan = plan };
        AccountChanged?.Invoke(this, Account);

        // Written down, not merely held. Settings.UserEmail was read on every
        // sign-in and written by nothing anywhere in the repository, so it was
        // permanently empty and the fallback above could never do anything. The
        // profile now persists with the session that produced it, which is what
        // makes the sign-out below able to tell whose it was.
        if (!string.Equals(Settings.UserEmail, email, StringComparison.Ordinal))
        {
            _ = SaveSettingsAsync(Settings with { UserEmail = email }, null, null);
        }

        _log.Info($"Signed in as {Account.Role} on the {Account.Plan} plan.");
        SetStatus($"Signed in — {Account.Plan} plan");
    }

    /// <summary>
    /// Whether this account may move itself between plans from inside the app.
    ///
    /// Staff only, and the interface asks before it offers the buttons. A plan
    /// is bought on the website; anybody else clicking one of these would be
    /// asking the client to grant them something the gateway will refuse, and an
    /// action that is certain to be refused should not be offered at all.
    /// </summary>
    public bool CanSwitchPlanLocally => Account.IsStaff;

    /// <summary>
    /// Moves a staff account on to another plan, so a gate can be watched
    /// working from both sides without buying anything.
    ///
    /// This used to be open to everyone, and it did rather more than its name
    /// suggests: it rewrote the account, wrote a full granted-feature set into a
    /// local <see cref="EntitlementSnapshot"/> with billing marked live, and
    /// persisted the choice — so a click on "Max" in the plan list was, in
    /// effect, the paid tier, until the next restart reinstated it from
    /// settings.json. Nothing was ever bought and the gateway would refuse every
    /// managed request, but everything the client decides for itself believed it.
    ///
    /// Fabricating the snapshot is why this stays staff-only rather than merely
    /// being made honest: a snapshot the client wrote for itself is the one kind
    /// of entitlement that never came from the server.
    /// </summary>
    public async Task SetPlanAsync(PlanTier newPlan)
    {
        if (!CanSwitchPlanLocally)
        {
            _log.Info($"Plan switch to {newPlan} refused: plans are bought on the website.");
            return;
        }

        Account = Account with { Plan = newPlan };
        var granted = Metis.Core.Services.Entitlements.GrantedFeatures(Account, billingIsLive: true);
        // From the catalogue rather than written out again here. This table had
        // already drifted: it priced Pro at $29 with a 500-step agent allowance
        // while the database said something else entirely, and whichever the
        // user saw depended on whether the gateway had answered yet.
        var limits = Metis.Core.Services.PlanCatalogue.LimitsFor(newPlan);
        Entitlements = new EntitlementSnapshot(
            Account.UserId, Account.Role, Account.Plan, Account.EmailVerified,
            true, granted, limits, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30));

        await SaveSettingsAsync(Settings with { TestPlanTier = newPlan.ToString() }, null, null);
        AccountChanged?.Invoke(this, Account);
        EntitlementsChanged?.Invoke(this, Entitlements);
        _log.Info($"Subscription plan switched to {newPlan}.");
        SetStatus($"Plan: {newPlan}");
    }

    /// <summary>
    /// Updates the user's profile display name and avatar icon/emoji.
    /// </summary>
    public async Task UpdateProfileAsync(string displayName, string avatar)
    {
        Account = Account with { DisplayName = displayName, Avatar = avatar };
        await SaveSettingsAsync(Settings with { UserName = displayName, UserAvatar = avatar }, null, null);
        AccountChanged?.Invoke(this, Account);
    }

    /// <summary>
    /// Records the access token the sign-in exchange produced.
    ///
    /// The application used to use this once and throw it away, which was fine
    /// while nothing but Supabase itself was ever called with it. The gateway
    /// needs it on every managed turn, so it is kept — in memory only, and
    /// alongside its expiry so an obviously dead one is never sent.
    /// </summary>
    public void SetSession(string? accessToken, DateTimeOffset expiresUtc)
    {
        _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim();
        _accessTokenExpiresUtc = expiresUtc;
    }

    /// <summary>
    /// The access token, or null when there is none or it has already expired.
    ///
    /// Thirty seconds of slack, because a token that expires while the request
    /// is in flight is refused by the server just as firmly as one that expired
    /// a minute ago, and the refusal costs the user the turn.
    /// </summary>
    public string? SessionAccessToken =>
        _accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresUtc.AddSeconds(-30)
            ? _accessToken
            : null;

    /// <summary>
    /// The account page's address, carrying a one-time sign-in when it can.
    ///
    /// "Manage on Web" was a plain link, and the desktop app and the website
    /// keep completely separate Supabase sessions, so it opened whichever
    /// account the browser was already signed in as. On a machine where more
    /// than one account has ever been used that is routinely not the one Metis
    /// is signed in to, and the user is shown a different plan and a different
    /// address than the panel they clicked from -- which reads as Metis having
    /// lost their account rather than as two sessions disagreeing.
    ///
    /// Falls back to the plain address on any failure. Landing on the site
    /// signed in as nobody is a small annoyance; refusing to open it because
    /// the handoff could not be minted would be a larger one.
    /// </summary>
    public async Task<string> ResolveAccountPageUrlAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionAccessToken;
        if (_disposed || token is null || !MetisBackend.HasGateway(Settings.MetisGatewayUrl))
        {
            return MetisBackend.AccountPageUrl;
        }

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Post,
                new Uri(new Uri(MetisBackend.ResolveGatewayUrl(Settings.MetisGatewayUrl)), "v1/web-session"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await WhileGatewayMayBeWakingAsync(
                () => http.SendAsync(request, cancellationToken));

            if (!response.IsSuccessStatusCode)
            {
                _log.Info($"No web handoff was issued ({(int)response.StatusCode}); opening the site signed out.");
                return MetisBackend.AccountPageUrl;
            }

            using var document = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            if (!document.RootElement.TryGetProperty("token", out var handoff)
                || handoff.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                return MetisBackend.AccountPageUrl;
            }

            // The token goes in the fragment rather than the query string, so it
            // is never sent to the server in a request line and never lands in
            // an access log or a Referer header. The page reads it and clears it.
            return MetisBackend.AccountPageUrl
                   + "#handoff=" + Uri.EscapeDataString(handoff.GetString() ?? string.Empty);
        }
        catch (Exception exception)
        {
            _log.Info($"No web handoff could be prepared ({exception.Message}); opening the site signed out.");
            return MetisBackend.AccountPageUrl;
        }
    }

    /// <summary>
    /// Adopts what the gateway said about this account, and remembers it so a
    /// signed-in user who is offline tomorrow is not shown the free plan.
    /// </summary>
    public void ApplyEntitlements(EntitlementSnapshot? snapshot, string? signedSnapshot)
    {
        Entitlements = snapshot;
        if (snapshot is not null && !string.IsNullOrWhiteSpace(signedSnapshot))
        {
            _entitlementCache.Write(signedSnapshot);
        }

        EntitlementsChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Reads back a snapshot cached on a previous run, if the signature, the
    /// account it names, and its expiry all still hold.
    /// </summary>
    public EntitlementSnapshot? RestoreCachedEntitlements(string userId)
    {
        using var publicKey = EntitlementSigner.TryLoadPublicKey(MetisBackend.EntitlementPublicKey);
        if (publicKey is null)
        {
            return null;
        }

        var snapshot = EntitlementSigner.Verify(
            _entitlementCache.Read(), publicKey, userId, DateTimeOffset.UtcNow);

        if (snapshot is not null)
        {
            Entitlements = snapshot;
            EntitlementsChanged?.Invoke(this, snapshot);
        }

        return snapshot;
    }

    /// <summary>
    /// Whether this account has a capability, according to whatever the client
    /// currently knows.
    ///
    /// A live or cached snapshot from the server is the truth. Without one, the
    /// compiled-in table is evaluated with billing treated as <em>off</em>,
    /// which deserves explaining because the cautious-looking choice is the
    /// wrong one here.
    ///
    /// The cautious reading — assume billing is live, so an unknown answer
    /// grants less — was what this did first, and running it showed what it
    /// actually produces: a user on the free plan, with the gateway briefly
    /// unreachable, is shown a list of nine capabilities they have not lost and
    /// told they do not have them. That is the application lying to someone
    /// about their own account, and it happens on exactly the day something is
    /// already going wrong.
    ///
    /// Guessing the other way costs nothing, because this decides only what to
    /// <em>show</em>. Every managed request is checked again by the gateway,
    /// which refuses to spend anything at all when it cannot read its own rules
    /// — so the client being generous here cannot turn into money leaving. The
    /// two halves now agree: absent information means billing-off on both sides.
    /// </summary>
    /// <summary>
    /// Whether this account may do something.
    ///
    /// The snapshot from the server wins whenever there is one. Without it, the
    /// answer is worked out from the plan on the account with billing treated
    /// as live — which is to say the plan restrictions apply.
    ///
    /// That fallback used to pass billingIsLive: false, which grants
    /// everything to everybody. The reasoning was that nothing should be taken
    /// away before there is anything to buy, and it had the effect of making
    /// every plan limit invisible: a Free account could do all of it, so there
    /// was no way to tell a gate that worked from a gate that had never been
    /// written. A restriction nobody can watch working is not a restriction.
    ///
    /// This only decides what the interface offers. The gateway checks the same
    /// entitlement again for anything it pays for, because a value that
    /// travelled through a program the user controls is not evidence.
    /// </summary>
    public bool Can(MetisFeature feature) =>
        Entitlements is { } snapshot
            ? snapshot.Has(feature)
            : Metis.Core.Services.Entitlements.Has(Account, feature, billingIsLive: true);

    /// <summary>
    /// Whether the client actually knows what this account may do, as opposed
    /// to falling back on what it assumes. The account page says so plainly
    /// rather than presenting a guess as a fact.
    /// </summary>
    public bool EntitlementsAreKnown => Entitlements is not null;

    /// <summary>Why a capability was refused, in words for the user.</summary>
    public string ExplainCapability(MetisFeature feature) =>
        Metis.Core.Services.Entitlements.Explain(
            Account, feature, Entitlements?.BillingIsLive ?? true);

    public void SignOut()
    {
        Account = MetisAccount.SignedOut;
        Entitlements = null;
        _accessToken = null;
        _accessTokenExpiresUtc = DateTimeOffset.MinValue;

        // A cached plan must not outlive the account it was issued for. It would
        // be rejected anyway — it names its user — but leaving it behind means
        // leaving a record of who used this machine, for no benefit at all.
        _entitlementCache.Clear();

        // Neither must the profile, for exactly the same reason and one more:
        // SignIn falls back to these when a session does not carry them, so a
        // name and avatar left behind here were adopted by whoever signed in
        // next. Sign in as one person after another and Metis showed you the
        // first one's identity attached to the second one's account -- while
        // the website, holding its own separate session, showed the truth. That
        // is the "it showed another account" report, and it was Metis that had
        // it wrong.
        _ = SaveSettingsAsync(
            Settings with { UserEmail = string.Empty, UserName = string.Empty, UserAvatar = string.Empty },
            null,
            null);

        AccountChanged?.Invoke(this, Account);
        EntitlementsChanged?.Invoke(this, null);
        _log.Info("Signed out.");
        SetStatus("Signed out. Metis still works with your own API key.");
    }

    /// <summary>
    /// Starts one background agent per goal and returns what was started.
    ///
    /// Everything that spawns goes through here so origin is never forgotten:
    /// origin decides how much an agent may do unattended, and a spawn that
    /// forgot to say where it came from would quietly inherit the most
    /// permissive answer.
    /// </summary>
    private IReadOnlyList<AgentTaskRecord> SpawnAgents(
        IReadOnlyList<string> goals,
        AgentSpawnOrigin origin)
    {
        if (AgentTasks is null || goals.Count == 0)
        {
            return [];
        }

        if (!Can(MetisFeature.AutonomousAgents))
        {
            _log.Info($"Agent spawn refused: {ExplainCapability(MetisFeature.AutonomousAgents)}");

            // Not "(Plus & Pro)". Plus stopped existing, and agents are on every
            // plan now — sold by the number of messages rather than withheld —
            // so naming a tier here was wrong twice over. The sentence
            // underneath says what the actual reason is.
            PlanLimitReached?.Invoke(this, new PlanLimitNotice(
                "Background helpers",
                ExplainCapability(MetisFeature.AutonomousAgents)));
            return [];
        }

        var started = new List<AgentTaskRecord>();
        foreach (var goal in goals)
        {
            if (string.IsNullOrWhiteSpace(goal))
            {
                continue;
            }

            try
            {
                var task = AgentTasks.SpawnTask(goal.Trim(), origin: origin);
                started.Add(task);
                _log.Info($"Agent {task.Id} spawned ({origin}): {task.Goal}");
            }
            catch (Exception exception)
            {
                // One goal failing must not lose the others, and must not take
                // down the turn that asked for them.
                _log.Error($"Could not spawn an agent for '{goal}'.", exception);
            }
        }

        return started;
    }

    /// <summary>
    /// The last few exchanges, verbatim, so a follow-up can be understood as
    /// one.
    ///
    /// Kept short deliberately. This is not memory — that is what ChatRecall is
    /// for — it is just enough of the immediate thread that "tidy my downloads"
    /// still reads as the answer to the question Metis asked a moment ago.
    /// </summary>
    private string? DescribeRecentTurns()
    {
        var turns = _currentChat.Turns;
        if (turns.Count == 0)
        {
            return null;
        }

        var recent = turns.Count <= RecentTurnsKept ? turns : turns.Skip(turns.Count - RecentTurnsKept).ToList();
        var lines = recent
            .Where(turn => !string.IsNullOrWhiteSpace(turn.Text))
            .Select(turn => $"{(turn.IsUser ? "user" : "metis")}: {Truncate(turn.Text.Trim(), 400)}");

        var joined = string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    /// <summary>How many exchanges of the live conversation travel with a request.</summary>
    private const int RecentTurnsKept = 6;

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";

    /// <summary>
    /// Goes and finds the one thing the model said it could not confirm.
    ///
    /// This is the whole of the difference between a chatbot and something
    /// worth trusting on screen. Metis used to get one look, one guess, and no
    /// way to say "I am not sure" — so when the screenshot was ambiguous it
    /// answered confidently anyway, and a confident wrong mark is worse than no
    /// mark. Now the model can decline to guess, and Metis asks Windows
    /// directly through the accessibility tree, which knows what every control
    /// is actually called.
    ///
    /// Deliberately local: no second round trip, no extra tokens, and typically
    /// under a fifth of a second. When the lookup fails the answer says so
    /// rather than papering over it, because "I cannot see it, is it open?" is
    /// a useful sentence and an invented location is not.
    /// </summary>
    private async Task<AssistantPlan> TakeSecondLookAsync(
        AssistantPlan plan,
        ScreenCapture? capture,
        CancellationToken cancellationToken)
    {
        var wanted = plan.LookFor?.Trim();
        if (string.IsNullOrWhiteSpace(wanted) || capture is null)
        {
            return plan;
        }

        UiElementHit? hit = null;
        try
        {
            hit = await _uiAutomation.FindElementAsync(wanted, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Error($"The second look for '{wanted}' failed.", exception);
        }

        if (hit is null)
        {
            _log.Info($"Second look: '{wanted}' is not on screen. Saying so rather than guessing.");
            return plan with
            {
                SpokenText = $"{plan.SpokenText} I can't see {wanted} on your screen at the moment — "
                             + "open it or scroll to it and ask me again.",
                NeedsAnotherLook = false
            };
        }

        _log.Info($"Second look found '{hit.Name}' at ({hit.ScreenX},{hit.ScreenY}).");

        // Found it. The mark is placed from what Windows reported rather than
        // from anything the model estimated, so the coordinates are measured
        // rather than guessed.
        _lessonFallbackTarget = null;
        return plan with
        {
            ElementName = string.IsNullOrWhiteSpace(hit.Name) ? wanted : hit.Name,
            ScopeName = "control",
            NeedsAnotherLook = false
        };
    }

    /// <summary>
    /// Raised when the user clicks a Windows notification or one of its
    /// buttons. The interface listens for this to bring the right task up.
    /// </summary>
    public event EventHandler<string>? AgentNotificationOpened;

    /// <summary>
    /// Acts on a notification the user pressed.
    ///
    /// The arguments are the short strings the toast was built with -
    /// "approve:agent-1234", "deny:agent-1234", "agent:agent-1234" - so that
    /// answering an approval never requires finding the window first, which for
    /// a background agent is most of the friction.
    /// </summary>
    private void HandleNotificationAction(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments) || AgentTasks is null)
        {
            return;
        }

        var parts = arguments.Split(':', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return;
        }

        var taskId = parts[1];

        switch (parts[0].ToLowerInvariant())
        {
            case "approve":
                AgentTasks.ApproveAction(taskId, true);
                _log.Info($"Approved {taskId} from a notification.");
                break;

            case "deny":
                AgentTasks.ApproveAction(taskId, false);
                _log.Info($"Denied {taskId} from a notification.");
                break;

            default:
                AgentNotificationOpened?.Invoke(this, taskId);
                break;
        }
    }

    /// <summary>One sentence naming what was just handed over.</summary>
    private static string DescribeSpawn(IReadOnlyList<AgentTaskRecord> tasks) => tasks.Count switch
    {
        0 => "I couldn't start that one, sorry.",
        1 => $"I've set an agent going on that: {tasks[0].Goal}. It'll work in the background while we carry on.",
        _ => $"I've set {tasks.Count} agents going. They'll work in the background while we carry on."
    };

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

    /// <summary>
    /// Raised once when a reply starts arriving, before any of it has been
    /// read. A listener should open an empty Metis bubble here.
    /// </summary>
    public event EventHandler? ResponseStreamStarted;

    /// <summary>
    /// Raised with each new piece of the answer as it is written. The argument
    /// is the text to append, not the whole answer so far.
    ///
    /// A turn that streams still ends with the usual <see cref="MessageAdded"/>
    /// carrying the complete reply, so a listener that ignores these two events
    /// behaves exactly as it did before and one that handles them must replace
    /// what it built rather than append to it again.
    /// </summary>
    public event EventHandler<string>? ResponseTextDelta;
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
        _pushToTalk.DirectAgentVoicePressed += OnDirectAgentVoicePressed;
        _pushToTalk.DirectAgentVoiceReleased += OnDirectAgentVoiceReleased;
        _pushToTalk.EmergencyStopPressed += OnEmergencyStopPressed;
        _pushToTalk.CancelPressed += (_, _) => TraceCancelKeyPressed?.Invoke(this, EventArgs.Empty);
        _pushToTalk.ContextActivationPressed += OnContextActivationPressed;
        _pushToTalk.ContextActivationReleased += OnContextActivationReleased;
        _pushToTalk.ContextActivationUpgraded += OnContextActivationUpgraded;
        _pushToTalk.ActiveListeningToggled += OnActiveListeningToggled;
        _pushToTalk.ContextShortcutsEnabled = Settings.ContextShortcutsEnabled;
        _pushToTalk.DirectAgentShortcutsEnabled = Settings.DirectAgentShortcutsEnabled;
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
        StartEntitlementRefreshTimer();
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
        _pushToTalk.DirectAgentShortcutsEnabled = Settings.DirectAgentShortcutsEnabled;
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

    /// <summary>
    /// Records a short stretch and returns what the configured speech-to-text
    /// route made of it.
    ///
    /// This exists because "I do not even know if dictation is working" was a
    /// real report, and it was a fair one: every other way of finding out
    /// requires asking Metis a question and then guessing whether a bad answer
    /// came from the microphone, the transcription or the model. This isolates
    /// the first two.
    ///
    /// It goes through the same TranscribeAsync the wake-word loop uses, so a
    /// route that cannot transcribe says so here in the same words rather than
    /// failing differently in a place the user cannot see.
    /// </summary>
    public async Task<string?> TestDictationAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_recorder.IsRecording)
        {
            throw new InvalidOperationException("Metis is already recording. Finish that first.");
        }

        _recorder.Start(Settings.PreferredMicrophoneId);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch
        {
            _recorder.Cancel();
            throw;
        }

        var recording = await _recorder.StopAsync(cancellationToken);
        if (recording is null || recording.Duration < TimeSpan.FromMilliseconds(400))
        {
            return null;
        }

        // Deliberately not the push-to-talk path. That one hands the audio to
        // the model, so a working result would prove only that the model can
        // hear -- not that the transcription this setting selects can.
        return await TranscribeAsync(recording, cancellationToken);
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

    public async Task<ProviderTestResult> PreviewVoiceAsync(
        string? voiceName = null,
        string? speechModel = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var voice = string.IsNullOrWhiteSpace(voiceName) ? (string.IsNullOrWhiteSpace(Settings.VoiceName) ? "Kore" : Settings.VoiceName) : voiceName;
        var model = string.IsNullOrWhiteSpace(speechModel)
            ? (string.IsNullOrWhiteSpace(Settings.SpeechModel) ? ModelCatalog.DefaultGeminiSpeechModel : Settings.SpeechModel)
            : speechModel;
        SetStatus($"Testing Gemini voice preview ({voice})…");

        var sw = Stopwatch.StartNew();
        try
        {
            var key = RequireApiKey();
            var sampleText = $"Hello! I'm Metis, using the {voice} voice on {model}.";
            var audio = await _gemini.SynthesizeSpeechAsync(
                key,
                model,
                Metis.AI.GeminiRequestBuilder.NormalizeVoice(voice),
                sampleText,
                cancellationToken);

            if (audio is not null && audio.PcmData.Length > 0)
            {
                await _audioPlayback.PlayAsync(audio, AudioPriority.Speech, cancellationToken);
                SetStatus($"Voice preview played successfully ({voice})");
                return new ProviderTestResult(model, true, $"Voice preview played successfully ({voice}).", sw.Elapsed);
            }

            return new ProviderTestResult(model, false, "No audio data was returned from voice synthesis.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            SetStatus($"Voice preview error: {ex.Message}");
            return new ProviderTestResult(model, false, ex.Message, sw.Elapsed);
        }
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

        // Their question is on screen either way — it was worth typing, and
        // swallowing it would look like Metis ignored them. What follows is an
        // answer about setup rather than a provider's stack trace.
        if (!CanAnswer(out var reason))
        {
            SetupRequired?.Invoke(this, reason);
            MessageAdded?.Invoke(this, new AssistantMessage(AssistantRole.Error, reason, DateTimeOffset.Now));
            SetStatus(reason);
            return Task.CompletedTask;
        }

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

        // Continuous listening has to turn audio into text locally, before any
        // turn starts, to check for the wake word -- there is no model call yet
        // for it to lean on the way push-to-talk does. "Native" has no
        // implementation that can do that, so it was previously discovered only
        // by trying: three silent ~5-second recording segments, each failing for
        // the same unfixable reason, before the retry loop's own message
        // finally explained it. This check says so immediately instead. It used
        // to test !Settings.SpeechEnabled, which is whether spoken *responses*
        // play -- unrelated to whether a speech-to-text provider is configured,
        // so a user with responses enabled and the default provider still fell
        // straight into the broken retry loop.
        if (Settings.SpeechToTextProvider is not ("AssemblyAI" or "Whisper.cpp"))
        {
            SetStatus("Continuous listening needs AssemblyAI or Whisper.cpp — open Setup, Voice & input, and choose one.");
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

        if (Settings.SpeechToTextProvider == "Whisper.cpp")
        {
            var whisper = await _whisperCpp.TranscribeAsync(
                ResolveLocalPath(Settings.WhisperCppExecutablePath),
                ResolveLocalPath(Settings.WhisperCppModelPath),
                recording,
                cancellationToken);
            return whisper.Text;
        }

        // "Native" -- the default, and the only other value Normalize allows --
        // has no implementation here. It works for push-to-talk and Inspect,
        // where the recorded audio goes straight to the model, which transcribes
        // it as part of answering. It cannot work here: this path exists to
        // decide *whether to call the model at all*, by checking a segment of
        // audio for the wake word before any turn starts, so there has to be
        // real text before the model is ever involved.
        //
        // This used to fall through to whisper.cpp unconditionally, silently
        // treating "Native" as "Whisper.cpp" for continuous listening only.
        // whisper.cpp and its model are not part of the installer's payload
        // (see Metis.App.csproj), so on an installed build with the default
        // provider this failed on every single segment -- three retries, about
        // fifteen seconds, before the existing failure handling below finally
        // explained why. Failing immediately, with the real reason, is both
        // faster and honest about what "Native" does and does not cover.
        throw new InvalidOperationException(
            "Continuous listening needs AssemblyAI or Whisper.cpp — open Setup, Voice & input, " +
            "and choose one. Push-to-talk and Inspect do not need this: their recording goes " +
            "straight to your AI provider.");
    }

    public async Task ClearMemoryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _memory.ClearAsync(cancellationToken);
        InvalidateMemoryCache();
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

        // CanAnswer and HasConfiguredProviderKey gate on the same condition, but
        // this one used to hardcode "add an API key" regardless of why the
        // route was refused -- so someone who was simply signed out, on a
        // Metis gateway route with no key of their own, was told to go paste a
        // credential that had nothing to do with the actual problem.
        if (!CanAnswer(out var voiceRefusalReason))
        {
            ReportError(voiceRefusalReason);
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
        _ = Task.Run(async () =>
        {
            try
            {
                await CompleteVoiceTurnAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Voice turn failed unexpectedly", ex);
            }
        });
    }

    private void OnDirectAgentVoicePressed(object? sender, EventArgs e)
    {
        if (_disposed || _recorder.IsRecording || _turnGate.CurrentCount == 0)
        {
            return;
        }

        // CanAnswer and HasConfiguredProviderKey gate on the same condition, but
        // this one used to hardcode "add an API key" regardless of why the
        // route was refused -- so someone who was simply signed out, on a
        // Metis gateway route with no key of their own, was told to go paste a
        // credential that had nothing to do with the actual problem.
        if (!CanAnswer(out var voiceRefusalReason))
        {
            ReportError(voiceRefusalReason);
            return;
        }

        try
        {
            _pendingPointer = null;
            PlayCue(MetisSound.RecordingStarted);
            _recorder.Start(Settings.PreferredMicrophoneId);
            State.Force(AssistantState.Listening);
            SetActivity(MetisActivityKind.Listening, "Listening for agent task");
            SetStatus("Listening for Agent Goal — release shortcut to dispatch");
        }
        catch (Exception exception)
        {
            ReportException("Metis could not start the microphone for agent task", exception);
        }
    }

    private void OnDirectAgentVoiceReleased(object? sender, EventArgs e)
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        _pendingActivation = ActivationKind.DirectAgent;
        _pendingPointer = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await CompleteDirectAgentVoiceTurnAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Direct agent voice turn failed unexpectedly", ex);
            }
        });
    }

    private async Task CompleteDirectAgentVoiceTurnAsync()
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

            PlayCue(MetisSound.RequestSent);
            MessageAdded?.Invoke(
                this,
                new AssistantMessage(
                    AssistantRole.User,
                    $"Direct Agent Voice ({recording.Duration.TotalSeconds:0.0}s)",
                    DateTimeOffset.Now));

            State.Force(AssistantState.Thinking);
            SetStatus("Transcribing agent goal…");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string? transcribedText = null;

            try
            {
                transcribedText = await TranscribeAsync(recording, cts.Token);
            }
            catch (Exception ex)
            {
                _log.Error("Direct agent voice transcription failed", ex);
            }

            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                try
                {
                    var geminiKey = _secretStore.ReadGeminiApiKey();
                    var openAiKey = _secretStore.ReadOpenAiApiKey();
                    if (Settings.AiProvider == "Gemini" && !string.IsNullOrWhiteSpace(geminiKey))
                    {
                        var req = new GeminiRequest(
                            "Transcribe the spoken audio verbatim. Return ONLY the transcription text, nothing else.",
                            RecordedAudioWav: recording.WavBytes);
                        var resp = await _gemini.GenerateAsync(geminiKey, Settings.ReasoningModel, req, onTextDelta: null, cts.Token);
                        transcribedText = resp.Text.Trim();
                    }
                    else if (Settings.AiProvider == "OpenAI" && !string.IsNullOrWhiteSpace(openAiKey))
                    {
                        var req = new GeminiRequest(
                            "Transcribe the spoken audio verbatim. Return ONLY the transcription text, nothing else.",
                            RecordedAudioWav: recording.WavBytes);
                        var resp = await _openAi.GenerateAsync(
                            openAiKey,
                            Settings.OpenAiReasoningModel,
                            Settings.OpenAiTranscriptionModel,
                            req,
                            onTextDelta: null,
                            cts.Token);
                        transcribedText = (!string.IsNullOrWhiteSpace(resp.Transcript) ? resp.Transcript : resp.Text).Trim();
                    }
                }
                catch (Exception cloudEx)
                {
                    _log.Error("Direct agent voice cloud transcription fallback failed", cloudEx);
                }
            }

            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                State.Force(AssistantState.Idle);
                SetStatus("Could not transcribe voice goal — check your speech provider or microphone");
                ReportError("Could not transcribe agent task voice recording.");
                return;
            }

            _log.Info($"Direct agent voice transcribed as: \"{transcribedText}\"");

            // The chord itself says this is a spawn, so there is no intent to
            // work out — only a spoken run-up to trim off, so the worker is
            // given "tidy my downloads" rather than "spawn an agent to tidy my
            // downloads", which would be an instruction about instructing
            // itself.
            List<string> goals = [];
            var spokenGoal = AgentIntentDetector.StripSpokenSpawnPrefix(transcribedText);
            if (!string.IsNullOrWhiteSpace(spokenGoal))
            {
                goals.Add(spokenGoal);
            }

            if (goals.Count == 0)
            {
                State.Force(AssistantState.Idle);
                SetStatus("No clear goal detected from voice input.");
                return;
            }

            RecordChatTurn("user", transcribedText, null);

            // SlashCommand rather than ModelProposed: the user held the agent
            // shortcut and dictated the goal, which is as direct an instruction
            // as typing it.
            var spawnedTasks = SpawnAgents(goals, AgentSpawnOrigin.SlashCommand);

            var spokenReply = spawnedTasks.Count switch
            {
                1 => $"I've spawned a background agent for you to {goals[0]}. It's working autonomously in the background now.",
                _ => $"I've spawned {spawnedTasks.Count} background agents for you. They are working autonomously in the background."
            };

            var fullReply = spokenReply + (spawnedTasks.Count == 1 ? $"\n\nTask ID: `{spawnedTasks[0].Id}`" : "");
            RecordChatTurn("metis", fullReply, null);
            MessageAdded?.Invoke(this, new AssistantMessage(AssistantRole.Metis, fullReply, DateTimeOffset.Now));

            PlayCue(MetisSound.RequestSent);

            if (Settings.SpeechEnabled)
            {
                SetStatus("Speaking…");
                State.Force(AssistantState.Speaking);
                var speech = await SynthesizeTextAsync(spokenReply, answeringProvider: null, cts.Token);
                if (speech is not null)
                {
                    var duration = GetAudioDuration(speech) ?? CompanionSpeech.ReadingDuration(spokenReply);
                    SetActivity(MetisActivityKind.Speaking, spokenReply);
                    StartCompanionResponse(spokenReply, duration, showBubble: true);

                    // PlayAsync already returns only once playback has stopped,
                    // so the Task.Delay that used to follow waited the clip out
                    // a second time and doubled every spoken reply.
                    await _audioPlayback.PlayAsync(speech, AudioPriority.Speech, cts.Token);
                }
                else
                {
                    SetActivity(MetisActivityKind.Speaking, spokenReply);
                    StartCompanionResponse(spokenReply, null, showBubble: true);
                    await Task.Delay(CompanionSpeech.ReadingDuration(spokenReply), cts.Token);
                }
            }
            else
            {
                SetActivity(MetisActivityKind.Speaking, spokenReply);
                StartCompanionResponse(spokenReply, null, showBubble: true);
                await Task.Delay(CompanionSpeech.ReadingDuration(spokenReply), cts.Token);
            }

            SetStatus("I'm ready. Ask me about your screen, or tell me what to do.");
            State.Force(AssistantState.Success);
            SetActivity(MetisActivityKind.Complete, "Done");
            PlayCue(MetisSound.TaskComplete);
            SetActivity(MetisActivityKind.Idle, string.Empty);
            State.Force(AssistantState.Idle);
        }
        catch (Exception exception)
        {
            ReportException("Metis could not dispatch direct agent voice task", exception);
        }
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

        // CanAnswer and HasConfiguredProviderKey gate on the same condition, but
        // this one used to hardcode "add an API key" regardless of why the
        // route was refused -- so someone who was simply signed out, on a
        // Metis gateway route with no key of their own, was told to go paste a
        // credential that had nothing to do with the actual problem.
        if (!CanAnswer(out var voiceRefusalReason))
        {
            ReportError(voiceRefusalReason);
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
    /// Accepts a single point tapped during tracing. Resolves as a point inspect
    /// on the control under that coordinate.
    /// </summary>
    public void SetTappedPoint(GuidancePoint point)
    {
        _pendingTrace = null;
        _pendingPointer = new PointerContext(point.ScreenX, point.ScreenY, 0, 0);
        _pendingActivation = ActivationKind.Inspect;
        _log.Info($"Inspected point at {point.ScreenX},{point.ScreenY}.");
        _ = RunTurnAsync("Explain what this control is on screen.", null, CancellationToken.None);
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

        _ = Task.Run(async () =>
        {
            try
            {
                await CompleteVoiceTurnAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Context voice turn failed unexpectedly", ex);
            }
        });
    }

    private void OnEmergencyStopPressed(object? sender, EventArgs e)
    {
        // Keep the low-level hook callback extremely short: cancel and drain
        // the action path immediately, then perform UI/audio cleanup off-hook.
        _turnCancellation?.Cancel();
        AgentTasks.CancelAll();
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

    /// <summary>
    /// Where one turn's time went, written to the log when it finishes.
    ///
    /// Every number is milliseconds from the start of the turn, so the gaps
    /// between them are the stages. <see cref="FirstWordMs"/> is the one that
    /// matters most: it is how long the user looked at nothing.
    /// </summary>
    private sealed class TurnTimings
    {
        public long CaptureMs { get; set; }
        public long AutomationMs { get; set; }
        public long RequestBuiltMs { get; set; }
        public long FirstWordMs { get; set; } = -1;
        public long AnswerMs { get; set; }
        public int ImageKiB { get; set; }
        public int PromptChars { get; set; }

        public string Describe(
            long totalMs,
            string provider,
            string model,
            bool streamed,
            ModelUsageReport? usage) =>
            $"Turn timing: capture {CaptureMs}ms, screen names {AutomationMs}ms, " +
            $"request built {RequestBuiltMs}ms, " +
            $"first word {(FirstWordMs < 0 ? "not streamed" : FirstWordMs + "ms")}, " +
            $"answer {AnswerMs}ms, total {totalMs}ms; " +
            $"image {ImageKiB} KiB, prompt {PromptChars} chars" +
            (usage is null
                ? string.Empty
                : $"; tokens in {usage.PromptTokens}, thinking {usage.ThoughtTokens}, out {usage.OutputTokens}") +
            $"; {provider} {model}{(streamed ? " streamed" : " buffered")}.";
    }

    /// <summary>
    /// A capture that carries the coordinate space and no pixels, for work that
    /// only needs to know where the screen is.
    /// </summary>
    private static ScreenCapture BoundsOnlyCapture(ScreenBounds bounds) => new(
        [],
        "Entire Windows desktop",
        bounds.Width,
        bounds.Height,
        bounds.Left,
        bounds.Top,
        bounds.Width,
        bounds.Height);

    /// <summary>
    /// Takes the screenshot, turning a failure into no image rather than a
    /// failed turn. Metis can still answer from the words alone.
    /// </summary>
    private async Task<ScreenCapture?> CaptureScreenAsync(
        ScreenCaptureDetail detail,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _capture.CaptureActiveWindowAsync(detail, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception captureError)
        {
            _log.Error("Full-desktop capture failed; continuing without an image.", captureError);
            SetStatus("Full-screen capture was unavailable; asking from voice/text only…");
            return null;
        }
    }

    /// <summary>
    /// Reads the accessibility tree. Best-effort in the same way: vision alone
    /// is a worse answer than vision plus names, and much better than none.
    /// </summary>
    private async Task<string?> DescribeScreenAsync(ScreenCapture capture, CancellationToken cancellationToken)
    {
        try
        {
            return await _uiAutomation.DescribeWindowAsync(capture, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception automationContextError)
        {
            _log.Error("UI Automation context was unavailable; continuing with vision only.", automationContextError);
            return null;
        }
    }

    private async Task RunTurnAsync(string prompt, RecordedAudio? recording, CancellationToken externalCancellation)
    {
        if (!await _turnGate.WaitAsync(0, externalCancellation))
        {
            SetStatus("Metis is already answering — stop or wait for the current reply");
            return;
        }

        _lastVoiceError = null;

        // The previous turn may still be finishing out loud. The gate is now
        // released as soon as its answer is on screen, so this one can start
        // while the last is still speaking or walking through a lesson — and
        // asking something new is a clear instruction to stop doing that.
        // Cancelling rather than only disposing is what actually stops it:
        // disposing a token source never signals its token, so the old speech
        // would have played on underneath the new answer.
        var previousTurn = _turnCancellation;
        _turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        if (previousTurn is not null)
        {
            try
            {
                previousTurn.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished and cleaned up.
            }

            previousTurn.Dispose();
        }

        _turnCancellation.CancelAfter(recording is null
            ? TimeSpan.FromSeconds(75)
            : TimeSpan.FromSeconds(120));
        var cancellationToken = _turnCancellation.Token;
        var turnNumber = ++_turnSequence;

        // Releasing the gate early means this method can still be running for
        // the previous turn, so its tail must not write status or state over
        // the newer one's.
        var gateReleased = false;
        void ReleaseTurnGate()
        {
            if (gateReleased)
            {
                return;
            }

            gateReleased = true;
            _turnGate.Release();
        }

        // One line per turn saying where the time went. There was no timing on
        // this path at all — the only stopwatches in the codebase were in the
        // provider self-tests — so how long a turn took could only be inferred
        // from the gap between two unrelated log lines, and the stages inside
        // it could not be seen at all.
        var turnClock = Stopwatch.StartNew();
        var timings = new TurnTimings();

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
            string? automationContext = null;

            // A fresh, complete virtual-desktop frame accompanies every turn
            // while screen context is enabled. Keyword gating made ordinary
            // follow-up prompts silently lose the screen.
            var shouldCaptureScreen = Settings.CaptureActiveWindow;
            if (shouldCaptureScreen)
            {
                // Pointing at one small control is the case where losing detail
                // to a downscale cannot be recovered, because the answer is a
                // coordinate. Everything else gets the smaller frame.
                var detail = activation == ActivationKind.Inspect || pendingTrace is not null
                    ? ScreenCaptureDetail.Full
                    : ScreenCaptureDetail.Standard;

                // The accessibility scan needs the coordinate space the capture
                // will use and nothing else from it, so when the capture can say
                // in advance where it is pointing, the two run together. They
                // used to run one after the other for no reason beyond the scan
                // taking a ScreenCapture as its argument, and each is worth
                // hundreds of milliseconds.
                var bounds = _capture.PeekCaptureBounds();
                var captureTask = CaptureScreenAsync(detail, cancellationToken);
                var automationTask = bounds is null
                    ? null
                    : DescribeScreenAsync(BoundsOnlyCapture(bounds), cancellationToken);

                SetActivity(MetisActivityKind.Capturing, "Capturing screen");
                screenshot = await captureTask;
                if (screenshot is not null)
                {
                    SetActivity(MetisActivityKind.Capturing, "Screen captured");
                    _log.Info($"Captured screen context with {screenshot.CaptureBackend} " +
                              $"at encoded {screenshot.Width}x{screenshot.Height}; " +
                              $"full bounds left={screenshot.ScreenLeft}, top={screenshot.ScreenTop}, " +
                              $"width={screenshot.SourceWidth}, height={screenshot.SourceHeight}; " +
                              $"{screenshot.ImageBytes.Length / 1024d:0.0} KiB {screenshot.ImageMimeType}" +
                              (screenshot.WithheldRegions > 0
                                  ? $"; {screenshot.WithheldRegions} region(s) withheld as private."
                                  : "."));
                }

                timings.CaptureMs = turnClock.ElapsedMilliseconds;
                timings.ImageKiB = (int)Math.Round((screenshot?.ImageBytes.Length ?? 0) / 1024d);

                if (automationTask is not null)
                {
                    automationContext = await automationTask;
                }
                else if (screenshot is not null)
                {
                    automationContext = await DescribeScreenAsync(screenshot, cancellationToken);
                }

                timings.AutomationMs = turnClock.ElapsedMilliseconds;

                // The scan was started against the bounds the capture said it
                // would use. If the capture then failed there is no image for
                // those coordinates to mean anything against.
                if (screenshot is null)
                {
                    automationContext = null;
                }
            }

            if (RequiresScreenObservation(prompt) && screenshot is null)
            {
                throw new InvalidOperationException(
                    "Metis could not capture the application behind its window, so it will not guess what is on your screen. " +
                    "Make sure Screen context is enabled and keep the target application open.");
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

            // Only the slash commands short-circuit the model now. Every other
            // way of asking for an agent -- "have an agent tidy my downloads",
            // or "spawn one" followed by "to do what?" and an answer -- reaches
            // the model, which can read the conversation and put the goal in its
            // reply. The regex could do neither, and when it missed, the request
            // fell through to a teaching prompt that forbids claiming to do
            // anything, so Metis described the agent instead of starting it.
            // That is the bug this replaces.
            if (AgentIntentDetector.TryExtractExplicitCommand(effectivePrompt, out var slashGoal) &&
                !string.IsNullOrWhiteSpace(slashGoal))
            {
                RecordChatTurn("user", effectivePrompt, screenshot?.WindowTitle);
                var spawnedTasks = SpawnAgents([slashGoal!], AgentSpawnOrigin.SlashCommand);
                var spokenReply = DescribeSpawn(spawnedTasks);
                var fullReply = spokenReply + (spawnedTasks.Count == 1 ? $"\n\nTask ID: `{spawnedTasks[0].Id}`" : "");
                RecordChatTurn("metis", fullReply, screenshot?.WindowTitle);
                MessageAdded?.Invoke(this, new AssistantMessage(AssistantRole.Metis, fullReply, DateTimeOffset.Now));

                if (Settings.SpeechEnabled)
                {
                    SetStatus("Speaking…");
                    State.Force(AssistantState.Speaking);
                    var speech = await SynthesizeTextAsync(spokenReply, answeringProvider: null, cancellationToken);
                    if (speech is not null)
                    {
                        var duration = GetAudioDuration(speech) ?? CompanionSpeech.ReadingDuration(spokenReply);
                        SetActivity(MetisActivityKind.Speaking, spokenReply);
                        StartCompanionResponse(spokenReply, duration, showBubble: true);

                        // PlayAsync returns only once playback has stopped, so
                        // waiting the duration again doubled the pause here.
                        await _audioPlayback.PlayAsync(speech, AudioPriority.Speech, cancellationToken);
                    }
                    else
                    {
                        SetActivity(MetisActivityKind.Speaking, spokenReply);
                        StartCompanionResponse(spokenReply, null, showBubble: true);
                        await Task.Delay(CompanionSpeech.ReadingDuration(spokenReply), cancellationToken);
                    }
                }
                else
                {
                    SetActivity(MetisActivityKind.Speaking, spokenReply);
                    StartCompanionResponse(spokenReply, null, showBubble: true);
                    await Task.Delay(CompanionSpeech.ReadingDuration(spokenReply), cancellationToken);
                }

                SetStatus("I'm ready. Ask me about your screen, or tell me what to do.");
                State.Force(AssistantState.Success);
                SetActivity(MetisActivityKind.Complete, "Done");
                PlayCue(MetisSound.TaskComplete);
                SetActivity(MetisActivityKind.Idle, string.Empty);
                State.Force(AssistantState.Idle);
                return;
            }

            var region = BuildRegion(pendingTrace, screenshot);
            var pointer = await BuildPointerContextAsync(
                pendingPointer,
                activation,
                screenshot,
                pendingTrace,
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
                DescribeRecentTurns(),
                region,
                _academicTeaching,
                Settings.UserName,
                screenshot?.WithheldRegions ?? 0);
            SetActivity(MetisActivityKind.Thinking, "Thinking");

            // The answer goes on screen as it is written rather than when it is
            // finished. Most of a turn's wait used to be the model completing a
            // structured reply the user could not see any of — the sentence
            // itself is ready long before the lesson steps and coordinates that
            // follow it.
            timings.RequestBuiltMs = turnClock.ElapsedMilliseconds;
            timings.PromptChars = effectivePrompt.Length + (automationContext?.Length ?? 0);

            var answerStream = new TurnTextStream(
                () =>
                {
                    timings.FirstWordMs = turnClock.ElapsedMilliseconds;
                    ResponseStreamStarted?.Invoke(this, EventArgs.Empty);
                },
                delta => ResponseTextDelta?.Invoke(this, delta));
            var response = await GenerateWithSelectedProviderAsync(request, answerStream, cancellationToken);
            timings.AnswerMs = turnClock.ElapsedMilliseconds;
            _log.Info(timings.Describe(
                turnClock.ElapsedMilliseconds,
                response.Provider,
                response.Model,
                answerStream.HasPublished,
                response.Usage));

            MessageAdded?.Invoke(
                this,
                new AssistantMessage(AssistantRole.Metis, response.Text, DateTimeOffset.Now));
            RecordChatTurn("metis", response.Text, screenshot?.WindowTitle);

            // The answer is on screen, so Metis is ready for the next question.
            // Everything below — synthesising the voice, saying it, marking the
            // screen, walking a lesson — is the reply being delivered, not the
            // reply being worked out, and the gate used to be held for all of
            // it. That was routinely eight to ten seconds of the app refusing
            // to listen while the user was already reading its answer.
            ReleaseTurnGate();

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
                if (screenshot is not null && (plan.HasAnnotation || plan.LessonSteps.Count > 0))
                {
                    plan = plan with { ScreenObserved = true };
                }
                else
                {
                    throw new InvalidOperationException(
                        "The AI did not confirm that it inspected Metis's current screenshot, so no screen answer was trusted.");
                }
            }

            // The model said it could not confirm what it was being asked
            // about. Go and look, rather than answering anyway.
            if (plan.WantsSecondLook)
            {
                plan = await TakeSecondLookAsync(plan, screenshot, cancellationToken);
            }

            // Work the model decided to hand over. This runs before the lesson
            // branch below, because a reply can legitimately do both: start an
            // agent on the long job and teach the user something while it runs.
            //
            // The agents start immediately rather than behind a confirmation,
            // because being asked to confirm what you just asked for is the
            // thing that made the old flow feel like a chatbot. What protects
            // the user instead is what the agent may do once running: a
            // model-proposed agent is held at AskApproval whatever the autonomy
            // setting says, so it pauses before anything destructive, and it
            // appears in the drawer where it can be stopped.
            if (plan.AgentsToSpawn.Count > 0)
            {
                // No event needed to announce these: AgentTaskManager.TaskCreated
                // already posts a line in the chat and lights the agent dots for
                // every spawn, whichever path started it.
                SpawnAgents(plan.AgentsToSpawn, AgentSpawnOrigin.ModelProposed);
            }

            var bubbleCue = string.IsNullOrWhiteSpace(plan.BubbleCue) ? string.Empty : plan.BubbleCue.Trim();
            _log.Info($"Assistant reply received: {plan.LessonSteps.Count} lesson step(s), " +
                      $"{plan.AgentsToSpawn.Count} agent(s) requested, " +
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

                if (IsCurrentTurn(turnNumber))
                {
                    SetActivity(MetisActivityKind.Idle, string.Empty);
                    State.Force(AssistantState.Idle);
                }

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
            var speakReply = Settings.SpeechEnabled;
            if (speakReply)
            {
                SetStatus("Preparing Metis's voice…");
                try
                {
                    var audio = await SynthesizeWithProviderAsync(response, plan, cancellationToken);
                    if (audio is not null)
                    {
                        State.Force(AssistantState.Speaking);
                        var spokenLine = CompanionSpeech.ChooseLine(plan.SpokenText, bubbleCue);
                        SetActivity(MetisActivityKind.Speaking, spokenLine ?? "Speaking");
                        SetStatus("Speaking…");
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
                        finalStatus += _lastVoiceError is not null
                            ? $" — voice was unavailable: {_lastVoiceError}"
                            : " — voice was unavailable";
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception speechError)
                {
                    _log.Error("Speech output failed; the text answer remains available.", speechError);
                    finalStatus += $" — speech failed ({speechError.Message}), text still works";
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
                if (writtenLine is not null)
                {
                    SetActivity(MetisActivityKind.Speaking, writtenLine);
                }
                StartCompanionResponse(
                    writtenLine ?? string.Empty,
                    CompanionSpeech.ReadingDuration(writtenLine),
                    writtenLine is not null);
            }

            await RecordTurnMemoryAsync(task, plan, screenshot?.WindowTitle, true, CancellationToken.None);

            if (IsCurrentTurn(turnNumber))
            {
                State.Force(AssistantState.Success);
                SetActivity(MetisActivityKind.Complete, "Done");
                PlayCue(MetisSound.TaskComplete);
                SetStatus(finalStatus);

                // The "Done" state used to be held here for 1.2s before going
                // idle, and because the gate was not released until this method
                // returned, that was a second of the app refusing the next
                // question for the sake of an indicator.
                SetActivity(MetisActivityKind.Idle, string.Empty);
                State.Force(AssistantState.Idle);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A turn is also cancelled when the user asks something else while
            // it is still speaking, and in that case the newer turn owns the
            // status line — saying "stopped" over its "Thinking…" would report
            // the interruption as a failure.
            if (IsCurrentTurn(turnNumber))
            {
                State.Force(AssistantState.Idle);
                SetStatus("Request stopped or timed out");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentTurn(turnNumber))
            {
                ReportException("Metis could not get an answer", exception);
            }
            else
            {
                _log.Error("A superseded turn failed while finishing; the newer turn was left alone.", exception);
            }
        }
        finally
        {
            ReleaseTurnGate();
        }
    }

    /// <summary>
    /// Whether the given turn is still the one the user is waiting on. False
    /// once a newer question has been asked over the top of it.
    /// </summary>
    private bool IsCurrentTurn(int turnNumber) => _turnSequence == turnNumber;

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
        IReadOnlyList<GuidancePoint>? pendingTrace,
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
            if (pendingTrace is { Count: >= 3 })
            {
                var (rx, ry, rw, rh) = TracePath.Bounds(pendingTrace);
                try
                {
                    hovered = await _uiAutomation.DescribeRegionAsync(
                        capture,
                        rx,
                        ry,
                        rw,
                        rh,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _log.Error("The elements in the traced region could not be read; continuing with vision only.", exception);
                }

                // The lasso overlay mark for the whole traced area is already visible on screen,
                // so we do not point an arrow to just the center point.
            }
            else
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
                // resolved "this" to before it answers.
                ShowPointerArrow(
                    pointer.ScreenX,
                    pointer.ScreenY,
                    hovered is null ? "Looking here" : null);
            }
        }

        return pointer with { NormalizedX = normalizedX, NormalizedY = normalizedY, HoveredElement = hovered };
    }

    /// <summary>
    /// Describes what Metis has learned that is relevant to this application.
    ///
    /// The memory document is cached between turns. It was being read from
    /// disk and deserialized on the request path of every single turn, and it
    /// only changes when Metis itself writes to it — which happens here, in
    /// this process, so the cache can simply be dropped at those points rather
    /// than being timed or watched.
    /// </summary>
    private async Task<string?> DescribeSkillsAsync(string? application, CancellationToken cancellationToken)
    {
        if (!Settings.MemoryEnabled)
        {
            return null;
        }

        try
        {
            var document = _cachedMemory ??= await _memory.LoadAsync(cancellationToken);
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
    /// Forgets the cached memory document, so the next turn reads it again.
    /// Called wherever Metis writes to memory.
    /// </summary>
    private void InvalidateMemoryCache() => _cachedMemory = null;

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
            InvalidateMemoryCache();

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
                InvalidateMemoryCache();
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
                SetStatus($"Step {lesson.StepNumber} of {lesson.Steps.Count}: {step.Instruction}");

                // What the screen looked like before the learner was asked, so
                // "has it changed?" has something to be a change from.
                var titleBefore = _lastLessonCapture?.WindowTitle;
                var nextStep = lesson.CurrentIndex + 1 < lesson.Steps.Count
                    ? lesson.Steps[lesson.CurrentIndex + 1]
                    : null;
                var nextTargetBefore = await IsElementPresentAsync(nextStep?.ElementName, cancellationToken);

                var held = await PresentLessonStepAsync(lesson, step, cancellationToken);

                // Metis still does not stop dead. It says the step and holds the
                // mark long enough to be followed, because a walkthrough that
                // blocks until each thing is done cannot be listened to while
                // you work, which is the whole point of being talked through
                // something.
                await Task.Delay(held, cancellationToken);

                // Then it looks. This is the part that used to be missing: the
                // screen was re-read between steps only to re-place the mark,
                // and never to ask whether the learner had actually kept up. So
                // a lesson marched on regardless, and every mark after the one
                // that was missed pointed at a screen nobody was on.
                var progress = await ReadStepProgressAsync(
                    step, nextStep, nextTargetBefore, titleBefore, cancellationToken);

                if (progress == StepProgress.NotYet && lesson.AttemptsOnCurrentStep < MaxLessonNudges)
                {
                    lesson = lesson.Retry();
                    LessonChanged?.Invoke(this, lesson);
                    await NudgeLearnerAsync(lesson, step, cancellationToken);
                    continue;
                }

                if (progress == StepProgress.NotYet)
                {
                    // Nudged as often as is useful. Carrying on beats standing
                    // over someone repeating myself, and they may simply be
                    // doing it a different way.
                    _log.Info($"Step {lesson.StepNumber} was never confirmed. Moving on.");
                }

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

            if (_lastVoiceError is not null)
            {
                SetStatus($"Ready — voice synthesis failed: {_lastVoiceError}");
            }
            else
            {
                SetStatus("I'm ready. Ask me about your screen, or tell me what to do.");
            }
        }
    }

    /// <summary>
    /// Takes a fresh screenshot for the next step's annotation to be placed
    /// against. A failure here is not fatal: the previous capture is kept, so
    /// the next mark is placed against a slightly older screen rather than not
    /// drawn at all.
    /// </summary>
    /// <summary>
    /// How many times Metis will point something out again before moving on.
    ///
    /// Two. The first nudge catches the common case, which is the learner not
    /// having noticed the mark; the second re-explains. A third would be
    /// standing over someone repeating myself, and they may simply be doing it
    /// another way.
    /// </summary>
    private const int MaxLessonNudges = 2;

    /// <summary>How long to keep watching for a step to take effect before nudging.</summary>
    private static readonly TimeSpan StepWatchBudget = TimeSpan.FromSeconds(9);

    /// <summary>How often to look while waiting.</summary>
    private static readonly TimeSpan StepWatchInterval = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Watches the screen for the step to take effect.
    ///
    /// Polls locally — no model call, nothing to pay for, and a hard budget so
    /// it cannot hang. Returns as soon as there is an answer, which for a
    /// learner who did the step immediately is usually the first look.
    /// </summary>
    private async Task<StepProgress> ReadStepProgressAsync(
        LessonStep step,
        LessonStep? nextStep,
        bool nextTargetBefore,
        string? titleBefore,
        CancellationToken cancellationToken)
    {
        if (!Settings.LessonWaitsForLearner || !StepCompletionEvidence.CanBeChecked(step, nextStep))
        {
            return StepProgress.Unknowable;
        }

        var deadline = DateTimeOffset.UtcNow + StepWatchBudget;
        var last = StepProgress.Unknowable;

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await RefreshLessonCaptureAsync(cancellationToken);

            string? elements = null;
            try
            {
                if (_lastLessonCapture is not null)
                {
                    elements = await _uiAutomation.DescribeWindowAsync(_lastLessonCapture, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Being unable to read the screen is not evidence the learner
                // failed, so it must never be reported as such.
                _log.Error("Metis could not read the screen while checking a step.", exception);
                return StepProgress.Unknowable;
            }

            last = StepCompletionEvidence.Read(
                step,
                nextStep,
                await IsElementPresentAsync(nextStep?.ElementName, cancellationToken),
                nextTargetBefore,
                _lastLessonCapture?.WindowTitle,
                titleBefore,
                elements);

            if (last != StepProgress.NotYet)
            {
                return last;
            }

            await Task.Delay(StepWatchInterval, cancellationToken);
        }

        return last;
    }

    /// <summary>Whether a named control can be found on screen right now.</summary>
    private async Task<bool> IsElementPresentAsync(string? elementName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(elementName))
        {
            return false;
        }

        try
        {
            return await _uiAutomation.FindElementAsync(elementName, cancellationToken) is not null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Points the step out again, a little differently each time.
    ///
    /// The first attempt re-marks it and says something short, because the
    /// usual reason a step has not happened is that the mark was not noticed.
    /// The second gives the reason and says what to look for, because by then
    /// the likelier problem is that the instruction was not understood.
    /// </summary>
    private async Task NudgeLearnerAsync(LessonState lesson, LessonStep step, CancellationToken cancellationToken)
    {
        var line = lesson.AttemptsOnCurrentStep <= 1
            ? $"It's this one — {step.TargetLabel ?? step.ElementName ?? "here"}."
            : BuildSecondNudge(step);

        await RefreshLessonCaptureAsync(cancellationToken);
        await PresentLessonStepAsync(lesson, step with { Instruction = line }, cancellationToken);
    }

    private static string BuildSecondNudge(LessonStep step)
    {
        var why = string.IsNullOrWhiteSpace(step.Why) ? null : step.Why!.Trim().TrimEnd('.');
        var done = string.IsNullOrWhiteSpace(step.DoneWhen) ? null : step.DoneWhen!.Trim().TrimEnd('.');

        if (why is not null && done is not null)
        {
            return $"{step.Instruction} {why}. You'll know it worked when {char.ToLowerInvariant(done[0])}{done[1..]}.";
        }

        return done is not null
            ? $"{step.Instruction} You'll know it worked when {char.ToLowerInvariant(done[0])}{done[1..]}."
            : step.Instruction;
    }

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
        if (!Settings.SpeechEnabled)
        {
            SetActivity(MetisActivityKind.Speaking, line);
            StartCompanionResponse(line, null, showBubble: true);
            await Task.Delay(CompanionSpeech.ReadingDuration(line), cancellationToken);
            SetActivity(MetisActivityKind.Idle, string.Empty);
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
                var duration = GetAudioDuration(audio) ?? CompanionSpeech.ReadingDuration(line);
                SetActivity(MetisActivityKind.Speaking, line);
                StartCompanionResponse(line, duration, showBubble: true);

                // PlayAsync returns only when playback has stopped. Waiting the
                // duration on top of that doubled the pause on every lesson
                // step, which is where it hurt most: a ten-step walkthrough
                // spent a full extra minute in silence.
                await _audioPlayback.PlayAsync(audio, AudioPriority.Speech, cancellationToken);
                SetActivity(MetisActivityKind.Idle, string.Empty);
            }
            else
            {
                SetActivity(MetisActivityKind.Speaking, line);
                StartCompanionResponse(line, null, showBubble: true);
                await Task.Delay(CompanionSpeech.ReadingDuration(line), cancellationToken);
                SetActivity(MetisActivityKind.Idle, string.Empty);
                _log.Info($"No speech audio came back for a lesson step, so it was shown but not spoken: \"{line}\"");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetActivity(MetisActivityKind.Speaking, line);
            StartCompanionResponse(line, null, showBubble: true);
            await Task.Delay(CompanionSpeech.ReadingDuration(line), cancellationToken);
            SetActivity(MetisActivityKind.Idle, string.Empty);
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
        InvalidateMemoryCache();
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

    /// <summary>
    /// Whether the configured provider has a key of the user's own saved for it.
    ///
    /// Note what this does *not* consider: accounts, plans, and the gateway.
    /// This answers only "can Metis call this provider on the user's own
    /// credential", because that is the question ProviderRouting asks first and
    /// the answer that outranks everything else.
    /// </summary>
    private bool HasOwnKeyForConfiguredProvider() => Settings.AiProvider switch
    {
        "OpenAI" => HasOpenAiKey,
        "Claude" => HasClaudeKey,
        "OpenRouter" => HasOpenRouterKey,
        "OpenClaw" or "Ollama" => true,
        "Metis" => false,
        "Automatic" => HasAnyApiKey,
        _ => HasGeminiKey
    };

    /// <summary>
    /// Which of the four routes this turn takes. One call, so nothing anywhere
    /// else re-derives it slightly differently.
    /// </summary>
    private ProviderRoute CurrentRoute() => ProviderRouting.Decide(
        Settings.AiProvider,
        HasOwnKeyForConfiguredProvider(),
        OwnKeyIsAllowed,
        Account.IsSignedIn && SessionAccessToken is not null,
        MetisBackend.HasGateway(Settings.MetisGatewayUrl));

    /// <summary>
    /// Whether this account may answer on a key of its own.
    ///
    /// Bringing your own key is part of Pro. Everybody keeps it until billing is
    /// switched on, which is what Can() returns while there is no entitlement
    /// snapshot to say otherwise — so this is a real gate that is deliberately
    /// open until the day plans start costing money.
    /// </summary>
    private bool OwnKeyIsAllowed => Can(MetisFeature.CustomAiProvider);

    /// <summary>
    /// Whether Metis can answer at all right now.
    ///
    /// Kept under its old name because every call site reads correctly with it,
    /// but it is a wider question than it used to be: having a key of your own
    /// is now one of three ways to be able to answer, alongside a local model
    /// and a signed-in plan.
    /// </summary>
    private bool HasConfiguredProviderKey() => CurrentRoute() != ProviderRoute.RefuseNeedsKeyOrPlan;

    /// <summary>
    /// Whether Metis has any way to answer, and if not, what to tell the user.
    ///
    /// This exists because the typed path did not check. Voice checked, and said
    /// something useful; typing went straight to the provider and surfaced
    /// whatever exception came back — "No Gemini API key is saved. Open Setup and
    /// add one first." — as a red error bubble. To someone who has just
    /// installed Metis and typed a question, that reads as a broken program
    /// rather than as an unfinished setup, and there is nothing in it to click.
    /// </summary>
    public bool CanAnswer(out string reason)
    {
        if (CurrentRoute() != ProviderRoute.RefuseNeedsKeyOrPlan)
        {
            reason = string.Empty;
            return true;
        }

        reason = ProviderRouting.ExplainRefusal(Account.IsSignedIn, OwnKeyIsAllowed);
        return false;
    }


    /// <summary>
    /// The stored secret for a provider Metis reaches over a configurable
    /// endpoint. OpenClaw's token is optional; OpenRouter's key is not.
    /// </summary>
    private string? EndpointProviderCredential(string provider) => provider switch
    {
        "OpenClaw" => _secretStore.ReadOpenClawToken(),
        "OpenRouter" => _secretStore.ReadOpenRouterApiKey(),

        // The gateway's credential is the session token, not a provider key.
        // It proves who is asking; it does not authorise a model call, which is
        // decided on the server against the plan behind that identity.
        ProviderRouting.GatewayProviderId => SessionAccessToken,
        _ => null
    };

    // ===================== Telling the user it is waking =====================

    /// <summary>
    /// How many gateway calls are outstanding. The notice is one thing shared
    /// between them: two overlapping calls should not produce two announcements,
    /// and the first one to finish should not clear a notice the second still
    /// needs.
    /// </summary>
    private int _gatewayCallsInFlight;

    /// <summary>Cancels the pending "it is waking" announcement when the call answers first.</summary>
    private CancellationTokenSource? _wakeNoticeTimer;

    /// <summary>
    /// The activity and status the notch was showing before the notice replaced
    /// them, so the wait can be undone rather than left as a stale line. Null
    /// while no notice is showing.
    /// </summary>
    private MetisActivity? _wakeNoticeShown;
    private MetisActivity? _wakeNoticeRestoreActivity;
    private string? _wakeNoticeRestoreStatus;

    /// <summary>
    /// Whether the last finished gateway call answered.
    ///
    /// Read by the support diagnostics, which run on the interface thread from
    /// a menu click and so cannot afford to probe: a sleeping free-tier gateway
    /// would freeze the menu for the better part of a minute. What happened
    /// last time is the answer a person can act on anyway — "it worked a moment
    /// ago" and "it has not worked all morning" are different complaints.
    /// </summary>
    public bool LastGatewayCallSucceeded { get; private set; }

    /// <summary>
    /// Whether a gateway call is outstanding and has been slow enough that the
    /// notch is saying so. True only while the waking notice is up.
    /// </summary>
    public bool GatewayMayBeWaking => Volatile.Read(ref _wakeNoticeShown) is not null;

    /// <summary>
    /// Runs a gateway call, and if it has not answered within three seconds,
    /// says so in the notch.
    ///
    /// This deliberately reuses the two channels the interface already listens
    /// on — <see cref="ActivityChanged"/> for the notch's own line and
    /// <see cref="StatusChanged"/> for the chat's — rather than inventing a
    /// second way to say something. Both handlers marshal to the interface
    /// thread themselves, so nothing here touches it and nothing blocks.
    ///
    /// The notice is put up on a timer that the call cancels when it returns, so
    /// a warm request costs one <c>Task.Delay</c> that never fires.
    /// </summary>
    private async Task<T> WhileGatewayMayBeWakingAsync<T>(Func<Task<T>> call)
    {
        BeginGatewayCall();
        try
        {
            var result = await call();

            // Recorded for the support diagnostics. "It answered" here means
            // the call completed rather than that the server was happy with it
            // — a 403 about a plan is the gateway working correctly.
            LastGatewayCallSucceeded = true;
            return result;
        }
        catch
        {
            LastGatewayCallSucceeded = false;
            throw;
        }
        finally
        {
            EndGatewayCall();
        }
    }

    private void BeginGatewayCall()
    {
        if (Interlocked.Increment(ref _gatewayCallsInFlight) != 1)
        {
            return;
        }

        var timer = new CancellationTokenSource();
        Interlocked.Exchange(ref _wakeNoticeTimer, timer)?.Dispose();
        _ = ShowWakeNoticeAfterDelayAsync(timer.Token);
    }

    private void EndGatewayCall()
    {
        if (Interlocked.Decrement(ref _gatewayCallsInFlight) != 0)
        {
            return;
        }

        NoteGatewayAnswered();
    }

    /// <summary>
    /// The gateway is talking. Called both when a call returns and, for a turn
    /// that streams, as soon as the first words arrive — a reply being written
    /// on screen is proof enough that nothing is asleep any more, and leaving
    /// "waking up" over a sentence the user is already reading would be worse
    /// than never having said it.
    /// </summary>
    private void NoteGatewayAnswered()
    {
        Interlocked.Exchange(ref _wakeNoticeTimer, null)?.Cancel();
        ClearWakeNotice();
    }

    private async Task ShowWakeNoticeAfterDelayAsync(CancellationToken settled)
    {
        try
        {
            await Task.Delay(GatewayRetry.NoticeAfter, settled);
        }
        catch (OperationCanceledException)
        {
            // Answered inside three seconds, which is the ordinary case.
            return;
        }

        if (_disposed || settled.IsCancellationRequested)
        {
            return;
        }

        _wakeNoticeRestoreActivity = CurrentActivity;
        _wakeNoticeRestoreStatus = CurrentStatus;
        _wakeNoticeShown = new MetisActivity(MetisActivityKind.Thinking, GatewayRetry.Notice);

        CurrentActivity = _wakeNoticeShown;
        ActivityChanged?.Invoke(this, CurrentActivity);
        SetStatus(GatewayRetry.Notice);
    }

    /// <summary>
    /// Puts back whatever the notice covered up — but only if it is still the
    /// notice that is showing. Anything else means the turn moved on while the
    /// gateway was waking, and restoring a line from before that would undo a
    /// newer, truer one.
    /// </summary>
    private void ClearWakeNotice()
    {
        var shown = Interlocked.Exchange(ref _wakeNoticeShown, null);
        if (shown is null)
        {
            return;
        }

        if (ReferenceEquals(CurrentActivity, shown))
        {
            CurrentActivity = _wakeNoticeRestoreActivity ?? MetisActivity.Idle;
            ActivityChanged?.Invoke(this, CurrentActivity);
        }

        if (string.Equals(CurrentStatus, GatewayRetry.Notice, StringComparison.Ordinal))
        {
            SetStatus(_wakeNoticeRestoreStatus ?? string.Empty);
        }

        _wakeNoticeRestoreActivity = null;
        _wakeNoticeRestoreStatus = null;
    }

    /// <summary>
    /// How long to wait for the gateway to say what an account may do.
    ///
    /// Sixty seconds, which looks absurd for one small GET and is not. The
    /// gateway runs on Render's free tier, which stops the container after about
    /// fifteen minutes with no traffic and takes roughly fifty seconds to build
    /// a new one on the next request. This call is made at start-up, and
    /// start-up is very often the first traffic of the day — so a twenty-second
    /// timeout did not measure a slow network, it measured a cold start, and it
    /// gave up nine tenths of the way through one. Every time. The user saw
    /// nothing: the failure logs at info level and the app carries on with its
    /// cached snapshot, which is why this went unnoticed for as long as it did.
    ///
    /// Waiting a minute costs nothing here because nothing waits on it. It runs
    /// unawaited beside a working interface, and the answer only ever makes the
    /// interface more accurate than the cache it is already showing.
    /// </summary>
    private static readonly TimeSpan EntitlementTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often to ask again.
    ///
    /// Fifteen minutes because the thing this catches is a plan bought on the
    /// website while the app is open, and the app used to catch it never: the
    /// only two calls were at start-up and just after sign-in, so somebody who
    /// upgraded saw no change until they next restarted Metis — at precisely the
    /// moment they had paid for one. Quarter of an hour is short enough that a
    /// purchase lands while the person is still thinking about it, and long
    /// enough that a day at the desk is under forty requests.
    /// </summary>
    private static readonly TimeSpan EntitlementRefreshInterval = TimeSpan.FromMinutes(15);

    private System.Windows.Threading.DispatcherTimer? _entitlementTimer;

    /// <summary>
    /// Whether a refresh is already in the air. Not a lock: the correct answer
    /// to "asked twice at once" is to drop the second, because both would return
    /// the same snapshot and the first is already on its way.
    /// </summary>
    private int _entitlementRefreshRunning;

    /// <summary>
    /// Starts asking the gateway what this account may do, and keeps asking.
    ///
    /// Called once, from <see cref="InitializeAsync"/>, on the interface thread —
    /// so the ticks arrive there too and the panels listening to
    /// <see cref="EntitlementsChanged"/> are updated without marshalling. Runs
    /// whether or not anyone is signed in, because signing in is one of the
    /// things it needs to notice, and a refresh with no session costs a
    /// comparison and returns.
    /// </summary>
    private void StartEntitlementRefreshTimer()
    {
        if (_entitlementTimer is not null)
        {
            return;
        }

        _entitlementTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = EntitlementRefreshInterval
        };

        _entitlementTimer.Tick += OnEntitlementRefreshTick;
        _entitlementTimer.Start();
    }

    private void OnEntitlementRefreshTick(object? sender, EventArgs e) =>
        _ = RefreshEntitlementsAsync();

    /// <summary>
    /// Asks the gateway what this account may do, and remembers the answer.
    ///
    /// Everything about this method is written so that failure is quiet. There
    /// is no gateway in a self-hosted build; there is no session when nobody is
    /// signed in; the service may be asleep, cold, or briefly unreachable. None
    /// of those are reasons to interrupt anyone, because the client already has
    /// a usable fallback in the cached snapshot and, below that, in its own
    /// compiled table. The one thing it must never do is invent an answer.
    ///
    /// Safe to call from anywhere at any time. A second call while one is in
    /// flight returns immediately rather than queueing behind it: they would
    /// both fetch the same snapshot, and the timer ticking during a cold start
    /// must not stack up a queue of minute-long requests.
    /// </summary>
    public async Task RefreshEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        var token = SessionAccessToken;
        if (_disposed || token is null || !MetisBackend.HasGateway(Settings.MetisGatewayUrl))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _entitlementRefreshRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // Three attempts with a doubling wait, because the first request
            // after an idle period is spent waking the container rather than
            // answering: Render returns it a 502 or drops it once the build
            // takes longer than its own edge timeout, and a later attempt lands
            // on a service that is now warm. Safe to repeat because this is a
            // GET of a snapshot — see GatewayRetry, which owns both rules.
            for (var attempt = 1; attempt <= GatewayRetry.MaxAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    await Task.Delay(GatewayRetry.BackoffBefore(attempt), cancellationToken);
                }

                if (await TryRefreshEntitlementsAsync(token, cancellationToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The caller withdrew the question. Not worth a line in the log.
        }
        finally
        {
            Interlocked.Exchange(ref _entitlementRefreshRunning, 0);
        }
    }

    /// <summary>
    /// One attempt. True when the gateway answered with something this build
    /// could read and apply; false when it is worth trying again.
    /// </summary>
    private async Task<bool> TryRefreshEntitlementsAsync(
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = EntitlementTimeout };
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get,
                new Uri(new Uri(MetisBackend.ResolveGatewayUrl(Settings.MetisGatewayUrl)), "v1/me"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Wrapped so that a request spent waiting on a cold start says so in
            // the notch instead of leaving the interface silent for a minute.
            using var response = await WhileGatewayMayBeWakingAsync(
                () => http.SendAsync(request, cancellationToken));

            if (!response.IsSuccessStatusCode)
            {
                _log.Info($"The gateway did not report entitlements ({(int)response.StatusCode}).");

                // A refusal is an answer and repeating it changes nothing. Only
                // the shapes that mean "the service is not up yet" are worth a
                // second go.
                return !GatewayRetry.IsWaking((int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var me = System.Text.Json.JsonSerializer.Deserialize<MeResponse>(
                body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            if (me is null)
            {
                return true;
            }

            // The account is re-read from the same answer, so the plan the
            // application shows and the plan the gateway will enforce cannot
            // disagree: they came from one response.
            var refreshed = Account with
            {
                Role = Metis.Core.Services.Entitlements.ParseRole(me.Role),
                Plan = Metis.Core.Services.Entitlements.ParsePlan(me.Plan),
                EmailVerified = me.EmailVerified
            };

            if (refreshed != Account)
            {
                // A plan that changed while the user was working in another
                // window is the one change that happens to them rather than
                // because of something they just did, so it is worth a sound.
                var planMoved = refreshed.Plan != Account.Plan && Account.IsSignedIn;
                SignIn(refreshed);
                if (planMoved)
                {
                    PlayCue(MetisSound.PlanChanged);
                }
            }

            var granted = me.Features
                .Select(name => Enum.TryParse<MetisFeature>(name, out var feature) ? feature : (MetisFeature?)null)
                .Where(feature => feature.HasValue)
                .Select(feature => feature!.Value)
                .ToHashSet();

            ApplyEntitlements(
                new EntitlementSnapshot(
                    me.UserId,
                    refreshed.Role,
                    refreshed.Plan,
                    me.EmailVerified,
                    me.BillingIsLive,
                    granted,
                    me.Limits,
                    me.IssuedUtc,
                    me.ExpiresUtc),
                me.Signed);

            LastAllowance = me.Allowance;
            return true;
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Withdrawn rather than timed out. Handled by the caller.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException
                                              or System.Text.Json.JsonException)
        {
            // Unreachable, slow, or answering with something this build cannot
            // read. All three mean the same thing to the caller: carry on with
            // what is already known.
            _log.Info($"Entitlements could not be refreshed right now: {exception.Message}");

            // Only the timeouts and connection failures are worth repeating.
            // Something this build cannot parse will not parse on a second go.
            return exception is System.Text.Json.JsonException;
        }
    }

    /// <summary>
    /// How much of this month's included AI has been used, as last reported.
    /// Null when nobody is signed in or the gateway has not been asked yet.
    /// </summary>
    public AssistAllowance? LastAllowance { get; private set; }

    /// <summary>
    /// Which managed model to ask the gateway for.
    ///
    /// Whatever the user picked, when the plan's list includes it. Otherwise the
    /// first model the plan does allow, which is authored cheapest-first. The
    /// gateway makes the same substitution and its answer is the one that counts;
    /// doing it here too means the model chip in the notch shows what will
    /// actually be used rather than what was asked for.
    ///
    /// An empty string when nothing is known yet, which the gateway reads as
    /// "choose for me".
    /// </summary>
    private string PreferredManagedModel()
    {
        var allowed = Entitlements?.Limits.ManagedModels;
        if (allowed is null || allowed.Count == 0)
        {
            return string.Empty;
        }

        var chosen = Settings.ReasoningModel?.Trim();
        return !string.IsNullOrEmpty(chosen) && allowed.Contains(chosen, StringComparer.OrdinalIgnoreCase)
            ? chosen
            : allowed[0];
    }

    private static string ProviderDisplayName(string provider) => provider switch
    {
        "OpenAI" => "OpenAI",
        "Claude" => "Claude",
        "OpenClaw" => "OpenClaw gateway",
        "OpenRouter" => "OpenRouter",
        "Ollama" => "local Ollama",
        "Metis" => "Metis's own AI",
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
        ProviderRouting.GatewayProviderId => new MetisGatewayReasoningProvider(
            new Uri(MetisBackend.ResolveGatewayUrl(Settings.MetisGatewayUrl), UriKind.Absolute)),
        _ => throw new InvalidOperationException($"{provider} is not a configurable endpoint provider.")
    };

    /// <summary>
    /// Carries the reply to the screen as it is written, and remembers whether
    /// any of it got there.
    ///
    /// That second job is what makes streaming safe to combine with Automatic
    /// mode's provider fallback. Falling back is invisible while nothing has
    /// been shown; once half a sentence is on screen, quietly starting a second
    /// provider would write a different answer on top of it. So a provider that
    /// fails after it has begun speaking is a failure of the turn, not a
    /// reason to try the next one.
    /// </summary>
    private sealed class TurnTextStream(Action onStarted, Action<string> onDelta) : IProgress<string>
    {
        private bool _started;

        public bool HasPublished { get; private set; }

        public void Report(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                onStarted();
            }

            HasPublished = true;
            onDelta(value);
        }
    }

    /// <summary>
    /// Whether a failed provider should be followed by the next one, given both
    /// what went wrong and how much of the answer the user can already see.
    /// </summary>
    private bool CanFallBackAfter(Exception exception, TurnTextStream? answerStream, string providerName)
    {
        if (answerStream is { HasPublished: true })
        {
            _log.Error(
                $"{providerName} failed after it had already begun answering, so Metis kept the partial reply " +
                "rather than starting a second one over the top of it.",
                exception);
            return false;
        }

        if (!IsWorthTryingAnotherProvider(exception))
        {
            _log.Error(
                $"{providerName} rejected the request itself, so the remaining providers were not tried — " +
                "they are sent the same request and would reject it the same way.",
                exception);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether trying a different provider could possibly do any better.
    ///
    /// Automatic mode walks Gemini, then OpenAI, then Claude, and each attempt
    /// costs a full request. That is worth it when a provider is down, out of
    /// quota, or does not have the model — a different one genuinely may
    /// answer. It is never worth it when the request itself was rejected,
    /// because every provider is being sent the same request and will reject it
    /// the same way. Walking the chain then just spends the turn's whole
    /// deadline arriving at the answer it already had.
    /// </summary>
    private static bool IsWorthTryingAnotherProvider(Exception exception) => exception switch
    {
        GeminiProviderException gemini => gemini.Kind != GeminiErrorKind.InvalidRequest,
        OpenAiProviderException openAi => openAi.Kind != OpenAiErrorKind.InvalidRequest,

        // Never fall back off the gateway after it has refused on grounds of
        // plan or allowance.
        //
        // This is the most consequential line in the fallback logic. The
        // "helpful" behaviour would be to try the user's own key instead, and
        // that would mean Metis quietly spending someone else's money the moment
        // it runs out of its own — on a request they believed was included,
        // without being asked. A refusal they can see and act on is the honest
        // outcome.
        ReasoningProviderException { Kind: ReasoningProviderErrorKind.PlanLimited } => false,
        ReasoningProviderException
        {
            ProviderId: MetisGatewayReasoningProvider.ProviderId,
            Kind: ReasoningProviderErrorKind.QuotaOrRateLimit
        } => false,

        ReasoningProviderException reasoning => reasoning.Kind != ReasoningProviderErrorKind.InvalidRequest,
        _ => true
    };

    private async Task<ProviderTurnResult> GenerateWithSelectedProviderAsync(
        GeminiRequest request,
        TurnTextStream? answerStream,
        CancellationToken cancellationToken)
    {
        // The route is decided before anything else, because the answer changes
        // who pays. A user with their own key falls straight through into the
        // switch below exactly as they always have; only someone with no key of
        // their own ever reaches the gateway.
        switch (CurrentRoute())
        {
            case ProviderRoute.MetisGateway:
                return await GenerateWithEndpointProviderAsync(
                    ProviderRouting.GatewayProviderId, request, answerStream, cancellationToken);

            case ProviderRoute.RefuseNeedsKeyOrPlan:
                throw new InvalidOperationException(
                    ProviderRouting.ExplainRefusal(Account.IsSignedIn, OwnKeyIsAllowed));

            case ProviderRoute.LocalOnly:
            case ProviderRoute.DirectByok:
            default:
                break;
        }

        if (Settings.AiProvider == "OpenAI")
        {
            return await GenerateWithOpenAiAsync(request, answerStream, cancellationToken);
        }

        if (Settings.AiProvider == "Gemini")
        {
            return await GenerateWithGeminiAsync(request, answerStream, cancellationToken);
        }

        if (Settings.AiProvider == "Claude")
        {
            return await GenerateWithClaudeAsync(request, answerStream, cancellationToken);
        }

        if (Settings.AiProvider is "OpenClaw" or "Ollama")
        {
            return await GenerateWithEndpointProviderAsync(
                Settings.AiProvider, request, answerStream, cancellationToken);
        }

        Exception? geminiError = null;
        Exception? openAiError = null;
        if (HasGeminiKey)
        {
            try
            {
                return await GenerateWithGeminiAsync(request, answerStream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (CanFallBackAfter(exception, answerStream, "Gemini"))
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
                return await GenerateWithOpenAiAsync(request, answerStream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (CanFallBackAfter(exception, answerStream, "OpenAI"))
            {
                openAiError = exception;
                _log.Error("OpenAI failed in Automatic mode; trying Claude.", exception);
            }
        }

        if (HasClaudeKey)
        {
            try
            {
                return await GenerateWithClaudeAsync(request, answerStream, cancellationToken);
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

        // The gateway is the last rung, and it is appended rather than inserted
        // for a reason worth stating plainly: put it anywhere earlier and every
        // user who has their own key stops using it, and Metis starts paying for
        // requests those users were already paying for themselves. It is the
        // fallback for someone who has run out of their own options, not the
        // default for someone who has options.
        if (Account.IsSignedIn
            && SessionAccessToken is not null
            && MetisBackend.HasGateway(Settings.MetisGatewayUrl))
        {
            try
            {
                return await GenerateWithEndpointProviderAsync(
                    ProviderRouting.GatewayProviderId, request, answerStream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception gatewayError)
            {
                // Only swallowed when an earlier provider already failed, so the
                // user is told about the one they configured rather than about
                // the fallback they never chose.
                if (geminiError is null && openAiError is null)
                {
                    throw;
                }

                _log.Error("Metis's own AI also failed in Automatic mode.", gatewayError);
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
            ProviderRouting.ExplainRefusal(Account.IsSignedIn, OwnKeyIsAllowed));
    }

    private async Task<ProviderTurnResult> GenerateWithGeminiAsync(
        GeminiRequest request,
        IProgress<string>? onTextDelta,
        CancellationToken cancellationToken)
    {
        var response = await _gemini.GenerateAsync(
            RequireApiKey(),
            Settings.ReasoningModel,
            request,
            onTextDelta,
            cancellationToken);
        return new ProviderTurnResult("Gemini", response.Model, response.Text, response.Plan, response.Usage);
    }

    private async Task<ProviderTurnResult> GenerateWithOpenAiAsync(
        GeminiRequest request,
        IProgress<string>? onTextDelta,
        CancellationToken cancellationToken)
    {
        var response = await _openAi.GenerateAsync(
            RequireOpenAiApiKey(),
            Settings.OpenAiReasoningModel,
            Settings.OpenAiTranscriptionModel,
            request,
            onTextDelta,
            cancellationToken);
        return new ProviderTurnResult("OpenAI", response.Model, response.Text, response.Plan);
    }

    private async Task<ProviderTurnResult> GenerateWithClaudeAsync(
        GeminiRequest request,
        IProgress<string>? onTextDelta,
        CancellationToken cancellationToken)
    {
        var response = await _claude.GenerateAsync(
            RequireClaudeApiKey(),
            Settings.ClaudeReasoningModel,
            request,
            onTextDelta,
            cancellationToken);
        return new ProviderTurnResult("Claude", response.Model, response.Text, response.Plan);
    }

    private async Task<ProviderTurnResult> GenerateWithEndpointProviderAsync(
        string providerName,
        GeminiRequest request,
        IProgress<string>? onTextDelta,
        CancellationToken cancellationToken)
    {
        var provider = CreateEndpointProvider(providerName);
        try
        {
            var credential = EndpointProviderCredential(providerName);
            var model = providerName switch
            {
                "OpenClaw" => Settings.OpenClawModel,

                // The gateway substitutes from the plan's own allow-list, so the
                // client sends what the user asked for and lets the server
                // decide, rather than guessing here at what the plan permits.
                ProviderRouting.GatewayProviderId => PreferredManagedModel(),
                _ => Settings.OllamaModel
            };

            // Only the gateway can be asleep. OpenClaw and Ollama are on the
            // user's own machine or network, so a slow answer from either is
            // the model thinking rather than a container being built, and
            // announcing a wake-up would be a lie about what is happening.
            var managed = providerName == ProviderRouting.GatewayProviderId;
            var progress = managed && onTextDelta is not null
                ? new FirstDeltaNotice(onTextDelta, NoteGatewayAnswered)
                : onTextDelta;

            var response = managed
                ? await WhileGatewayMayBeWakingAsync(
                    () => provider.GenerateAsync(credential, model, request, progress, cancellationToken))
                : await provider.GenerateAsync(credential, model, request, progress, cancellationToken);

            return new ProviderTurnResult(
                providerName, response.Model, response.Text, response.Plan, response.Usage);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Passes a streamed answer through untouched, and rings a bell the first
    /// time any of it arrives.
    ///
    /// A streamed turn's call does not return until the last word, so the
    /// "waking up" notice would otherwise stay over an answer the user is
    /// already reading. The first delta is the moment the gateway is provably
    /// awake, which is the moment to take it down.
    /// </summary>
    private sealed class FirstDeltaNotice(IProgress<string> inner, Action onFirstDelta) : IProgress<string>
    {
        private int _rung;

        public void Report(string value)
        {
            if (Interlocked.Exchange(ref _rung, 1) == 0)
            {
                onFirstDelta();
            }

            inner.Report(value);
        }
    }

    private Task<SpeechAudio?> SynthesizeWithProviderAsync(
        ProviderTurnResult response,
        AssistantPlan? plan,
        CancellationToken cancellationToken)
    {
        var textToSpeak = !string.IsNullOrWhiteSpace(plan?.SpokenText)
            ? plan.SpokenText
            : response.Text;
        return SynthesizeTextAsync(textToSpeak, response.Provider, cancellationToken);
    }

    /// <summary>
    /// Speaks any line through whichever voice the user chose, with automatic
    /// fallback to Windows offline voice if the primary cloud service fails.
    /// </summary>
    private async Task<SpeechAudio?> SynthesizeTextAsync(
        string text,
        string? answeringProvider,
        CancellationToken cancellationToken)
    {
        _lastVoiceError = null;
        var spokenText = CompanionSpeech.CleanForSpeech(text);
        if (string.IsNullOrWhiteSpace(spokenText))
        {
            return null;
        }

        var provider = answeringProvider ?? Settings.AiProvider;

        try
        {
            if (Settings.TextToSpeechProvider == "Piper")
            {
                return await _piper.SynthesizeSpeechAsync(
                    ResolveLocalPath(Settings.PiperExecutablePath),
                    ResolveLocalPath(Settings.PiperVoiceModelPath),
                    spokenText,
                    cancellationToken);
            }

            if (Settings.TextToSpeechProvider == "Chatterbox-Nano")
            {
                return await _chatterboxNano.SynthesizeSpeechAsync(
                    Settings.ChatterboxEndpoint,
                    Settings.ChatterboxModel,
                    Settings.ChatterboxVoice,
                    spokenText,
                    cancellationToken);
            }

            if (Settings.TextToSpeechProvider == "ElevenLabs")
            {
                return await _elevenLabs.SynthesizeSpeechAsync(
                    RequireElevenLabsApiKey(),
                    Settings.ElevenLabsModel,
                    Settings.ElevenLabsVoiceId,
                    spokenText,
                    cancellationToken);
            }

            if (Settings.TextToSpeechProvider == "Native")
            {
                if (HasGeminiKey)
                {
                    _log.Info("Using Gemini for Native TTS.");
                    return await _gemini.SynthesizeSpeechAsync(
                        RequireApiKey(),
                        string.IsNullOrWhiteSpace(Settings.SpeechModel)
                            ? ModelCatalog.DefaultGeminiSpeechModel
                            : Settings.SpeechModel,
                        Metis.AI.GeminiRequestBuilder.NormalizeVoice(Settings.VoiceName),
                        spokenText,
                        cancellationToken);
                }

                if (HasOpenAiKey)
                {
                    _log.Info("Using OpenAI for Native TTS.");
                    return await _openAi.SynthesizeSpeechAsync(
                        RequireOpenAiApiKey(),
                        Settings.OpenAiSpeechModel,
                        Settings.OpenAiVoiceName,
                        spokenText,
                        cancellationToken);
                }
            }

            if (provider == "OpenAI")
            {
                return await _openAi.SynthesizeSpeechAsync(
                    RequireOpenAiApiKey(),
                    Settings.OpenAiSpeechModel,
                    Settings.OpenAiVoiceName,
                    spokenText,
                    cancellationToken);
            }

            if (provider == "Gemini" || HasGeminiKey)
            {
                return await _gemini.SynthesizeSpeechAsync(
                    RequireApiKey(),
                    Settings.SpeechModel,
                    Settings.VoiceName,
                    spokenText,
                    cancellationToken);
            }

            if (HasOpenAiKey)
            {
                return await _openAi.SynthesizeSpeechAsync(
                    RequireOpenAiApiKey(),
                    Settings.OpenAiSpeechModel,
                    Settings.OpenAiVoiceName,
                    spokenText,
                    cancellationToken);
            }

            _lastVoiceError = "No API key configured for speech synthesis (Gemini or OpenAI)";
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception primaryEx)
        {
            _log.Error($"Text-to-speech failed ({Settings.TextToSpeechProvider} / {provider}): {primaryEx.Message}", primaryEx);
            _lastVoiceError = primaryEx.Message;
            SetStatus($"Voice synthesis error: {primaryEx.Message}");
            return null;
        }
    }

    private void OnAudioLevelChanged(object? sender, float level) => AudioLevelChanged?.Invoke(this, level);

    /// <summary>
    /// Tells the capture service how much detail to keep and what it must not
    /// look at. Called whenever settings change, so a newly excluded
    /// application takes effect on the next question rather than the next
    /// restart.
    /// </summary>
    private void ConfigureCaptureProfile()
    {
        if (_capture is not VirtualDesktopCaptureService virtualDesktopCapture)
        {
            return;
        }

        virtualDesktopCapture.UseCompactLocalProfile(Settings.AiProvider == "Ollama");
        virtualDesktopCapture.ExcludedApplications = Settings.ExcludedApplicationList;

        // Windows marks a protected window for us, but nothing marks a password
        // box. That answer only exists in the accessibility tree, so the capture
        // service is given a way to ask it.
        if (_uiAutomation is FlaUiAutomationService automation)
        {
            virtualDesktopCapture.ReadFocusedPasswordField = automation.FindFocusedPasswordFieldRegion;
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
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var trimmed = path.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
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
        if (audio == null || audio.PcmData.Length == 0)
        {
            return null;
        }

        try
        {
            if (audio.PcmData.Length >= 12 &&
                audio.PcmData[0] == 0x52 && audio.PcmData[1] == 0x49 &&
                audio.PcmData[2] == 0x46 && audio.PcmData[3] == 0x46)
            {
                using var ms = new MemoryStream(audio.PcmData, false);
                using var reader = new WaveFileReader(ms);
                return reader.TotalTime;
            }
        }
        catch
        {
        }

        var sampleRate = audio.SampleRate > 0 ? audio.SampleRate : 24000;
        var channels = audio.Channels > 0 ? audio.Channels : 1;
        var bitsPerSample = audio.BitsPerSample > 0 ? audio.BitsPerSample : 16;
        var bytesPerSecond = sampleRate * channels * (bitsPerSample / 8d);
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
    public void PlayCue(MetisSound sound)
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

        // No recording exists for these two yet, so they borrow the synthesised
        // cues rather than being silent. A plan change is a small affirmative
        // pop; running out of allowance is not an error and must not sound like
        // one, so it gets the neutral woosh instead of the error tone. Drop
        // "plan changed.mp3" or "limit reached.mp3" into the sound pack folder
        // and SoundPackNaming picks them up with no code change.
        MetisSound.PlanChanged => SoundCueFactory.Pop(),
        MetisSound.LimitReached => SoundCueFactory.Woosh(),
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

    /// <summary>
    /// Keeps the in-memory chat list current and writes the session to disk.
    ///
    /// The write is handed to the thread pool rather than done here. This is
    /// called twice per turn — once for the question and once for the answer —
    /// from a method whose continuations run on the UI thread, and it rewrites
    /// the whole session as one <c>File.WriteAllText</c>. The first of those
    /// two writes sat directly between the user's keystroke and the request
    /// going out; the second sat between the answer arriving and the bubble
    /// being drawn.
    ///
    /// The list update stays here, on the caller's thread, so the ordering
    /// callers depend on is unchanged; only the file write moves.
    /// </summary>
    private void SaveCurrentChat()
    {
        if (!Settings.ChatMemoryEnabled || _currentChat.IsEmpty)
        {
            return;
        }

        var session = _currentChat;
        _chatSessions.RemoveAll(existing => existing.Id == session.Id);
        _chatSessions.Insert(0, session);
        ChatsChanged?.Invoke(this, EventArgs.Empty);

        // Chained rather than simply queued, so the two saves in a turn land in
        // the order they were made. Two independent writes to the same file can
        // finish in either order, and the loser is the question's copy being
        // written over the answer's.
        lock (_chatSaveGate)
        {
            _chatSaveChain = _chatSaveChain.ContinueWith(
                _ =>
                {
                    try
                    {
                        _chatStore.Save(session);
                    }
                    catch (Exception exception)
                    {
                        _log.Error("The chat could not be saved in the background.", exception);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
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

        // A plan or allowance refusal is not a fault, and reporting it as one
        // sends people looking for something to fix. It gets its own banner and
        // its own cue, and the error surface never sees it.
        if (IsPlanRefusal(exception))
        {
            var isPlan = exception is ReasoningProviderException
            {
                Kind: ReasoningProviderErrorKind.PlanLimited
            };

            PlanLimitReached?.Invoke(this, new PlanLimitNotice(
                isPlan ? "Not included in your plan" : "This month's included AI is used up",
                detail));

            SetStatus(detail);
            MessageAdded?.Invoke(this, new AssistantMessage(AssistantRole.Error, detail, DateTimeOffset.Now));
            State.Force(AssistantState.QuotaError);
            PlayCue(MetisSound.LimitReached);
            _log.Info($"{context}: refused by plan or allowance. {detail}");
            return;
        }

        ReportError($"{context}. {detail}", ClassifyErrorState(exception), spokenMessage: detail);
        _log.Error(context, exception);
    }

    public event EventHandler<PlanLimitNotice>? PlanLimitReached;

    /// <summary>
    /// Displays a plan limit / upgrade notice to the user in the shell.
    /// </summary>
    public void ShowPlanNotice(string title, string message) =>
        PlanLimitReached?.Invoke(this, new PlanLimitNotice(title, message));

    /// <summary>
    /// Raised when a question could not be answered because Metis has not been
    /// set up yet. Carries the sentence to show; the surface decides what to
    /// offer alongside it.
    /// </summary>
    public event EventHandler<string>? SetupRequired;

    /// <summary>
    /// Whether the gateway turned this turn down over the plan or the month's
    /// allowance, as opposed to anything actually going wrong.
    ///
    /// Only the gateway's own refusals count. A quota error from a provider on
    /// the user's own key means their key is out of quota, which is their
    /// account and their business, and telling them to look at a Metis plan
    /// would be answering a question they did not ask.
    /// </summary>
    private static bool IsPlanRefusal(Exception exception) => exception switch
    {
        ReasoningProviderException { Kind: ReasoningProviderErrorKind.PlanLimited } => true,
        ReasoningProviderException
        {
            ProviderId: MetisGatewayReasoningProvider.ProviderId,
            Kind: ReasoningProviderErrorKind.QuotaOrRateLimit
        } => true,
        _ => false
    };

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

        // A plan that does not include something is shown the same way as a
        // spent allowance rather than as an authentication problem. Both mean
        // "not right now, and here is what would change that". An authentication
        // error means "your credential is wrong", which would send someone off
        // to replace a key that is working perfectly.
        ReasoningProviderException { Kind: ReasoningProviderErrorKind.PlanLimited } => AssistantState.QuotaError,
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

        // Stopped and unhooked rather than merely dropped. A DispatcherTimer is
        // rooted by the dispatcher it was created on, so one that is only
        // forgotten keeps ticking — and keeps this whole runtime alive — for as
        // long as the application lives.
        if (_entitlementTimer is not null)
        {
            _entitlementTimer.Stop();
            _entitlementTimer.Tick -= OnEntitlementRefreshTick;
            _entitlementTimer = null;
        }

        _turnCancellation?.Cancel();
        _turnCancellation?.Dispose();
        _pushToTalk.Pressed -= OnPushToTalkPressed;
        _pushToTalk.Released -= OnPushToTalkReleased;
        _pushToTalk.DirectAgentVoicePressed -= OnDirectAgentVoicePressed;
        _pushToTalk.DirectAgentVoiceReleased -= OnDirectAgentVoiceReleased;
        _pushToTalk.EmergencyStopPressed -= OnEmergencyStopPressed;
        _pushToTalk.ActiveListeningToggled -= OnActiveListeningToggled;
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
        AgentTasks.Dispose();
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
    AssistantPlan? Plan = null,
    ModelUsageReport? Usage = null);

