using System.Windows;
using Microsoft.Win32;

// The project enables both WPF and Windows Forms, so Application and Color are
// ambiguous under implicit usings. Pin them to the WPF types and give the
// System.Drawing colour used by the tray menu its own name.
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using DrawingColor = System.Drawing.Color;
using SystemColors = System.Windows.SystemColors;

namespace Metis.App.Theme;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// Owns the one swappable entry in <see cref="Application.Resources"/> and
/// keeps it in step with the user's preference and with Windows.
///
/// Switching works because every brush in Controls.xaml is referenced through
/// DynamicResource: replacing the dictionary re-resolves them in place, so no
/// window has to be reloaded and open windows change theme while visible.
/// </summary>
public sealed class ThemeService : IDisposable
{
    /// <summary>
    /// The theme dictionary is pinned at index 0. App.xaml documents the same
    /// contract; if anything is ever merged ahead of it, swapping would replace
    /// the wrong dictionary and strip the app of its controls.
    /// </summary>
    private const int ThemeSlot = 0;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Application _application;
    private string _preference = "System";
    private ThemeMode? _applied;
    private bool _disposed;

    public ThemeService(Application application)
    {
        _application = application;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Raised after the effective theme actually changes.</summary>
    public event EventHandler? Changed;

    public ThemeMode Effective => _applied ?? ThemeMode.Light;

    /// <summary>
    /// Applies a stored <c>AppSettings.ThemePreference</c> value. Unknown text
    /// is treated as System, matching AppSettings.Normalize, so a hand-edited
    /// settings file cannot leave the app unthemed.
    /// </summary>
    public void Apply(string? themePreference)
    {
        _preference = string.IsNullOrWhiteSpace(themePreference) ? "System" : themePreference.Trim();
        Refresh();
    }

    private void Refresh()
    {
        var target = Resolve();
        if (_applied == target)
        {
            return;
        }

        var source = target == ThemeMode.Dark
            ? "pack://application:,,,/Metis;component/Theme/Tokens.Dark.xaml"
            : "pack://application:,,,/Metis;component/Theme/Tokens.Light.xaml";

        var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) };
        var merged = _application.Resources.MergedDictionaries;

        if (merged.Count > ThemeSlot)
        {
            merged[ThemeSlot] = dictionary;
        }
        else
        {
            merged.Insert(ThemeSlot, dictionary);
        }

        _applied = target;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ThemeMode Resolve()
    {
        if (string.Equals(_preference, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeMode.Light;
        }

        if (string.Equals(_preference, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeMode.Dark;
        }

        // High contrast is not yet a token set of its own. Picking the nearer
        // of the two real themes at least keeps contrast in the right
        // direction; a SystemColors-bound dictionary is the proper fix.
        if (SystemParameters.HighContrast)
        {
            return IsDark(SystemColors.WindowColor) ? ThemeMode.Dark : ThemeMode.Light;
        }

        return WindowsPrefersDark() ? ThemeMode.Dark : ThemeMode.Light;
    }

    private static bool IsDark(Color color) =>
        ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) < 128;

    private static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            // A locked-down or policy-managed profile can deny this read.
            // Light is the safer guess: it is what the app shipped as.
            return false;
        }
    }

    /// <summary>
    /// Reads a colour token as a System.Drawing colour so the tray menu, which
    /// is Windows Forms and cannot consume a WPF brush, can be painted from the
    /// same palette instead of its own hardcoded one.
    /// </summary>
    public DrawingColor GetDrawingColor(string tokenKey, DrawingColor fallback)
    {
        if (_application.TryFindResource(tokenKey) is Color color)
        {
            return DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
        }

        return fallback;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility))
        {
            return;
        }

        // SystemEvents can raise on a worker thread; touching Application
        // resources off the UI thread throws.
        _application.Dispatcher.BeginInvoke(new Action(Refresh));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // SystemEvents holds a static, process-lifetime handler list, so
        // failing to unhook keeps this service and the Application alive.
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
