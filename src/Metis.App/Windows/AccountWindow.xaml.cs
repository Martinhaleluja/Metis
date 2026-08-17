using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Metis.Core.Models;
using Metis.Core.Services;
using Metis.Data;

namespace Metis.App.Windows;

/// <summary>
/// Signing in, and seeing what the account is worth.
///
/// Deliberately optional. Metis works with no account at all, on the user's own
/// API key, and the window says so — an account is for Pro features and sync,
/// not a gate in front of the product.
/// </summary>
public partial class AccountWindow : Window
{
    private readonly Runtime.MetisRuntime _runtime;
    private readonly SupabaseAuthClient _auth;
    private readonly ISecretStoreAccess _secrets;
    private bool _signingUp;
    private bool _busy;

    /// <summary>
    /// The narrow slice of the credential store this window needs. Passing the
    /// whole store would hand a sign-in dialog the ability to read every
    /// provider key in the machine.
    /// </summary>
    public interface ISecretStoreAccess
    {
        string? ReadSupabaseRefreshToken();
        void WriteSupabaseRefreshToken(string token);
        void DeleteSupabaseRefreshToken();
    }

    public AccountWindow(Runtime.MetisRuntime runtime, ISecretStoreAccess secrets)
    {
        InitializeComponent();
        _runtime = runtime;
        _secrets = secrets;
        _auth = new SupabaseAuthClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

        Loaded += async (_, _) => await RestoreSessionAsync();
    }

    /// <summary>
    /// Trades a stored refresh token for a session, so the window opens already
    /// signed in when it can.
    /// </summary>
    private async Task RestoreSessionAsync()
    {
        var token = _secrets.ReadSupabaseRefreshToken();
        if (string.IsNullOrWhiteSpace(token) || !HasBackend(out var url, out var key))
        {
            ShowSignedOut();
            return;
        }

        SetBusy(true, "Restoring your session…");
        var result = await _auth.RefreshAsync(url, key, token!);
        SetBusy(false, string.Empty);

        if (!result.Success || result.AccessToken is null || result.Account is null)
        {
            // An expired or revoked token is not an error worth alarming anyone
            // about; it just means signing in again.
            _secrets.DeleteSupabaseRefreshToken();
            ShowSignedOut();
            return;
        }

        await AdoptSessionAsync(result, url, key);
    }

    private async void Primary_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (!HasBackend(out var url, out var key))
        {
            StatusText.Text = "No Metis backend is configured. Add the Supabase URL and key in Setup.";
            return;
        }

        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (email.Length == 0 || password.Length == 0)
        {
            StatusText.Text = "Enter your email and password.";
            return;
        }

        SetBusy(true, _signingUp ? "Creating your account…" : "Signing in…");
        var result = _signingUp
            ? await _auth.SignUpAsync(url, key, email, password)
            : await _auth.SignInAsync(url, key, email, password);
        SetBusy(false, result.Message);

        // Cleared whatever happened. A password left in a box is a password on
        // screen, and this window can be reopened by anyone at the machine.
        PasswordBox.Clear();

        if (result is { Success: true, AccessToken: not null, Account: not null })
        {
            await AdoptSessionAsync(result, url, key);
        }
    }

    /// <summary>
    /// Stores the refresh token, loads role and plan, and shows what the
    /// account is entitled to.
    /// </summary>
    private async Task AdoptSessionAsync(AuthResult result, string url, string key)
    {
        if (result.RefreshToken is not null)
        {
            _secrets.WriteSupabaseRefreshToken(result.RefreshToken);
        }

        var account = await _auth.LoadAccountAsync(
            url, key, result.AccessToken!, result.Account!.UserId,
            Entitlements.ParseEnvironment(_runtime.Settings.MetisEnvironment));

        _runtime.SignIn(account ?? result.Account);
        ShowSignedIn(account ?? result.Account, EmailBox.Text.Trim());
    }

    private void SignOut_OnClick(object sender, RoutedEventArgs e)
    {
        _secrets.DeleteSupabaseRefreshToken();
        _runtime.SignOut();
        EmailBox.Clear();
        PasswordBox.Clear();
        ShowSignedOut();
        StatusText.Text = "Signed out.";
    }

    private void SwitchMode_OnClick(object sender, RoutedEventArgs e)
    {
        _signingUp = !_signingUp;
        HeadingText.Text = _signingUp ? "Create a Metis account" : "Sign in to Metis";
        PrimaryButton.Content = _signingUp ? "Create account" : "Sign in";
        SwitchModeButton.Content = _signingUp ? "I already have an account" : "Create an account instead";
        PasswordHintText.Visibility = _signingUp ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = string.Empty;
    }

    private void ShowSignedOut()
    {
        FormPanel.Visibility = Visibility.Visible;
        AccountPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowSignedIn(MetisAccount account, string email)
    {
        FormPanel.Visibility = Visibility.Collapsed;
        AccountPanel.Visibility = Visibility.Visible;

        HeadingText.Text = "Your Metis account";
        SubheadingText.Text = "What this account unlocks is decided by the Metis servers, not by this app.";
        AccountEmailText.Text = email.Length > 0 ? email : account.UserId;
        AccountPlanText.Text = account.Plan == PlanTier.Pro
            ? $"Metis Pro — {account.Role.ToString().ToLowerInvariant()}"
            : $"Free plan — {account.Role.ToString().ToLowerInvariant()}";
        AccountEnvironmentText.Text = $"Connected to {account.Environment.ToString().ToLowerInvariant()}";

        // Listed from the same entitlement rules the server uses, so what is
        // shown here and what is permitted there cannot disagree.
        FeatureList.ItemsSource = Enum.GetValues<MetisFeature>()
            .Select(feature => new TextBlock
            {
                Text = (Entitlements.Has(account, feature) ? "✓  " : "—  ") + Describe(feature),
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = Entitlements.Has(account, feature)
                    ? (System.Windows.Media.Brush)FindResource("TextBrush")
                    : (System.Windows.Media.Brush)FindResource("MutedBrush")
            })
            .ToArray();
    }

    private static string Describe(MetisFeature feature) => feature switch
    {
        MetisFeature.CustomAiProvider => "Connect your own AI provider",
        MetisFeature.ComputerControl => "Let Metis work the computer",
        MetisFeature.SystemCommands => "Run system commands",
        MetisFeature.DeveloperMode => "Developer diagnostics",
        MetisFeature.ExperimentalFeatures => "Experimental features",
        MetisFeature.StagingAccess => "Staging access",
        MetisFeature.AdminDashboard => "Admin dashboard",
        _ => "Internal cost visibility"
    };

    private bool HasBackend(out string url, out string key)
    {
        url = _runtime.Settings.SupabaseUrl?.Trim() ?? string.Empty;
        key = _runtime.Settings.SupabaseAnonKey?.Trim() ?? string.Empty;
        return url.Length > 0 && key.Length > 0;
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        PrimaryButton.IsEnabled = !busy;
        SwitchModeButton.IsEnabled = !busy;
        StatusText.Text = status;
    }
}
