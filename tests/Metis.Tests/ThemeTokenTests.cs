using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Metis.Tests;

/// <summary>
/// Guards the theme dictionaries against the one failure mode that cannot be
/// caught by compiling: a token defined in one theme and missing from the
/// other, or referenced by a control and defined by neither. Both only surface
/// when a user switches theme, and a missing DynamicResource fails silently by
/// drawing nothing rather than throwing.
/// </summary>
public sealed class ThemeTokenTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Light_and_dark_declare_exactly_the_same_tokens()
    {
        var light = KeysOf("Tokens.Light.xaml");
        var dark = KeysOf("Tokens.Dark.xaml");

        Assert.Equal(light, dark);
    }

    [Fact]
    public void Both_themes_define_every_token_the_controls_reference()
    {
        var defined = KeysOf("Tokens.Light.xaml");
        var referenced = DynamicReferencesOf("Controls.xaml");

        Assert.NotEmpty(referenced);

        var missing = referenced.Except(defined).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            $"Controls.xaml references tokens no theme defines: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// A brush reached through StaticResource is resolved once at load and
    /// never updates, so a single one of these is enough to leave a stale
    /// light-mode colour stranded in a dark window.
    /// </summary>
    [Fact]
    public void Controls_reach_every_brush_through_DynamicResource()
    {
        var markup = File.ReadAllText(FixturePath("Controls.xaml"));

        var staticBrushes = Regex.Matches(markup, @"\{StaticResource\s+([A-Za-z0-9_]+Brush)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            staticBrushes.Length == 0,
            $"These brushes are bound with StaticResource and will not follow a theme change: {string.Join(", ", staticBrushes)}");
    }

    [Fact]
    public void Every_colour_token_has_a_matching_brush()
    {
        foreach (var theme in new[] { "Tokens.Light.xaml", "Tokens.Dark.xaml" })
        {
            var document = XDocument.Load(FixturePath(theme));
            var colours = document.Root!.Elements()
                .Where(element => element.Name.LocalName == "Color")
                .Select(element => (string?)element.Attribute(Xaml + "Key"))
                .Where(key => key is not null)
                .Cast<string>()
                .ToArray();

            var brushes = KeysOf(theme);

            foreach (var colour in colours)
            {
                Assert.True(
                    brushes.Contains(colour + "Brush"),
                    $"{theme} defines colour '{colour}' with no '{colour}Brush' to use it.");
            }
        }
    }

    private static SortedSet<string> KeysOf(string fixture)
    {
        var document = XDocument.Load(FixturePath(fixture));
        var keys = document.Root!.Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => key is not null)
            .Cast<string>();

        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }

    private static SortedSet<string> DynamicReferencesOf(string fixture)
    {
        var markup = File.ReadAllText(FixturePath(fixture));
        var names = Regex.Matches(markup, @"\{DynamicResource\s+([A-Za-z0-9_]+)\}")
            .Select(match => match.Groups[1].Value);

        return new SortedSet<string>(names, StringComparer.Ordinal);
    }

    private static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
}
