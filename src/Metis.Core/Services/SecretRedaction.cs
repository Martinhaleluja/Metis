using System;
using System.Text.RegularExpressions;

namespace Metis.Core.Services;

/// <summary>
/// Takes the things that must never leave this machine back out of a string.
///
/// Metis holds provider API keys, photographs the screen, and keeps the text of
/// conversations. The privacy policy names every company that receives anything
/// and promises none of that is among it. This is where that promise is kept for
/// text that was never meant to carry a secret and picked one up anyway.
///
/// That is the case worth defending against. A deliberate field is easy to leave
/// out; the leak that actually happens is a path with a person's name in it, or
/// an exception whose message stringified the request that failed along with its
/// Authorization header. So this runs over the whole string and matches key
/// <em>shapes</em> rather than a list of known secrets — a key Metis has never
/// seen is still caught, and so is one from a provider added next year.
/// </summary>
public static class SecretRedaction
{
    /// <summary>
    /// The prefixes the providers Metis talks to put on their keys, plus the
    /// three-part JWTs Supabase issues as access tokens.
    ///
    /// Deliberately anchored on the prefix and a minimum length rather than an
    /// exact one, because providers lengthen keys without warning and a pattern
    /// that stops matching is a pattern that silently stops protecting.
    /// </summary>
    private static readonly Regex KeyShaped = new(
        @"(sk-ant-[A-Za-z0-9_\-]{16,}"
        + @"|sk-[A-Za-z0-9_\-]{16,}"
        + @"|AIza[A-Za-z0-9_\-]{20,}"
        + @"|sbp_[A-Za-z0-9_\-]{16,}"
        + @"|sb_secret_[A-Za-z0-9_\-]{16,}"
        + @"|polar_[A-Za-z0-9_\-]{16,}"
        + @"|whsec_[A-Za-z0-9_\-]{16,}"
        + @"|eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,})",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>What a redacted secret is replaced with.</summary>
    public const string Placeholder = "<redacted>";

    /// <summary>
    /// Replaces anything key-shaped, and the current user's name and home
    /// directory, in <paramref name="text"/>.
    ///
    /// The username matters as much as the keys. Every path under a profile
    /// directory contains it, stack frames are full of build paths, and a real
    /// name is personal data however accidentally it arrived.
    /// </summary>
    public static string Apply(string? text, string? userName = null, string? homeDirectory = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var cleaned = text;

        // The longer replacement first: a home directory contains the username,
        // so replacing the name first would leave "C:\Users\<user>" behind and
        // the directory pattern would no longer match.
        var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            cleaned = cleaned.Replace(home, "<home>", StringComparison.OrdinalIgnoreCase);
        }

        // Two characters or fewer is not a name worth protecting and would
        // shred ordinary words on the way past.
        var user = userName ?? Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length > 2)
        {
            cleaned = cleaned.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return KeyShaped.Replace(cleaned, Placeholder);
        }
        catch (RegexMatchTimeoutException)
        {
            // A string pathological enough to time out is one nobody should be
            // sending anywhere. Refusing to return it is the safe answer.
            return Placeholder;
        }
    }
}
