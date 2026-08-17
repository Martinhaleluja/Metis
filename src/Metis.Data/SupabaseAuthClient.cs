using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Data;

/// <summary>The outcome of an authentication attempt, with a sentence worth showing.</summary>
public sealed record AuthResult(
    bool Success,
    string Message,
    string? AccessToken = null,
    string? RefreshToken = null,
    MetisAccount? Account = null)
{
    public static AuthResult Failed(string message) => new(false, message);
}

/// <summary>
/// Signs in against Supabase and keeps the session alive.
///
/// The password is used once, to exchange for tokens, and is never stored — not
/// in settings, not in the credential store, not in memory beyond the call. The
/// refresh token is what persists, because it can be revoked from the server
/// and a password cannot.
/// </summary>
public sealed class SupabaseAuthClient(HttpClient http)
{
    /// <summary>
    /// Creates an account. Supabase sends the confirmation email; the account
    /// exists immediately but earns no entitlements until the address is
    /// verified, which the database enforces rather than this client.
    /// </summary>
    public async Task<AuthResult> SignUpAsync(
        string supabaseUrl,
        string anonKey,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!LooksLikeEmail(email))
        {
            return AuthResult.Failed("That does not look like an email address.");
        }

        if (password.Length < 8)
        {
            return AuthResult.Failed("Use at least eight characters.");
        }

        var response = await PostAsync(
            supabaseUrl, anonKey, "/auth/v1/signup",
            new { email, password }, cancellationToken);

        if (response is null)
        {
            return AuthResult.Failed("Metis could not reach the sign-in service.");
        }

        using var document = response.Value.Document;
        if (!response.Value.Ok)
        {
            return AuthResult.Failed(ReadError(document, "That sign-up was refused."));
        }

        // Supabase returns tokens straight away when email confirmation is off,
        // and no session when it is on. Both are success; only one signs you in.
        return ReadSession(document, supabaseUrl)
               ?? new AuthResult(true, "Account created. Check your email to confirm the address, then sign in.");
    }

    public async Task<AuthResult> SignInAsync(
        string supabaseUrl,
        string anonKey,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await PostAsync(
            supabaseUrl, anonKey, "/auth/v1/token?grant_type=password",
            new { email, password }, cancellationToken);

        if (response is null)
        {
            return AuthResult.Failed("Metis could not reach the sign-in service.");
        }

        using var document = response.Value.Document;
        if (!response.Value.Ok)
        {
            return AuthResult.Failed(ReadError(document, "That email and password were not accepted."));
        }

        return ReadSession(document, supabaseUrl)
               ?? AuthResult.Failed("The sign-in service returned no session.");
    }

    /// <summary>
    /// Trades a stored refresh token for a fresh session at startup, so the
    /// user does not sign in every time Metis opens.
    /// </summary>
    public async Task<AuthResult> RefreshAsync(
        string supabaseUrl,
        string anonKey,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var response = await PostAsync(
            supabaseUrl, anonKey, "/auth/v1/token?grant_type=refresh_token",
            new { refresh_token = refreshToken }, cancellationToken);

        if (response is null)
        {
            return AuthResult.Failed("Metis could not reach the sign-in service.");
        }

        using var document = response.Value.Document;
        return response.Value.Ok
            ? ReadSession(document, supabaseUrl) ?? AuthResult.Failed("The session could not be renewed.")
            : AuthResult.Failed("The saved session has expired. Sign in again.");
    }

    /// <summary>
    /// Reads role and plan from account_status. Row-level security means this
    /// can only ever return the caller's own row, whatever is asked for.
    /// </summary>
    public async Task<MetisAccount?> LoadAccountAsync(
        string supabaseUrl,
        string anonKey,
        string accessToken,
        string userId,
        MetisEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{supabaseUrl.TrimEnd('/')}/rest/v1/account_status?select=role,plan,email_verified");
            request.Headers.Add("apikey", anonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var row = document.RootElement[0];
            return new MetisAccount(
                userId,
                Entitlements.ParseRole(row.GetProperty("role").GetString()),
                Entitlements.ParsePlan(row.GetProperty("plan").GetString()),
                environment,
                row.GetProperty("email_verified").GetBoolean());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<(bool Ok, JsonDocument Document)?> PostAsync(
        string supabaseUrl,
        string anonKey,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, supabaseUrl.TrimEnd('/') + path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("apikey", anonKey);

            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return (response.IsSuccessStatusCode, JsonDocument.Parse(text));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AuthResult? ReadSession(JsonDocument document, string supabaseUrl)
    {
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var access) ||
            !root.TryGetProperty("refresh_token", out var refresh))
        {
            return null;
        }

        var userId = root.TryGetProperty("user", out var user) && user.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

        _ = supabaseUrl;
        return new AuthResult(
            true,
            "Signed in.",
            access.GetString(),
            refresh.GetString(),
            userId is null
                ? null
                : new MetisAccount(userId, UserRole.User, PlanTier.Free, MetisEnvironment.Production));
    }

    /// <summary>
    /// Supabase puts its explanation in one of three fields depending on the
    /// endpoint. A sign-in that fails with "an error occurred" is unactionable,
    /// so all three are tried before falling back.
    /// </summary>
    private static string ReadError(JsonDocument document, string fallback)
    {
        foreach (var field in new[] { "msg", "error_description", "message", "error" })
        {
            if (document.RootElement.TryGetProperty(field, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text!;
                }
            }
        }

        return fallback;
    }

    private static bool LooksLikeEmail(string value)
    {
        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0 && trimmed.LastIndexOf('.') > at && !trimmed.EndsWith('.');
    }
}
