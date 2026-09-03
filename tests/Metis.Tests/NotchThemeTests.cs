using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Metis.Tests;

/// <summary>
/// The notch and the chat inside it used to be painted from literal colours on
/// the theory that they were always dark. They are not: the app-wide TextBlock
/// and TextBox styles in Controls.xaml set Foreground from the themed Text
/// token, so in light mode any text that did not name its own colour turned
/// near-black on the notch's black panel and simply vanished.
///
/// These tests pin the two halves of that failure. Nothing here needs a WPF
/// dispatcher: the markup is read as XML and the tokens as colour literals,
/// which is what lets a contrast regression fail on a build server.
/// </summary>
public sealed class NotchThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Every surface that lives inside the notch. All of them are drawn over
    /// whatever the user is working in, and all of them were literal-coloured.
    /// </summary>
    private static readonly string[] NotchMarkup =
    [
        "NotchWindow.xaml",
        "NotchChat.xaml",
        "NotchAgentDrawer.xaml",
        "NotchSpawnAgentPanel.xaml",
        "SpawnAgentDialog.xaml",
        "NotchAuth.xaml"
    ];

    /// <summary>
    /// A literal colour cannot follow a theme change, so a single one left in
    /// the notch is enough to strand a black panel in light mode. The two
    /// activity greens are the exception: they carry meaning rather than
    /// decoration and are deliberately identical in both themes.
    /// </summary>
    [Theory]
    [InlineData("NotchWindow.xaml")]
    [InlineData("NotchChat.xaml")]
    [InlineData("NotchAgentDrawer.xaml")]
    [InlineData("NotchSpawnAgentPanel.xaml")]
    [InlineData("SpawnAgentDialog.xaml")]
    [InlineData("NotchAuth.xaml")]
    public void Notch_markup_carries_no_literal_colours(string fixture)
    {
        var markup = File.ReadAllText(FixturePath(fixture));

        var literals = Regex.Matches(markup, @"#[0-9A-Fa-f]{6,8}")
            .Select(match => match.Value.ToUpperInvariant())
            .Where(value => value is not ("#30D158" or "#30B158"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            literals.Length == 0,
            $"{fixture} paints with literal colours that cannot follow a theme change: {string.Join(", ", literals)}");
    }

    /// <summary>
    /// StaticResource resolves once at load, so a brush reached that way keeps
    /// whichever theme happened to be active when the window was built.
    /// </summary>
    [Theory]
    [InlineData("NotchWindow.xaml")]
    [InlineData("NotchChat.xaml")]
    [InlineData("NotchAgentDrawer.xaml")]
    [InlineData("NotchSpawnAgentPanel.xaml")]
    [InlineData("SpawnAgentDialog.xaml")]
    [InlineData("NotchAuth.xaml")]
    public void Notch_markup_reaches_every_brush_through_DynamicResource(string fixture)
    {
        var markup = File.ReadAllText(FixturePath(fixture));

        var stranded = Regex.Matches(markup, @"\{StaticResource\s+([A-Za-z0-9_]+Brush)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            stranded.Length == 0,
            $"{fixture} binds brushes with StaticResource, which will not follow a theme change: {string.Join(", ", stranded)}");
    }

    /// <summary>
    /// Resources the notch binds dynamically that are deliberately not theme
    /// tokens. Anything added here needs a reason beside it, because the whole
    /// point of the test below is that a name which resolves to nothing draws
    /// nothing, silently.
    /// </summary>
    private static readonly string[] MotionKeys =
    [
        // The chat bubble's entrance, overridden at runtime when motion is off.
        "BubbleEnterScale",
        "BubbleEnterRise"
    ];

    /// <summary>
    /// A DynamicResource that resolves to nothing does not throw — it draws
    /// nothing at all, which is indistinguishable from the bug being fixed.
    /// </summary>
    [Fact]
    public void Both_themes_define_every_token_the_notch_references()
    {
        // Motion values are DynamicResource for a different reason than colours
        // are. They do not vary by theme, so they live in Foundations rather
        // than in the token dictionaries; they are dynamic so that turning
        // reduced motion on can neuter a XAML storyboard that cannot otherwise
        // be retimed once its template is sealed.
        var defined = TokenKeys("Tokens.Light.xaml")
            .Concat(MotionKeys)
            .ToArray();

        foreach (var fixture in NotchMarkup)
        {
            var markup = File.ReadAllText(FixturePath(fixture));
            var referenced = Regex.Matches(markup, @"\{DynamicResource\s+([A-Za-z0-9_]+)\}")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(referenced);

            var missing = referenced.Except(defined, StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                missing.Length == 0,
                $"{fixture} references tokens no theme defines: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// The notch must not be pinned to one polarity. If the body reads the same
    /// in both dictionaries then light mode has not actually been implemented,
    /// which is the state this whole file exists to prevent returning to.
    /// </summary>
    [Fact]
    public void The_notch_is_light_in_light_mode_and_dark_in_dark_mode()
    {
        var light = Tokens("Tokens.Light.xaml");
        var dark = Tokens("Tokens.Dark.xaml");

        Assert.True(Luminance(light["NotchBody"]) > 0.5, "The notch body should be a light surface in light mode.");
        Assert.True(Luminance(dark["NotchBody"]) < 0.5, "The notch body should stay a dark surface in dark mode.");

        Assert.True(Luminance(light["NotchText"]) < 0.5, "Notch text should be dark ink in light mode.");
        Assert.True(Luminance(dark["NotchText"]) > 0.5, "Notch text should stay light ink in dark mode.");
    }

    /// <summary>
    /// The symptom the user actually reported: text that cannot be read against
    /// the surface behind it. Checked in both themes, because the failure only
    /// appeared in one of them.
    /// </summary>
    [Theory]
    [InlineData("Tokens.Light.xaml")]
    [InlineData("Tokens.Dark.xaml")]
    public void Notch_text_stays_readable_on_every_notch_surface(string theme)
    {
        var tokens = Tokens(theme);

        string[] surfaces = ["NotchBody", "NotchBubble", "NotchField", "NotchSurface"];

        foreach (var surface in surfaces)
        {
            // 4.5:1 is the WCAG AA floor for body-sized text, which is what the
            // transcript and the composer are.
            AssertContrast(theme, tokens, "NotchText", surface, 4.5);

            // Secondary labels are smaller and quieter by design, but "quiet"
            // has to stop short of "gone".
            AssertContrast(theme, tokens, "NotchMuted", surface, 3.0);

            // Placeholder and hint text is the faintest thing on the panel; it
            // still has to be visible enough to read before typing over it.
            AssertContrast(theme, tokens, "NotchFaint", surface, 2.2);
        }
    }

    /// <summary>
    /// The chrome the user clicks: the pull-down chevron, the gear, and the
    /// grabber that says the notch can be dragged at all.
    /// </summary>
    [Theory]
    [InlineData("Tokens.Light.xaml")]
    [InlineData("Tokens.Dark.xaml")]
    public void Notch_chrome_stays_visible_against_the_body(string theme)
    {
        var tokens = Tokens(theme);

        AssertContrast(theme, tokens, "NotchChrome", "NotchBody", 3.0);
        AssertContrast(theme, tokens, "NotchGrabber", "NotchBody", 1.6);
    }

    private static void AssertContrast(
        string theme,
        IReadOnlyDictionary<string, string> tokens,
        string inkKey,
        string surfaceKey,
        double minimum)
    {
        var ratio = Contrast(tokens[inkKey], tokens[surfaceKey]);

        Assert.True(
            ratio >= minimum,
            $"{theme}: {inkKey} ({tokens[inkKey]}) on {surfaceKey} ({tokens[surfaceKey]}) " +
            $"has a contrast ratio of {ratio:F2}:1, below the {minimum:F1}:1 this surface needs.");
    }

    private static Dictionary<string, string> Tokens(string theme)
    {
        var document = XDocument.Load(FixturePath(theme));

        return document.Root!.Elements()
            .Where(element => element.Name.LocalName == "Color")
            .Where(element => (string?)element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(
                element => (string)element.Attribute(Xaml + "Key")!,
                element => element.Value.Trim(),
                StringComparer.Ordinal);
    }

    private static HashSet<string> TokenKeys(string theme)
    {
        var document = XDocument.Load(FixturePath(theme));

        return document.Root!.Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// WCAG relative luminance. Alpha is ignored deliberately: an overlay token
    /// such as the chip fill is judged on the colour it is made of, and every
    /// token compared here for readability is fully opaque.
    /// </summary>
    private static double Luminance(string hex)
    {
        var (r, g, b) = Channels(hex);
        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));

        static double Linear(double channel) =>
            channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static double Contrast(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        var (lighter, darker) = a >= b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static (double R, double G, double B) Channels(string hex)
    {
        var value = hex.TrimStart('#');

        // #AARRGGBB as well as #RRGGBB, because the overlay tokens carry alpha.
        if (value.Length == 8)
        {
            value = value[2..];
        }

        Assert.Equal(6, value.Length);

        return (
            Convert.ToInt32(value[..2], 16) / 255.0,
            Convert.ToInt32(value.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(value.Substring(4, 2), 16) / 255.0);
    }

    private static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
}
