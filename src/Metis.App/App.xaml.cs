using System.Threading;
using System.Windows;
using Metis.App.Runtime;
using Metis.App.Windows;
using Metis.App.Branding;
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
    private SetupWindow? _setupWindow;
    private AssistantWindow? _assistantWindow;
    private GuidanceOverlayWindow? _overlayWindow;
    private NotchWindow? _notchWindow;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;
    private readonly Dictionary<OperatingMode, Forms.ToolStripMenuItem> _modeMenuItems = [];

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

            _companionWindow = new CompanionWindow(_runtime);
            _setupWindow = new SetupWindow(_runtime, ShowAssistant);
            _assistantWindow = new AssistantWindow(_runtime, ShowSetup);
            _overlayWindow = new GuidanceOverlayWindow();
            _notchWindow = new NotchWindow();
            _runtime.GuidanceOverlayRequested += Runtime_OnGuidanceOverlayRequested;
            _runtime.ModeChanged += Runtime_OnModeChanged;
            _runtime.ActivityChanged += Runtime_OnActivityChanged;
            _notchWindow.OpenRequested += (_, _) => ShowAssistant();
            _notchWindow.SettingsRequested += (_, _) => ShowSetup();

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

            // --setup opens straight into Setup regardless of configuration,
            // which is how support and UI work reach it without first having to
            // remove a working API key.
            if (e.Args.Contains("--setup", StringComparer.OrdinalIgnoreCase) || !_runtime.HasAnyApiKey)
            {
                ShowSetup();
            }
            else
            {
                ShowAssistant();
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Metis could not start. {exception.Message}",
                "Metis startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Quit();
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(9, 13, 18),
            ForeColor = System.Drawing.Color.FromArgb(224, 226, 234),
            Font = new System.Drawing.Font("Segoe UI Variable Text", 10F),
            ShowImageMargin = true,
            DropShadowEnabled = false,
            Padding = new Forms.Padding(3),
            Renderer = new Forms.ToolStripProfessionalRenderer(new MetisTrayColorTable())
        };

        var openItem = new Forms.ToolStripMenuItem("Open Metis", null, (_, _) => Dispatcher.Invoke(ShowAssistant))
        {
            ForeColor = System.Drawing.Color.FromArgb(120, 216, 255),
            Padding = new Forms.Padding(8, 6, 18, 6)
        };
        var setupItem = new Forms.ToolStripMenuItem("Setup", null, (_, _) => Dispatcher.Invoke(ShowSetup))
        {
            Padding = new Forms.Padding(8, 6, 18, 6)
        };
        var quitItem = new Forms.ToolStripMenuItem("Quit", null, (_, _) => Dispatcher.Invoke(Quit))
        {
            Padding = new Forms.Padding(8, 6, 18, 6)
        };

        menu.Items.Add(openItem);
        menu.Items.Add(CreateModeMenu());
        menu.Items.Add(setupItem);
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
    private Forms.ToolStripMenuItem CreateModeMenu()
    {
        var modeMenu = new Forms.ToolStripMenuItem("Mode")
        {
            Padding = new Forms.Padding(8, 6, 18, 6)
        };

        foreach (var capabilities in ModePolicy.All)
        {
            var mode = capabilities.Mode;
            var item = new Forms.ToolStripMenuItem(
                capabilities.DisplayName,
                null,
                (_, _) => Dispatcher.Invoke(() => _ = SwitchModeAsync(mode)))
            {
                ToolTipText = capabilities.Summary,
                Padding = new Forms.Padding(8, 4, 18, 4),
                Checked = _runtime is not null && _runtime.Mode == mode
            };

            _modeMenuItems[mode] = item;
            modeMenu.DropDownItems.Add(item);
        }

        return modeMenu;
    }

    private async Task SwitchModeAsync(OperatingMode mode)
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            await _runtime.SetModeAsync(mode);
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

    private void Runtime_OnModeChanged(object? sender, OperatingMode mode) => Dispatcher.Invoke(() =>
    {
        foreach (var (menuMode, item) in _modeMenuItems)
        {
            item.Checked = menuMode == mode;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Text = $"Metis — {ModePolicy.For(mode).DisplayName} mode";
        }
    });

    private void Runtime_OnGuidanceOverlayRequested(object? sender, GuidanceOverlayRequest request) =>
        Dispatcher.Invoke(() => _overlayWindow?.Show(request));

    private void Runtime_OnActivityChanged(object? sender, MetisActivity activity) =>
        Dispatcher.Invoke(() => _notchWindow?.Show(activity));

    private sealed class MetisTrayColorTable : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(9, 13, 18);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(9, 13, 18);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(9, 13, 18);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(9, 13, 18);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(53, 70, 84);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(38, 54, 66);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(38, 42, 48);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(53, 70, 84);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(53, 70, 84);
    }

    /// <summary>
    /// Only one anchored window is open at a time: they share the same space
    /// under the notch, so showing one folds the other away first.
    /// </summary>
    private void ShowSetup()
    {
        if (_setupWindow is null || _notchWindow is null)
        {
            return;
        }

        _assistantWindow?.FoldIntoNotch();
        _setupWindow.OpenFromNotch(_notchWindow.AnchorBottom);
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

        _setupWindow?.FoldIntoNotch();
        _assistantWindow.OpenFromNotch(_notchWindow.AnchorBottom);
    }

    private void Quit()
    {
        if (_runtime is not null)
        {
            _runtime.GuidanceOverlayRequested -= Runtime_OnGuidanceOverlayRequested;
            _runtime.ModeChanged -= Runtime_OnModeChanged;
            _runtime.ActivityChanged -= Runtime_OnActivityChanged;
        }

        _setupWindow?.AllowClose();
        _assistantWindow?.AllowClose();
        _overlayWindow?.AllowClose();
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
