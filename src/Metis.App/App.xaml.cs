using System.IO;
using System.Threading;
using System.Windows;
using Metis.App.Runtime;
using Metis.App.Windows;
using Metis.App.Branding;
using Metis.App.Theme;
using Metis.Core.Models;
using Metis.Core.Services;
using Forms = System.Windows.Forms;

namespace Metis.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private MetisRuntime? _runtime;
    private CompanionWindow? _companionWindow;
    private PreferencesWindow? _preferencesWindow;
    private OnboardingWindow? _onboardingWindow;
    private GuidanceOverlayWindow? _overlayWindow;
    private TraceOverlayWindow? _traceWindow;
    private NotchWindow? _notchWindow;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;
    private ThemeService? _themeService;
    private TopmostGuard? _topmostGuard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch exceptions that escape from background Task threads (including fire-and-forget tasks
        // that are not awaited). Without this the process silently terminates on any unhandled exception
        // in a Task continuation or a fire-and-forget async method.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var ex = args.ExceptionObject as Exception;
                var report = TryWriteStartupReport(ex ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown error"));
                if (_runtime is not null)
                {
                    _runtime.Log.Error("Unhandled AppDomain exception", ex ?? new Exception(args.ExceptionObject?.ToString()));
                }
            }
            catch
            {
                // Never throw from an unhandled exception handler
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                args.SetObserved();
                _runtime?.Log.Error("Unobserved Task exception caught and suppressed", args.Exception);
            }
            catch
            {
                // Never throw from an unhandled exception handler
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                _runtime?.Log.Error("Unhandled WPF Dispatcher exception caught", args.Exception);
                args.Handled = true;
            }
            catch
            {
                // Never throw from an unhandled exception handler
            }
        };

        _singleInstance = new Mutex(true, "Local\\Metis.Desktop.Companion", out var isFirstInstance);
        _ownsSingleInstance = isFirstInstance;
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("Metis is already running in the notification area.", "Metis");
            Quit();
            return;
        }

        try
        {
            _runtime = new MetisRuntime();
            await _runtime.InitializeAsync();

            // Before any window is constructed, so each one loads already
            // wearing the right theme instead of flashing light and correcting.
            _themeService = new ThemeService(this);
            _themeService.Apply(_runtime.Settings.ThemePreference);
            _themeService.Changed += (_, _) => RepaintTrayMenu();

            _companionWindow = new CompanionWindow(_runtime);
            _preferencesWindow = new PreferencesWindow(_runtime, _themeService, ShowChat);
            _onboardingWindow = new OnboardingWindow(_runtime, _themeService, ShowChat);
            _overlayWindow = new GuidanceOverlayWindow();
            _notchWindow = new NotchWindow { DebugLog = message => _runtime.Log.Info(message) };
            _traceWindow = new TraceOverlayWindow();

            // The chat is part of the notch rather than a window of its own, so
            // it is wired here and never shown or hidden as a separate thing.
            _notchWindow.Chat.Attach(_runtime);
            _notchWindow.ConnectChat();
            _notchWindow.ChatSetupRequested += (_, _) => ShowSetup();
            _notchWindow.AgentDrawer.Attach(_runtime);
            _notchWindow.ConnectAgentDrawer(_runtime);
            _notchWindow.ConnectSpawnAgent(_runtime);

            // Clicking a Windows notification brings the agent it is about up
            // on screen, rather than merely putting Metis in the foreground and
            // leaving the user to find what it was telling them about.
            _runtime.AgentNotificationOpened += (_, _) => Dispatcher.Invoke(() =>
            {
                _notchWindow?.OpenAgentDrawer();
                _notchWindow?.AgentDrawer.RefreshTasks();
            });

            // The pen comes out with the chord, and the notch becomes the tool
            // picker. A quick hold-and-drag still commits on release; touching
            // any tool makes the surface sticky so it survives the keys coming
            // up and is finished from the notch instead.
            _runtime.TraceArmRequested += (_, _) => Dispatcher.Invoke(() =>
            {
                _traceWindow.Arm();
                _notchWindow.ShowTraceTools(_traceWindow.Tool);

                // Order matters: the surface is shown first, then the notch is
                // lifted, then the surface is pinned directly beneath it. The
                // last step is what makes it stick — lifting alone loses to a
                // surface that finishes showing afterwards.
                _notchWindow.LiftAboveOverlays();
                _traceWindow.PlaceBelow(_notchWindow.Handle);

                _runtime.SetTraceCancelKeyEnabled(true);
                VerifyToolbarAppeared();
            });

            _runtime.TraceCancelKeyPressed += (_, _) => Dispatcher.Invoke(() => _traceWindow.Disarm());

            _runtime.TraceCommitRequested += (_, _) => Dispatcher.Invoke(() =>
            {
                _traceWindow.Commit();
                if (!_traceWindow.IsSticky)
                {
                    _notchWindow.HideTraceTools();
                }
            });

            // One subscription covers every ending — Escape, a stray click, a
            // finished mark, cancel — so the notch can never be left showing
            // the toolbar, or worse, silently muted because it still believes
            // a trace is in progress.
            _traceWindow.Disarmed += (_, _) => Dispatcher.Invoke(() =>
            {
                _notchWindow.HideTraceTools();
                _runtime.SetTraceCancelKeyEnabled(false);
            });

            _traceWindow.TraceCompleted += (_, path) => Dispatcher.Invoke(() =>
            {
                _runtime.Log.Info($"Trace committed with the {_traceWindow.LastCommittedTool} tool.");
                _runtime.SetTracedRegion(path);
            });

            _traceWindow.TapCompleted += (_, point) => Dispatcher.Invoke(() =>
            {
                _runtime.Log.Info($"Tap committed on trace overlay at {point.ScreenX},{point.ScreenY}.");
                _runtime.SetTappedPoint(point);
            });

            _notchWindow.TraceToolPicked += (_, tool) => Dispatcher.Invoke(() =>
            {
                _traceWindow.Arm();
                _traceWindow.SetTool(tool);
            });

            _notchWindow.TraceConfirmed += (_, _) => Dispatcher.Invoke(() => _traceWindow.CommitNow());
            _notchWindow.TraceCancelled += (_, _) => Dispatcher.Invoke(() => _traceWindow.Disarm());
            _runtime.GuidanceOverlayRequested += Runtime_OnGuidanceOverlayRequested;
            _runtime.ActivityChanged += Runtime_OnActivityChanged;
            _notchWindow.OpenRequested += (_, _) => ShowChat();
            _notchWindow.SettingsRequested += (_, _) => ShowSetup();

            // Toggling from the notch keeps the one consequential setting a
            // single click away rather than three levels into a menu.

            CreateTrayIcon();
            _overlayWindow.Show();
            _overlayWindow.Clear();
            _notchWindow.Show();
            _notchWindow.Tuck();

            // The companion is deliberately not shown here. It is the thing that
            // makes Metis look like it is running, and someone who has not signed
            // in yet has nothing for it to do — a face following the cursor around
            // while the only thing on screen asks for a password reads as an
            // application that has already started without permission. It arrives
            // in RevealMetis, once the first run is done.

            // Declared bottom-up: annotations are drawn over the user's screen,
            // the companion stands on top of them, and the notch is above
            // everything because it is the one control surface. Without this the
            // whole stack quietly slips behind the taskbar and behind any window
            // that raises itself when it is activated.
            _topmostGuard = new TopmostGuard();
            _topmostGuard.Add(_overlayWindow);
            _topmostGuard.Add(_companionWindow);
            _topmostGuard.Add(_notchWindow);
            _topmostGuard.Start();
            _notchWindow.KeepOnTop = () => _topmostGuard?.Reassert();

            if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            // --setup and --onboarding open their window regardless of
            // configuration, which is how support and UI work reach them
            // without first having to remove a working API key. With no stored
            // first-run flag, --onboarding is also the only practical way to
            // rehearse the wizard.
            _notchWindow.ConnectAuth();
            _notchWindow.Auth.Attach(_runtime, new CredentialStoreSessionAccess());
            _notchWindow.Auth.SignedIn += (_, _) => AfterSignIn();
            _notchWindow.Auth.Finished += (_, _) => FinishFirstRun();

            // Everything below this line assumes there is someone to show Metis
            // to. StartFirstRunAsync decides whether that is true, and opens the
            // rest of the startup sequence itself once it is.
            if (await HoldForFirstRunAsync(e.Args))
            {
                return;
            }

            ContinueStartup(e.Args);
        }
        catch (Exception exception)
        {
            // A startup failure happens before the diagnostic log is usable, so
            // it used to leave no trace at all beyond a one-line message box.
            // Writing the full exception somewhere findable is the difference
            // between a reportable bug and "it just doesn't open".
            var report = TryWriteStartupReport(exception);

            System.Windows.MessageBox.Show(
                $"Metis could not start. {exception.Message}"
                + (report is null ? string.Empty : $"\n\nDetails were written to:\n{report}"),
                "Metis startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Quit();
        }
    }

    private static string? TryWriteStartupReport(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Metis",
                "logs");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "startup-error.log");
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
            return path;
        }
        catch (Exception)
        {
            // Reporting a crash must never cause a second one.
            return null;
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = _themeService?.GetDrawingColor("Panel", System.Drawing.Color.FromArgb(9, 13, 18))
                        ?? System.Drawing.Color.FromArgb(9, 13, 18),
            ForeColor = _themeService?.GetDrawingColor("Text", System.Drawing.Color.FromArgb(224, 226, 234))
                        ?? System.Drawing.Color.FromArgb(224, 226, 234),
            Font = new System.Drawing.Font("Segoe UI Variable Text", 10F),
            ShowImageMargin = true,
            DropShadowEnabled = false,
            Padding = new Forms.Padding(3),
            Renderer = new Forms.ToolStripProfessionalRenderer(new MetisTrayColorTable(_themeService))
        };

        var openItem = new Forms.ToolStripMenuItem("Open Metis", null, (_, _) => Dispatcher.Invoke(ShowChat))
        {
            ForeColor = _themeService?.GetDrawingColor("Accent", System.Drawing.Color.FromArgb(120, 216, 255))
                        ?? System.Drawing.Color.FromArgb(120, 216, 255),
            Padding = new Forms.Padding(8, 6, 18, 6)
        };
        var setupItem = new Forms.ToolStripMenuItem("Setup", null, (_, _) => Dispatcher.Invoke(ShowSetup))
        {
            Padding = new Forms.Padding(8, 6, 18, 6)
        };
        var accountItem = new Forms.ToolStripMenuItem("Account", null, (_, _) => Dispatcher.Invoke(ShowAccount))
        {
            Padding = new Forms.Padding(8, 6, 18, 6)
        };
        var quitItem = new Forms.ToolStripMenuItem("Quit", null, (_, _) => Dispatcher.Invoke(Quit))
        {
            Padding = new Forms.Padding(8, 6, 18, 6)
        };

        menu.Items.Add(openItem);
        menu.Items.Add(setupItem);
        menu.Items.Add(accountItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);

        _trayDrawingIcon = MetisIconFactory.CreateTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Metis desktop companion",
            Icon = _trayDrawingIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowChat);
    }

    /// <summary>
    /// Exposes the four operating modes from the tray so the user can change
    /// how much Metis teaches versus does without opening Setup.
    /// </summary>
    private AccountWindow? _accountWindow;

    /// <summary>
    /// Opens the account window, creating it once and reusing it. Signing in is
    /// optional throughout: Metis works with no account at all, so this is a
    /// menu entry rather than anything the user is made to pass through.
    /// </summary>
    private void ShowAccount()
    {
        if (_runtime is null)
        {
            return;
        }

        if (_accountWindow is null)
        {
            _accountWindow = new AccountWindow(_runtime, new CredentialStoreSessionAccess());
            _accountWindow.Closed += (_, _) => _accountWindow = null;
        }

        _accountWindow.Show();
        _accountWindow.Activate();
    }

    // ============================= First run =============================

    /// <summary>The startup arguments, kept so the branch below the gate can run once it lifts.</summary>
    private string[] _pendingArgs = [];

    /// <summary>
    /// Decides whether this launch may proceed, and holds it at the sign-in
    /// panel if not.
    /// </summary>
    /// <returns>True when startup should stop here and wait for the user.</returns>
    private async Task<bool> HoldForFirstRunAsync(string[] args)
    {
        _pendingArgs = args;

        var settings = _runtime!.Settings;
        var configured = MetisBackend.IsConfigured(settings.SupabaseUrl, settings.SupabaseAnonKey);
        var url = MetisBackend.ResolveUrl(settings.SupabaseUrl);
        var key = MetisBackend.ResolveKey(settings.SupabaseAnonKey);

        var secrets = new CredentialStoreSessionAccess();
        var token = secrets.ReadSupabaseRefreshToken();
        var hasStoredSession = !string.IsNullOrWhiteSpace(token);

        var refreshSucceeded = false;
        var reachable = true;

        if (configured && hasStoredSession)
        {
            // A short timeout on purpose. This runs before the user can see
            // anything, so a backend that is slow to answer must not be able to
            // hold the whole application shut while it decides.
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            var auth = new Metis.Data.SupabaseAuthClient(http);

            Metis.Data.AuthResult result;
            try
            {
                result = await auth.RefreshAsync(url, key, token!);
            }
            catch (Exception exception)
            {
                // Anything thrown on the way out is the network, not an answer.
                _runtime.Log.Error("The stored session could not be renewed.", exception);
                result = Metis.Data.AuthResult.Unreachable(exception.Message);
            }

            reachable = result.Reachable;
            refreshSucceeded = result.Success && result.AccessToken is not null && result.Account is not null;

            if (refreshSucceeded)
            {
                if (result.RefreshToken is not null)
                {
                    secrets.WriteSupabaseRefreshToken(result.RefreshToken);
                }

                var account = await auth.LoadAccountAsync(
                    url, key, result.AccessToken!, result.Account!.UserId,
                    Entitlements.ParseEnvironment(settings.MetisEnvironment));

                _runtime.SignIn(account ?? result.Account);
                await _runtime.SaveSettingsAsync(
                    _runtime.Settings with { LastAuthenticatedUtc = DateTimeOffset.UtcNow }, null, null);
            }
            else if (reachable)
            {
                // The server answered and would not renew it. Keeping a token
                // the backend has already rejected only makes the next launch
                // wait for the same refusal.
                secrets.DeleteSupabaseRefreshToken();
            }
        }

        var decision = StartupAuthGate.Decide(
            configured,
            hasStoredSession,
            refreshSucceeded,
            reachable,
            _runtime.Settings.LastAuthenticatedUtc,
            DateTimeOffset.UtcNow);

        _runtime.Log.Info(
            $"Startup auth: {decision} (backend {(configured ? "configured" : "absent")}, "
            + $"stored session {(hasStoredSession ? "found" : "none")}, "
            + $"refresh {(refreshSucceeded ? "renewed" : "failed")}, "
            + $"backend {(reachable ? "reachable" : "unreachable")}).");

        if (decision == StartupAuthDecision.Allow)
        {
            RevealMetis();
            return false;
        }

        _notchWindow!.OpenAuth();
        return true;
    }

    /// <summary>
    /// Signed in. Setup comes next, so the user can add the API key Metis will
    /// actually answer with, and the welcome page waits until they are done
    /// with it.
    /// </summary>
    private void AfterSignIn()
    {
        _notchWindow?.CloseAuth();

        if (_preferencesWindow is null)
        {
            ShowWelcomePanel();
            return;
        }

        // Setup hides rather than closes — it is created once and lives for the
        // whole session — so "the user has finished with it" is a visibility
        // change and not a Closed event. The handler removes itself, because
        // this should happen on the first run and never again.
        void OnSetupDismissed(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (_preferencesWindow is null || _preferencesWindow.IsVisible)
            {
                return;
            }

            _preferencesWindow.IsVisibleChanged -= OnSetupDismissed;
            ShowWelcomePanel();
        }

        _preferencesWindow.IsVisibleChanged += OnSetupDismissed;
        ShowSetup();
    }

    /// <summary>
    /// Brings the notch back with the about-and-permissions page, and lets the
    /// companion appear alongside it.
    /// </summary>
    private void ShowWelcomePanel()
    {
        RevealMetis();

        if (_notchWindow is null)
        {
            return;
        }

        _notchWindow.Auth.ShowWelcomeOnly();
        _notchWindow.OpenAuth();
    }

    /// <summary>Done being asked things. The rest of startup runs now.</summary>
    private void FinishFirstRun()
    {
        _notchWindow?.CloseAuth();
        RevealMetis();
        ContinueStartup(_pendingArgs);
    }

    /// <summary>
    /// Lets the companion appear. Held back until the first run is over, so
    /// Metis does not stand next to the cursor before anyone has agreed to it
    /// being there. Safe to call more than once.
    /// </summary>
    private void RevealMetis()
    {
        if (_companionWindow is null || _companionWindow.IsVisible)
        {
            return;
        }

        _companionWindow.Show();
        _topmostGuard?.Reassert();
    }

    /// <summary>
    /// The branch that used to sit directly in OnStartup, now reachable either
    /// immediately or after the first run finishes.
    /// </summary>
    private void ContinueStartup(string[] args)
    {
        ShowWhatsNewIfUpdated();

        if (args.Contains("--onboarding", StringComparer.OrdinalIgnoreCase))
        {
            ShowOnboarding();
        }
        else if (args.Contains("--setup", StringComparer.OrdinalIgnoreCase))
        {
            ShowSetup();
        }
        else if (OnboardingVersions.ShouldShow(
                     _runtime!.Settings.OnboardingCompleted,
                     _runtime.Settings.OnboardingVersion))
        {
            // Deliberately not HasAnyApiKey, which only knows about the three
            // cloud providers: a fully local user has no cloud key, so that test
            // sent them back to Setup on every launch forever.
            //
            // The version check is what brings existing users back through it
            // once, because onboarding explains things that have since changed.
            ShowOnboarding();
        }
        else
        {
            ShowChat();
        }

        StartBackgroundUpdateCheck();
    }

    private void StartBackgroundUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait 4 seconds after startup so UI is fully settled
                await Task.Delay(TimeSpan.FromSeconds(4));
                if (_runtime is null || _notchWindow is null)
                {
                    return;
                }

                var updater = new UpdateService(_runtime.Log);
                var check = await updater.CheckAsync();
                if (check.UpdateAvailable)
                {
                    _runtime.Log.Info($"Background update check: version {check.Version} available.");
                    Dispatcher.Invoke(() => _notchWindow.Chat.ShowUpdate(check));
                }
            }
            catch (Exception exception)
            {
                _runtime?.Log.Error("Background update check encountered an error.", exception);
            }
        });
    }

    /// <summary>
    /// Shows the release notes once, after an update.
    ///
    /// Metis updates itself in the background for testers, so a build with new
    /// behaviour otherwise just appears one morning with no explanation. This
    /// release in particular adds agents that write files and drive a browser,
    /// which is not something anyone should meet by surprise.
    ///
    /// The recorded version is written before the window opens rather than
    /// after it is dismissed: if the notes throw, or the user closes them from
    /// the taskbar, the alternative is showing them again on every single
    /// launch forever, which is worse than missing them once.
    /// </summary>
    private void ShowWhatsNewIfUpdated()
    {
        if (_runtime is null || !WhatsNewWindow.ShouldShow(_runtime.Settings.LastSeenVersion))
        {
            RememberThisVersion();
            return;
        }

        RememberThisVersion();

        try
        {
            var window = new WhatsNewWindow { Owner = null };
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            _runtime.Log.Error("The release notes could not be shown.", exception);
        }
    }

    private void RememberThisVersion()
    {
        if (_runtime is null ||
            string.Equals(_runtime.Settings.LastSeenVersion, WhatsNewWindow.Version, StringComparison.Ordinal))
        {
            return;
        }

        _ = _runtime.SaveSettingsAsync(
            _runtime.Settings with { LastSeenVersion = WhatsNewWindow.Version }, null, null);
    }

    /// <summary>
    /// Hands the sign-in window only the session token, not the whole
    /// credential store. A dialog that collects a password has no business
    /// being able to read every provider key on the machine.
    /// </summary>
    private sealed class CredentialStoreSessionAccess : AccountWindow.ISecretStoreAccess
    {
        private readonly Metis.Data.WindowsCredentialStore _store = new();

        public string? ReadSupabaseRefreshToken() => _store.ReadSupabaseRefreshToken();

        public void WriteSupabaseRefreshToken(string token) => _store.WriteSupabaseRefreshToken(token);

        public void DeleteSupabaseRefreshToken() => _store.DeleteSupabaseRefreshToken();
    }

    /// <summary>
    /// Confirms the trace toolbar really arrived, and puts it up again once if
    /// it did not. A toolbar that fails silently leaves the user holding a pen
    /// with nowhere to click, which is worse than a visible error — so this
    /// both self-heals and records what went wrong.
    /// </summary>
    private void VerifyToolbarAppeared()
    {
        var settled = new System.Windows.Threading.DispatcherTimer
        {
            // Long enough for the widen, drop and stagger to finish; measuring
            // any earlier just reports the values they start from.
            Interval = TimeSpan.FromMilliseconds(700)
        };

        var retried = false;
        settled.Tick += (_, _) =>
        {
            if (_notchWindow is null || _traceWindow is null || !_traceWindow.IsArmed)
            {
                settled.Stop();
                return;
            }

            if (_notchWindow.VerifyToolbarVisible(out var report))
            {
                settled.Stop();
                _runtime?.Log.Info($"Trace toolbar ready: {_notchWindow.DescribeToolRects()}");
                return;
            }

            if (retried)
            {
                settled.Stop();
                _runtime?.Log.Error($"Trace toolbar did not appear after a retry: {report}");
                return;
            }

            retried = true;
            _runtime?.Log.Error($"Trace toolbar was not usable; restoring it: {report}");
            _notchWindow.ShowTraceTools(_traceWindow.Tool);
            _notchWindow.LiftAboveOverlays();
            _traceWindow.PlaceBelow(_notchWindow.Handle);
        };

        settled.Start();
    }

    private void Runtime_OnGuidanceOverlayRequested(object? sender, GuidanceOverlayRequest request) =>
        Dispatcher.InvokeAsync(() => _overlayWindow?.Show(request));

    private void Runtime_OnActivityChanged(object? sender, MetisActivity activity) =>
        Dispatcher.InvokeAsync(() =>
        {
            // The notch stays quiet while the toolbar is up, so a trace flag
            // left set by mistake would mute it for the rest of the session.
            // The armed surface is the authority, so reconcile against it here
            // rather than trusting the notch's own copy of that state.
            if (_notchWindow is { IsTracing: true } && _traceWindow is { IsArmed: false })
            {
                _notchWindow.HideTraceTools();
            }

            _notchWindow?.Show(activity);
        });

    /// <summary>
    /// Repaints the tray menu after a theme change. The colour table reads the
    /// live palette on every paint, so the menu only needs its own two colours
    /// refreshed and an invalidate; it does not have to be rebuilt.
    /// </summary>
    private void RepaintTrayMenu()
    {
        if (_trayIcon?.ContextMenuStrip is not { } menu || _themeService is null)
        {
            return;
        }

        menu.BackColor = _themeService.GetDrawingColor("Panel", System.Drawing.Color.FromArgb(9, 13, 18));
        menu.ForeColor = _themeService.GetDrawingColor("Text", System.Drawing.Color.FromArgb(224, 226, 234));
        menu.Invalidate();
    }

    /// <summary>
    /// The tray menu is Windows Forms and cannot consume a WPF brush, so it
    /// used to carry a third hardcoded palette that matched neither the windows
    /// nor the notch. Reading the tokens through ThemeService puts it on the
    /// same palette as everything else, and the getters resolve on each paint
    /// so it follows a theme change.
    /// </summary>
    private sealed class MetisTrayColorTable(ThemeService? theme) : Forms.ProfessionalColorTable
    {
        private System.Drawing.Color Token(string key, int r, int g, int b) =>
            theme?.GetDrawingColor(key, System.Drawing.Color.FromArgb(r, g, b))
            ?? System.Drawing.Color.FromArgb(r, g, b);

        public override System.Drawing.Color ToolStripDropDownBackground => Token("Panel", 9, 13, 18);
        public override System.Drawing.Color ImageMarginGradientBegin => Token("Panel", 9, 13, 18);
        public override System.Drawing.Color ImageMarginGradientMiddle => Token("Panel", 9, 13, 18);
        public override System.Drawing.Color ImageMarginGradientEnd => Token("Panel", 9, 13, 18);
        public override System.Drawing.Color MenuBorder => Token("Border", 53, 70, 84);
        public override System.Drawing.Color MenuItemBorder => Token("BorderStrong", 38, 54, 66);
        public override System.Drawing.Color MenuItemSelected => Token("Hover", 38, 42, 48);
        public override System.Drawing.Color SeparatorDark => Token("Border", 53, 70, 84);
        public override System.Drawing.Color SeparatorLight => Token("Border", 53, 70, 84);
    }

    /// <summary>
    /// Preferences is a free-standing, resizable window rather than an anchored
    /// one. It is a place to work through settings, not a glance at the notch,
    /// and the paged layout needs more room than the anchored slot allows.
    /// </summary>
    private void ShowSetup()
    {
        if (_preferencesWindow is null)
        {
            return;
        }

        _notchWindow?.CloseChat();
        _preferencesWindow.ShowAt();
    }

    /// <summary>
    /// The wizard is centred and free-standing rather than anchored under the
    /// notch: on first run the notch has not been explained yet, so it cannot
    /// serve as the landmark the anchored windows rely on.
    /// </summary>
    private void ShowOnboarding()
    {
        if (_onboardingWindow is null)
        {
            return;
        }

        _notchWindow?.CloseChat();
        _onboardingWindow.Show();
        _onboardingWindow.Activate();
    }

    /// <summary>
    /// Opens the chat, which is the notch itself unfolding rather than a window
    /// appearing. Asking for it again while it is open puts it away, so the same
    /// gesture, tray entry and shortcut all toggle the one surface.
    /// </summary>
    private void ShowChat()
    {
        if (_notchWindow is null)
        {
            return;
        }

        if (_notchWindow.IsChatOpen)
        {
            _notchWindow.CloseChat();
            return;
        }

        // Preferences is no longer anchored under the notch, so it does not
        // compete for the slot and only needs hiding.
        _preferencesWindow?.Hide();
        _notchWindow.OpenChat();
    }

    private void Quit()
    {
        if (_runtime is not null)
        {
            _runtime.GuidanceOverlayRequested -= Runtime_OnGuidanceOverlayRequested;
            _runtime.ActivityChanged -= Runtime_OnActivityChanged;
        }

        // SystemEvents keeps a process-lifetime handler list, so the theme
        // service has to be unhooked explicitly.
        _themeService?.Dispose();
        _topmostGuard?.Dispose();

        _preferencesWindow?.AllowClose();
        _onboardingWindow?.AllowClose();
        _overlayWindow?.AllowClose();
        _traceWindow?.AllowClose();
        _notchWindow?.AllowClose();
        _companionWindow?.AllowClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayDrawingIcon?.Dispose();
        _runtime?.Dispose();
        if (_ownsSingleInstance)
        {
            _singleInstance?.ReleaseMutex();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
