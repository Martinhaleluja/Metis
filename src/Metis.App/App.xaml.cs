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
    private AssistantWindow? _assistantWindow;
    private GuidanceOverlayWindow? _overlayWindow;
    private TraceOverlayWindow? _traceWindow;
    private NotchWindow? _notchWindow;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;
    private ThemeService? _themeService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            _preferencesWindow = new PreferencesWindow(_runtime, _themeService, ShowAssistant);
            _onboardingWindow = new OnboardingWindow(_runtime, _themeService, ShowAssistant);
            _assistantWindow = new AssistantWindow(_runtime, ShowSetup);
            _overlayWindow = new GuidanceOverlayWindow();
            _notchWindow = new NotchWindow { DebugLog = message => _runtime.Log.Info(message) };
            _traceWindow = new TraceOverlayWindow();

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

            _notchWindow.TraceToolPicked += (_, tool) => Dispatcher.Invoke(() =>
            {
                _traceWindow.Arm();
                _traceWindow.SetTool(tool);
            });

            _notchWindow.TraceConfirmed += (_, _) => Dispatcher.Invoke(() => _traceWindow.CommitNow());
            _notchWindow.TraceCancelled += (_, _) => Dispatcher.Invoke(() => _traceWindow.Disarm());
            _runtime.GuidanceOverlayRequested += Runtime_OnGuidanceOverlayRequested;
            _runtime.IntentChanged += Runtime_OnIntentChanged;
            _runtime.PermissionRequested += Runtime_OnPermissionRequested;
            _runtime.ActivityChanged += Runtime_OnActivityChanged;
            _notchWindow.OpenRequested += (_, _) => ShowAssistant();
            _notchWindow.SettingsRequested += (_, _) => ShowSetup();

            // Toggling from the notch keeps the one consequential setting a
            // single click away rather than three levels into a menu.
            _notchWindow.ModeCycleRequested += (_, _) => Dispatcher.Invoke(() => _ = ToggleModeAsync());
            _runtime.ModeChanged += (_, mode) => Dispatcher.Invoke(() => _notchWindow.ShowMode(mode));
            _notchWindow.ShowMode(_runtime.Mode);


            CreateTrayIcon();
            _overlayWindow.Show();
            _overlayWindow.Clear();
            _notchWindow.Show();
            _notchWindow.Tuck();
            _companionWindow.Show();

            if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            // --setup and --onboarding open their window regardless of
            // configuration, which is how support and UI work reach them
            // without first having to remove a working API key. With no stored
            // first-run flag, --onboarding is also the only practical way to
            // rehearse the wizard.
            if (e.Args.Contains("--onboarding", StringComparer.OrdinalIgnoreCase))
            {
                ShowOnboarding();
            }
            else if (e.Args.Contains("--setup", StringComparer.OrdinalIgnoreCase))
            {
                ShowSetup();
            }
            else if (!_runtime.Settings.OnboardingCompleted)
            {
                // Deliberately not HasAnyApiKey, which only knows about the
                // three cloud providers: a fully local user has no cloud key,
                // so that test sent them back to Setup on every launch forever.
                ShowOnboarding();
            }
            else
            {
                ShowAssistant();
            }
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

        var openItem = new Forms.ToolStripMenuItem("Open Metis", null, (_, _) => Dispatcher.Invoke(ShowAssistant))
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
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowAssistant);
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
    /// Reflects what Metis did on the last request, so the tray tooltip
    /// answers "is it about to touch my computer?" without the user having
    /// set anything. There is nothing to switch: the decision is made per
    /// request from the user's own words.
    /// </summary>
    private void Runtime_OnIntentChanged(object? sender, IntentDecision decision) => Dispatcher.Invoke(() =>
    {
        if (_trayIcon is not null && _runtime is not null)
        {
            _trayIcon.Text =
                $"Metis — {AssistanceModes.Name(_runtime.Mode)} · {IntentPolicy.For(decision.Intent).DisplayName}";
        }
    });

    /// <summary>
    /// Puts a high-risk step to the user and waits.
    ///
    /// The command is shown exactly as it will run, never summarised: someone
    /// approving "a system change" has not been told anything, and the whole
    /// value of the prompt is that they can read the thing and recognise it as
    /// wrong. No is the default button, so a dismissed dialog declines.
    /// </summary>
    private Task<bool> Runtime_OnPermissionRequested(PermissionRequest request) =>
        Dispatcher.InvokeAsync(() =>
        {
            var body = request.Detail;
            if (!string.IsNullOrWhiteSpace(request.Command))
            {
                body += $"\n\n{request.Command}";
            }

            if (request.NeedsElevation)
            {
                body += "\n\nWindows will ask for administrator rights as well.";
            }

            return System.Windows.MessageBox.Show(
                $"{body}\n\nRun it?",
                request.Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }).Task;

    /// <summary>
    /// Two modes make a toggle rather than a menu, and the state the user is
    /// switching from is already on the chip they clicked.
    /// </summary>
    private async Task ToggleModeAsync()
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            await _runtime.SetModeAsync(
                _runtime.Mode == AssistanceMode.Learn ? AssistanceMode.Autopilot : AssistanceMode.Learn);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Metis could not switch mode. {exception.Message}",
                "Metis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
        Dispatcher.Invoke(() => _overlayWindow?.Show(request));

    private void Runtime_OnActivityChanged(object? sender, MetisActivity activity) =>
        Dispatcher.Invoke(() =>
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
    /// Only one anchored window is open at a time: they share the same space
    /// under the notch, so showing one folds the other away first.
    /// </summary>
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

        _assistantWindow?.FoldIntoNotch();
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

        _assistantWindow?.FoldIntoNotch();
        _onboardingWindow.Show();
        _onboardingWindow.Activate();
    }

    private void ShowAssistant()
    {
        if (_assistantWindow is null || _notchWindow is null)
        {
            return;
        }

        if (_assistantWindow.IsVisible)
        {
            _assistantWindow.FoldIntoNotch();
            return;
        }

        // Preferences is no longer anchored under the notch, so it does not
        // compete for the slot and only needs hiding.
        _preferencesWindow?.Hide();
        _assistantWindow.OpenFromNotch(_notchWindow.AnchorBottom);
    }

    private void Quit()
    {
        if (_runtime is not null)
        {
            _runtime.GuidanceOverlayRequested -= Runtime_OnGuidanceOverlayRequested;
            _runtime.IntentChanged -= Runtime_OnIntentChanged;
            _runtime.ActivityChanged -= Runtime_OnActivityChanged;
        }

        // SystemEvents keeps a process-lifetime handler list, so the theme
        // service has to be unhooked explicitly.
        _themeService?.Dispose();

        _preferencesWindow?.AllowClose();
        _onboardingWindow?.AllowClose();
        _assistantWindow?.AllowClose();
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
