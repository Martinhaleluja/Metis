using System.Threading;
using System.Windows;
using Lulu.App.Runtime;
using Lulu.App.Windows;
using Lulu.App.Branding;
using Forms = System.Windows.Forms;

namespace Lulu.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private LuluRuntime? _runtime;
    private CompanionWindow? _companionWindow;
    private SetupWindow? _setupWindow;
    private AssistantWindow? _assistantWindow;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "Local\\Lulu.Desktop.Companion", out var isFirstInstance);
        _ownsSingleInstance = isFirstInstance;
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("Lulu is already running in the notification area.", "Lulu");
            Quit();
            return;
        }

        try
        {
            _runtime = new LuluRuntime();
            await _runtime.InitializeAsync();

            _companionWindow = new CompanionWindow(_runtime);
            _setupWindow = new SetupWindow(_runtime, ShowAssistant);
            _assistantWindow = new AssistantWindow(_runtime, ShowSetup);

            CreateTrayIcon();
            _companionWindow.Show();

            if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            if (_runtime.HasAnyApiKey)
            {
                ShowAssistant();
            }
            else
            {
                ShowSetup();
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Lulu could not start. {exception.Message}",
                "Lulu startup error",
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
            Renderer = new Forms.ToolStripProfessionalRenderer(new LuluTrayColorTable())
        };

        var openItem = new Forms.ToolStripMenuItem("Open Lulu", null, (_, _) => Dispatcher.Invoke(ShowAssistant))
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
        menu.Items.Add(setupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);

        _trayDrawingIcon = LuluIconFactory.CreateTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Lulu desktop companion",
            Icon = _trayDrawingIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowAssistant);
    }

    private sealed class LuluTrayColorTable : Forms.ProfessionalColorTable
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

    private void ShowSetup()
    {
        if (_setupWindow is null)
        {
            return;
        }

        _assistantWindow?.Hide();
        _setupWindow.RefreshFromRuntime();
        _setupWindow.Show();
        _setupWindow.Activate();
    }

    private void ShowAssistant()
    {
        if (_assistantWindow is null)
        {
            return;
        }

        _setupWindow?.Hide();
        _assistantWindow.Show();
        _assistantWindow.Activate();
    }

    private void Quit()
    {
        _setupWindow?.AllowClose();
        _assistantWindow?.AllowClose();
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
