using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Metis.Core.Models;

namespace Metis.Api;

/// <summary>
/// Turns an <c>Authorization: Bearer &lt;supabase access token&gt;</c> header
/// into a signed-in principal.
///
/// This used to be a local function called from inside each endpoint, which
/// worked but put the caller's identity out of reach of everything that runs
/// before the endpoint body — and the rate limiter is exactly that. A limiter
/// that cannot see who is calling can only partition by IP address, which
/// punishes an office and lets a laptop with a script through.
/// </summary>
public sealed class SupabaseTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SupabaseGateway supabase,
    IMemoryCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SupabaseAccessToken";

    /// <summary>Claim types this handler emits, so nothing spells them twice.</summary>
    public const string PlanClaim = "metis:plan";
    public const string RoleClaim = "metis:role";
    public const string EmailVerifiedClaim = "metis:email_verified";
    public const string HasAccountClaim = "metis:has_account";

    /// <summary>
    /// How long a validated token is trusted without asking Supabase again.
    ///
    /// The comment on ResolveUserIdAsync explains why validation is a network
    /// call rather than a local signature check: revocation. Caching gives some
    /// of that back, so the number matters. Sixty seconds means a revoked token
    /// keeps working for at most a minute, against two Supabase round trips
    /// saved on every single turn — which under a per-minute rate limit would
    /// otherwise be both the dominant latency and the dominant load.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[7..].Trim();
        if (token.Length == 0)
        {
            return AuthenticateResult.Fail("No access token.");
        }

        // Keyed by a hash rather than by the token, so the token itself is never
        // a value sitting in a process-wide dictionary that a memory dump or a
        // diagnostic endpoint could enumerate.
        var key = "auth:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        if (!cache.TryGetValue(key, out CachedIdentity? identity) || identity is null)
        {
            var userId = await supabase.ResolveUserIdAsync(token, Context.RequestAborted);
            if (userId is null)
            {
                return AuthenticateResult.Fail("The access token was refused.");
            }

            var account = await supabase.LoadAccountAsync(userId, Context.RequestAborted);
            identity = new CachedIdentity(userId, account);
            cache.Set(key, identity, CacheFor);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId),
            new("sub", identity.UserId),
            new(HasAccountClaim, (identity.Account is not null).ToString())
        };

        if (identity.Account is { } account2)
        {
            claims.Add(new Claim(PlanClaim, account2.Plan.ToString().ToLowerInvariant()));
            claims.Add(new Claim(RoleClaim, account2.Role.ToString().ToLowerInvariant()));
            claims.Add(new Claim(EmailVerifiedClaim, account2.EmailVerified.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private sealed record CachedIdentity(string UserId, MetisAccount? Account);
}

/// <summary>
/// Reads the caller back out of the request, in the two shapes endpoints want.
/// </summary>
public static class CallerExtensions
{
    public static string? UserId(this HttpContext context) =>
        context.User.FindFirst("sub")?.Value;

    /// <summary>
    /// The caller's account, or null when they authenticated but no account row
    /// exists for them.
    ///
    /// That second case is a broken state rather than an unauthorised one and is
    /// worth telling apart when reading logs: it means a user exists in auth but
    /// the trigger that seeds their account row did not run.
    /// </summary>
    public static MetisAccount? Account(this HttpContext context, MetisEnvironment environment)
    {
        var userId = context.UserId();
        if (userId is null || context.User.FindFirst(SupabaseTokenHandler.HasAccountClaim)?.Value != bool.TrueString)
        {
            return null;
        }

        return new MetisAccount(
            userId,
            Metis.Core.Services.Entitlements.ParseRole(context.User.FindFirst(SupabaseTokenHandler.RoleClaim)?.Value),
            Metis.Core.Services.Entitlements.ParsePlan(context.User.FindFirst(SupabaseTokenHandler.PlanClaim)?.Value),
            environment,
            bool.TryParse(context.User.FindFirst(SupabaseTokenHandler.EmailVerifiedClaim)?.Value, out var verified) && verified);
    }
}
