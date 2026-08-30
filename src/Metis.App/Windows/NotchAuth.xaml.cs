using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Metis.Core.Models;
using Metis.Core.Services;
using Metis.Data;

// The project pulls in Windows Forms for the tray icon, and both toolkits define
// these names. Aliased rather than disambiguated at each use, so the body of the
// file reads as ordinary WPF.
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using CheckBox = System.Windows.Controls.CheckBox;
using Cursors = System.Windows.Input.Cursors;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Metis.App.Windows;

/// <summary>
/// The first run, inside the notch.
///
/// Sign-in happens here rather than in a window of its own because the notch is
/// the only part of Metis a brand-new user has seen. Opening a separate dialog
/// over an empty desktop asks someone to trust a login form belonging to an
/// application they have not met; asking inside the thing they just installed,
/// with nothing else on screen, is the same request with an answer to "what is
/// this?" already visible.
///
/// The panel has two pages and shows them in order: the account, then what
/// Metis is and what it needs. Nothing else in Metis is visible until both are
/// done — the companion does not appear and the chat cannot be opened — which
/// is the point of gating here rather than nagging later.
/// </summary>
public partial class NotchAuth : UserControl
{
    private readonly SupabaseAuthClient _auth = new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    private Runtime.MetisRuntime? _runtime;
    private Metis.Core.Contracts.ISessionTokenAccess? _secrets;
    private Storyboard? _spin;
    private bool _signingUp;
    private bool _busy;

    /// <summary>
    /// Raised the moment a session is established. The host takes over here:
    /// Setup opens next so the user can add their own API keys, and only after
    /// that does the welcome page come back.
    /// </summary>
    public event EventHandler? SignedIn;

    /// <summary>Raised when the panel has nothing left to ask for.</summary>
    public event EventHandler? Finished;

    /// <summary>Raised when the panel's height changes, so the notch can follow it.</summary>
    public event EventHandler? ContentSizeChanged;

    public NotchAuth()
    {
        InitializeComponent();
    }

    public void Attach(Runtime.MetisRuntime runtime, Metis.Core.Contracts.ISessionTokenAccess secrets)
    {
        _runtime = runtime;
        _secrets = secrets;
        ApplyMode();
    }

    public double MeasureDesiredHeight(double width)
    {
        Measure(new Size(Math.Max(width, 1), double.PositiveInfinity));
        return DesiredSize.Height;
    }

    /// <summary>Puts the caret where the user is about to type.</summary>
    public void FocusFirstField()
    {
        if (WelcomePage.Visibility == Visibility.Visible)
        {
            return;
        }

        EmailBox.Focus();
        Keyboard.Focus(EmailBox);
        EmailBox.CaretIndex = EmailBox.Text.Length;
    }

    /// <summary>
    /// Skips straight to the welcome page, for a user who is already signed in
    /// but has not been told what Metis is yet.
    /// </summary>
    public void ShowWelcomeOnly()
    {
        AuthPage.Visibility = Visibility.Collapsed;
        ShowWelcome();
    }

    // ============================= Sign in =============================

    private void Switch_OnClick(object sender, RoutedEventArgs e)
    {
        _signingUp = !_signingUp;
        ApplyMode();
        RaiseSizeChanged();
    }

    private void ApplyMode()
    {
        Heading.Text = _signingUp ? "Create your Metis account" : "Sign in to Metis";
        Subheading.Text = _signingUp
            ? "One account, so Metis remembers what you have learned on any machine you use it on."
            : "Your account is what lets Metis remember what you have learned.";
        PrimaryLabel.Text = _signingUp ? "Create account" : "Sign in";
        SwitchPrompt.Text = _signingUp ? "Already have an account?" : "New to Metis?";
        SwitchAction.Text = _signingUp ? "Sign in" : "Create an account";
        PasswordHint.Visibility = _signingUp ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EmailBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        EmailPlaceholder.Visibility = EmailBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PasswordField_OnChanged(object sender, RoutedEventArgs e)
    {
        PasswordPlaceholder.Visibility =
            PasswordField.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Field_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        // Enter from the email box moves on rather than submitting a half-filled
        // form, which is what every other sign-in the user has ever used does.
        if (ReferenceEquals(sender, EmailBox) && PasswordField.Password.Length == 0)
        {
            PasswordField.Focus();
        }
        else
        {
            _ = SubmitAsync();
        }

        e.Handled = true;
    }

    private void Primary_OnClick(object sender, MouseButtonEventArgs e) => _ = SubmitAsync();

    private async Task SubmitAsync()
    {
        if (_busy || _runtime is null || _secrets is null)
        {
            return;
        }

        var email = EmailBox.Text.Trim();
        var password = PasswordField.Password;

        if (email.Length == 0 || password.Length == 0)
        {
            Say("Fill in both fields to continue.", problem: true);
            return;
        }

        var url = MetisBackend.ResolveUrl(_runtime.Settings.SupabaseUrl);
        var key = MetisBackend.ResolveKey(_runtime.Settings.SupabaseAnonKey);

        SetBusy(true);
        Say(_signingUp ? "Creating your account…" : "Signing you in…", problem: false);

        var result = _signingUp
            ? await _auth.SignUpAsync(url, key, email, password)
            : await _auth.SignInAsync(url, key, email, password);

        SetBusy(false);

        if (!result.Success)
        {
            Say(result.Message, problem: true);
            return;
        }

        // Sign-up with email confirmation on returns success and no session.
        // The account exists; the user simply cannot come in yet.
        if (result.AccessToken is null || result.Account is null)
        {
            Say(result.Message, problem: false);
            _signingUp = false;
            ApplyMode();
            PasswordField.Clear();
            RaiseSizeChanged();
            return;
        }

        await AdoptAsync(result, url, key);
    }

    private async Task AdoptAsync(AuthResult result, string url, string key)
    {
        if (result.RefreshToken is not null)
        {
            _secrets!.WriteSupabaseRefreshToken(result.RefreshToken);
        }

        var account = await _auth.LoadAccountAsync(
            url, key, result.AccessToken!, result.Account!.UserId,
            Entitlements.ParseEnvironment(_runtime!.Settings.MetisEnvironment));

        _runtime.SetSession(result.AccessToken, result.AccessTokenExpiresUtc);
        _runtime.SignIn(account ?? result.Account);

        // What the plan actually includes comes from the gateway rather than
        // from anything this panel decides. Not awaited: signing in must not
        // wait on a service that may be cold.
        _ = _runtime.RefreshEntitlementsAsync();

        // The password has done its one job. Nothing keeps it.
        PasswordField.Clear();

        await Save(_runtime.Settings with { LastAuthenticatedUtc = DateTimeOffset.UtcNow });

        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    // ====================== About and permissions ======================

    private void ShowWelcome()
    {
        AuthPage.Visibility = Visibility.Collapsed;
        WelcomePage.Visibility = Visibility.Visible;
        WelcomeHeading.Text = "Welcome to Metis";

        BuildPermissionRows();
        RaiseSizeChanged();

        // Let the notch finish resizing before the rows arrive, or they stagger
        // in against a panel that is still growing underneath them.
        Dispatcher.BeginInvoke(new Action(StaggerRowsIn), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// What Metis actually needs, described as what it is.
    ///
    /// An unpackaged desktop application has no Windows permission API to call,
    /// so dressing these up as system grants would be a costume. Exactly one of
    /// them is a real OS setting — the microphone — and that row opens the
    /// Windows privacy page rather than pretending to grant anything itself.
    /// The rest are Metis's own switches, and are labelled as such.
    /// </summary>
    private void BuildPermissionRows()
    {
        if (_runtime is null || PermissionRows.Children.Count > 0)
        {
            return;
        }

        var settings = _runtime.Settings;

        AddRow(
            "Look at your screen",
            "So Metis can teach you what is actually in front of you.",
            settings.CaptureActiveWindow,
            on => _ = Save(_runtime!.Settings with { CaptureActiveWindow = on }));

        AddRow(
            "Draw over your screen",
            "The arrows, outlines and pointer. Click-through, and they fade on their own.",
            settings.VisualGuidanceEnabled,
            on => _ = Save(_runtime!.Settings with { VisualGuidanceEnabled = on }));

        AddRow(
            "Speak out loud",
            "Metis explains as it draws. Turn this off to read instead.",
            settings.SpeechEnabled,
            on => _ = Save(_runtime!.Settings with { SpeechEnabled = on }));

        AddMicrophoneRow();
    }

    private void AddRow(string title, string detail, bool on, Action<bool> apply)
    {
        var toggle = new CheckBox
        {
            IsChecked = on,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Focusable = false
        };

        toggle.Checked += (_, _) => apply(true);
        toggle.Unchecked += (_, _) => apply(false);

        PermissionRows.Children.Add(BuildRowShell(title, detail, toggle));
    }

    /// <summary>
    /// The one genuine Windows permission. Metis cannot grant it, so this row
    /// does not offer a switch that would lie about having done so — it opens
    /// the page where the user can actually decide.
    /// </summary>
    private void AddMicrophoneRow()
    {
        var link = new TextBlock
        {
            Text = "Open settings",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 2, 0),
            Cursor = Cursors.Hand,
            Foreground = (Brush)FindResource("AuthAccent")
        };

        link.MouseLeftButtonUp += (_, _) => OpenMicrophoneSettings();

        PermissionRows.Children.Add(BuildRowShell(
            "Microphone",
            "Only for talking to Metis, and only while you hold the shortcut. Windows grants this one.",
            link));
    }

    private void OpenMicrophoneSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _runtime?.Log.Error("Could not open the microphone privacy settings.", exception);
        }
    }

    private Border BuildRowShell(string title, string detail, UIElement trailing)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AuthInk"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("AuthMuted")
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(trailing, 1);
        grid.Children.Add(text);
        grid.Children.Add(trailing);

        var scale = new ScaleTransform(0.96, 0.96);
        var rise = new TranslateTransform(0, 8);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(rise);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 0, 0, 6),
            SnapsToDevicePixels = true,
            Child = grid,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = group
        };
    }

    /// <summary>
    /// The rows arrive one after another rather than all at once, so the eye is
    /// walked down the list instead of being handed a block to re-read. Same
    /// 40 ms step and the same easing the trace toolbar uses.
    /// </summary>
    private void StaggerRowsIn()
    {
        var step = TimeSpan.FromMilliseconds(40);
        var index = 0;

        foreach (var child in PermissionRows.Children)
        {
            if (child is not Border row || row.RenderTransform is not TransformGroup group)
            {
                continue;
            }

            var start = TimeSpan.FromMilliseconds(index * step.TotalMilliseconds);
            var duration = TimeSpan.FromMilliseconds(260);

            var fade = new DoubleAnimation(0, 1, duration) { BeginTime = start };
            row.BeginAnimation(OpacityProperty, fade);

            if (group.Children[0] is ScaleTransform scale)
            {
                var pop = new DoubleAnimation(0.96, 1, duration)
                {
                    BeginTime = start,
                    EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }

            if (group.Children[1] is TranslateTransform rise)
            {
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration)
                {
                    BeginTime = start,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }

            index++;
        }
    }

    private void Continue_OnClick(object sender, MouseButtonEventArgs e)
    {
        Finished?.Invoke(this, EventArgs.Empty);
    }

    // ============================== Chrome ==============================

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PrimaryLabel.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        Spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        EmailBox.IsEnabled = !busy;
        PasswordField.IsEnabled = !busy;

        if (busy)
        {
            _spin = new Storyboard();
            var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(turn, Spinner);
            Storyboard.SetTargetProperty(turn, new PropertyPath("RenderTransform.Angle"));
            _spin.Children.Add(turn);
            _spin.Begin();
        }
        else
        {
            _spin?.Stop();
            _spin = null;
        }
    }

    private void Say(string message, bool problem)
    {
        StatusLine.Text = message;
        StatusLine.Visibility = Visibility.Visible;
        StatusLine.Foreground = (Brush)FindResource(problem ? "AuthDangerInk" : "AuthMuted");
        RaiseSizeChanged();
    }

    /// <summary>
    /// Persists a settings change. The permission toggles call this without
    /// awaiting it, so the failure has to be caught here: an unobserved
    /// exception from a checkbox handler would otherwise take the process down
    /// during someone's first thirty seconds with Metis.
    /// </summary>
    private async Task Save(AppSettings settings)
    {
        try
        {
            await _runtime!.SaveSettingsAsync(settings, null, null);
        }
        catch (Exception exception)
        {
            _runtime?.Log.Error("Could not save the first-run settings.", exception);
        }
    }

    private void RaiseSizeChanged() => ContentSizeChanged?.Invoke(this, EventArgs.Empty);
}
