using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Metis.Tests;

/// <summary>
/// That every resource key the code asks for by name actually exists.
///
/// This test exists because of a specific failure that shipped and went
/// unnoticed. The sign-in panel asked for four keys — <c>AuthAccent</c>,
/// <c>AuthInk</c>, <c>AuthMuted</c>, <c>AuthDangerInk</c> — that were never
/// defined in any dictionary. <c>FindResource</c> throws on a missing key, and
/// the application's global handler marks the exception handled, so the effect
/// was invisible: clicking <b>Sign in</b> started a spinner and never
/// authenticated, and the welcome page's permission rows never rendered at all.
/// That was the first screen a new user ever saw.
///
/// Nothing else could have caught it. The code compiled, the XAML parsed, the
/// suite was green, and the failure only appeared at the moment a human clicked
/// a button. A string is a string until it is looked up, so the lookup is what
/// has to be checked — statically, here, from the same text the compiler saw.
/// </summary>
public sealed class ResourceKeyTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Keys that legitimately come from somewhere this test cannot see: WPF's
    /// own system resources, or a dictionary merged only at runtime. Anything
    /// added here needs a reason beside it.
    /// </summary>
    private static readonly HashSet<string> KnownExternal = new(StringComparer.Ordinal);

    [Fact]
    public void Every_resource_key_the_code_asks_for_by_name_exists()
    {
        var declared = DeclaredKeys();
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(AppDirectory(), "*.xaml.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);

            // FindResource("Key") and TryFindResource("Key"). The Try form is
            // included deliberately: it does not throw, but a key that is never
            // found means the fallback is the only thing anyone ever sees, and
            // that is a silently dead theme token rather than a working one.
            foreach (Match match in Regex.Matches(source, @"(?:Try)?FindResource\(\s*""([^""]+)""\s*\)"))
            {
                var key = match.Groups[1].Value;
                if (!declared.Contains(key) && !KnownExternal.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)} asks for \"{key}\"");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These resource keys are looked up by name but declared in no dictionary. "
            + "FindResource throws on a missing key and the global handler hides it, so this "
            + "fails at the moment a user clicks something:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The same check for markup. A <c>StaticResource</c> that names a missing
    /// key throws while the window is being constructed, which takes the whole
    /// window with it — the failure mode that made the old account window's tray
    /// entry do nothing at all when clicked.
    /// </summary>
    [Fact]
    public void Every_static_resource_reference_in_markup_exists()
    {
        var declared = DeclaredKeys();
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories))
        {
            // A dictionary may reference keys it declares itself, and the theme
            // dictionaries are swapped as a set, so they are checked together
            // rather than individually.
            var source = File.ReadAllText(file);
            var localKeys = KeysIn(source);

            foreach (Match match in Regex.Matches(source, @"\{StaticResource\s+([A-Za-z0-9_.]+)\s*\}"))
            {
                var key = match.Groups[1].Value;
                if (!declared.Contains(key) && !localKeys.Contains(key) && !KnownExternal.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)} references {{StaticResource {key}}}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These resource keys are referenced in markup but declared nowhere. A missing "
            + "StaticResource throws while the window is being built, so the window simply "
            + "never opens:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Every key declared anywhere the application merges: the theme
    /// dictionaries, the control library, and any resources a window declares
    /// for itself.
    /// </summary>
    private static HashSet<string> DeclaredKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories))
        {
            foreach (var key in KeysIn(File.ReadAllText(file)))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static HashSet<string> KeysIn(string markup)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var element in XDocument.Parse(markup).Descendants())
            {
                if (element.Attribute(Xaml + "Key")?.Value is { Length: > 0 } key)
                {
                    keys.Add(key);
                }
            }
        }
        catch (System.Xml.XmlException)
        {
            // Markup this test cannot parse is markup it cannot vouch for. The
            // regex fallback keeps one malformed file from failing every check.
            foreach (Match match in Regex.Matches(markup, @"x:Key=""([^""]+)"""))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    /// <summary>
    /// Walks up from the test binary to the application's source. The markup is
    /// deliberately read from source rather than from a copied fixture: a stale
    /// copy could pass while the real dictionaries had drifted.
    /// </summary>
    private static string AppDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Metis.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find src/Metis.App above " + AppContext.BaseDirectory);
    }
}
