using System.Reflection;

namespace Metis.Core.Services;

/// <summary>
/// Which build of Metis this is.
///
/// Nothing read this before, and the cost of that showed up as soon as builds
/// started moving quickly: an installed copy and a freshly compiled one are
/// indistinguishable at a glance, so a fix that had not actually shipped looked
/// exactly like a fix that had not worked. Time was lost to that more than once.
///
/// The installer already stamps <c>InformationalVersion</c> on every publish,
/// so the number exists; this just makes it readable, and makes comparing two
/// of them a rule that can be tested rather than a string comparison that gets
/// 10 wrong against 9.
/// </summary>
public static class AppVersion
{
    private static readonly Lazy<string> Resolved = new(ReadFromAssembly);

    /// <summary>
    /// The running build, as "1.2.3". Falls back to "0.0.0" when the attribute
    /// is missing, which happens under the test runner and in a debug build
    /// that was never published.
    /// </summary>
    public static string Current => Resolved.Value;

    private static string ReadFromAssembly()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var text = Normalize(informational) ?? Normalize(assembly.GetName().Version?.ToString());
        return text ?? "0.0.0";
    }

    /// <summary>
    /// Reduces a version string to comparable numbers.
    ///
    /// Deliberately forgiving about the decorations these strings pick up: a
    /// leading "v" from a git tag, and the "+abc1234" source-revision suffix
    /// the SDK appends to InformationalVersion by default. Neither changes
    /// which build is newer, and neither parses.
    /// </summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        // Cut anything that is not part of the number: "+sha", "-beta.1".
        var cut = trimmed.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        return Version.TryParse(trimmed, out var version) ? Normalise(version) : null;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is a build worth moving to.
    ///
    /// False when either side will not parse. That is the safe answer: an
    /// unreadable version is not evidence of anything, and downloading an
    /// installer on the strength of a string nobody could interpret is a worse
    /// outcome than staying on a working build.
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var offered = Parse(candidate);
        var running = Parse(current);

        return offered is not null && running is not null && offered > running;
    }

    /// <summary>
    /// Pads the unspecified components to zero, so 3.4 and 3.4.0 compare equal
    /// instead of 3.4 sorting below 3.4.0 as Version's -1 components make it.
    /// </summary>
    private static Version Normalise(Version version) => new(
        version.Major,
        version.Minor,
        version.Build < 0 ? 0 : version.Build,
        version.Revision < 0 ? 0 : version.Revision);

    private static string? Normalize(string? text)
    {
        var parsed = Parse(text);
        return parsed is null ? null : $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
    }
}
