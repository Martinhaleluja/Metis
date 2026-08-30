namespace Metis.Core.Services;

/// <summary>
/// Where Metis's backend lives, compiled into the build.
///
/// This exists because the settings fields it falls back on start empty, and a
/// tester who has just installed Metis has nothing to type into them and no
/// reason to know what a project URL is. Leaving them empty meant the very
/// first thing a new user saw was a form asking for a server address, which is
/// the opposite of the intended first run.
///
/// The publishable key is safe here. It identifies the project and grants
/// nothing on its own — row-level security decides what any request may read —
/// and it is already public, because every visitor to the launch site downloads
/// it. The service role key, which does bypass row-level security, is never in
/// this application at all.
///
/// Settings still win when they are filled in, so a development build can be
/// pointed somewhere else without a recompile.
/// </summary>
public static class MetisBackend
{
    /// <summary>The hosted project both the app and the launch site talk to.</summary>
    public const string DefaultUrl = "https://liaqpxhgxmdjzwxullag.supabase.co";

    /// <summary>The publishable key for <see cref="DefaultUrl"/>.</summary>
    public const string DefaultPublishableKey = "sb_publishable_d9bpXhE-_S1nPKtNfl9X8A_Y8__55u-";

    /// <summary>
    /// The AI gateway, which answers turns on Metis's own provider key for
    /// people who have not brought one of their own.
    ///
    /// Separate from the Supabase URL because they are separate services doing
    /// separate jobs: Supabase says who you are, the gateway spends money on
    /// your behalf. Blank in a build with no gateway, which is a real
    /// configuration rather than a broken one — such a build simply never offers
    /// the managed route and everyone runs on their own key, exactly as Metis
    /// worked before any of this existed.
    /// </summary>
    public const string DefaultGatewayUrl = "https://metis-api-staging.onrender.com";

    /// <summary>
    /// The public half of the key the gateway signs entitlement snapshots with,
    /// as base64 SubjectPublicKeyInfo.
    ///
    /// Public by definition; it can only check a signature, never make one. It
    /// is blank until the gateway has a signing key, and while it is blank the
    /// client simply never trusts a cached plan and asks the server every time —
    /// which is correct behaviour rather than a failure.
    /// </summary>
    public const string EntitlementPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEzMcFaM8ZwJvWwaPjez4zHeEmmKx9JCBaFTmkca8N"
        + "Eq9ORU5j7rjgKxt2hY4LWT1TfPTWJ4crZkGw/V4HMoVb/A==";

    /// <summary>
    /// The project URL to use: whatever settings say, or the built-in one when
    /// settings are silent.
    /// </summary>
    public static string ResolveUrl(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultUrl : configured.Trim();

    /// <summary>The publishable key to use, on the same rule.</summary>
    public static string ResolveKey(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultPublishableKey : configured.Trim();

    /// <summary>The gateway to use, on the same rule. Trailing slash guaranteed,
    /// because every caller builds a relative URI against it and a missing
    /// slash silently drops the last path segment.</summary>
    public static string ResolveGatewayUrl(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? DefaultGatewayUrl : configured.Trim();
        return value.Length == 0 || value.EndsWith('/') ? value : value + "/";
    }

    /// <summary>
    /// Whether this build has a gateway to route managed turns through. False in
    /// a self-hosted or fully offline build, where everyone uses their own key.
    /// </summary>
    public static bool HasGateway(string? configured) => ResolveGatewayUrl(configured).Length > 0;

    /// <summary>
    /// Whether there is a backend to talk to at all. Only false in a build that
    /// has had the constants above deliberately blanked, which is how a fully
    /// offline copy of Metis would be made.
    /// </summary>
    public static bool IsConfigured(string? configuredUrl, string? configuredKey) =>
        ResolveUrl(configuredUrl).Length > 0 && ResolveKey(configuredKey).Length > 0;
}
