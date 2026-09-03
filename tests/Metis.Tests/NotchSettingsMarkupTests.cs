using System.Xml.Linq;

namespace Metis.Tests;

/// <summary>
/// That the settings surface's markup still says what its code-behind assumes
/// it says.
///
/// These tests have now been aimed at their third window. They started on
/// SetupWindow.xaml, which had been superseded and was constructed nowhere;
/// they were re-aimed at PreferencesWindow.xaml, which was constructed but
/// never shown, so they went on passing against markup no user could reach.
/// NotchSettings is the one the Setup menu item, the tray and the notch's own
/// gear all actually open, and it is the only settings surface left.
///
/// The value is the same as it was: a control renamed in XAML but not in the
/// code-behind throws at load rather than at compile time, in a panel somebody
/// reaches by clicking Setup.
/// </summary>
public sealed class NotchSettingsMarkupTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Every section the menu can navigate to has a panel to show. The sections
    /// are listed in the code-behind and the panels live in the markup, so
    /// nothing but a test connects the two.
    /// </summary>
    [Theory]
    [InlineData("MenuPage")]
    [InlineData("AccountPage")]
    [InlineData("IntelligencePage")]
    [InlineData("VoicePage")]
    [InlineData("GeneralPage")]
    [InlineData("CompanionPage")]
    [InlineData("PrivacyPage")]
    [InlineData("AgentsPage")]
    [InlineData("SkillsPage")]
    [InlineData("UpdatesPage")]
    [InlineData("DiagnosticsPage")]
    public void Every_settings_section_has_a_panel(string name) =>
        Assert.Contains(LoadMarkup().Descendants(), element => NameOf(element) == name);

    /// <summary>
    /// The account page carries the controls its code-behind reaches by name.
    /// </summary>
    [Theory]
    [InlineData("AccountEmail")]
    [InlineData("PlanList")]
    [InlineData("UsageCard")]
    [InlineData("UsageList")]
    [InlineData("FeatureList")]
    [InlineData("SignInChip")]
    [InlineData("SignOutChip")]
    [InlineData("ManageWebChip")]
    public void The_account_page_carries_the_controls_the_code_behind_expects(string name) =>
        Assert.Contains(LoadMarkup().Descendants(), element => NameOf(element) == name);

    /// <summary>
    /// The companion can be changed from the shipping settings surface.
    ///
    /// Every one of these existed as a setting and was honoured by the
    /// companion; only the character picker was missing, and only from here.
    /// Its sole picker lived in PreferencesWindow, which was never shown, so
    /// the choice could not be made at all and every user had the default.
    /// </summary>
    [Theory]
    [InlineData("CompanionCharacters")]
    [InlineData("CompanionColours")]
    [InlineData("CompanionSizeSlider")]
    [InlineData("CursorDistanceSlider")]
    [InlineData("CompanionAlwaysCheck")]
    public void The_companion_can_be_changed_from_settings(string name) =>
        Assert.Contains(LoadMarkup().Descendants(), element => NameOf(element) == name);

    /// <summary>
    /// Dictation can be configured from the shipping settings surface.
    ///
    /// None of these existed here. The speech-to-text provider could only be
    /// changed in PreferencesWindow, which was built and never shown, so every
    /// user was pinned to "Native" -- which has no local transcription and
    /// therefore cannot do the wake-word detection continuous listening is
    /// built on. The feature was unreachable and unfixable at the same time.
    /// </summary>
    [Theory]
    [InlineData("SpeechToTextProviderBox")]
    [InlineData("AssemblyAiApiKeyBox")]
    [InlineData("AssemblyAiModelBox")]
    [InlineData("WhisperCppExecutablePathBox")]
    [InlineData("WhisperCppModelPathBox")]
    [InlineData("TestDictationChip")]
    [InlineData("DictationStatus")]
    public void Dictation_can_be_configured_from_settings(string name) =>
        Assert.Contains(LoadMarkup().Descendants(), element => NameOf(element) == name);

    /// <summary>
    /// The three speech-to-text routes the runtime actually branches on, in the
    /// order a user should meet them: the one needing no setup first.
    /// </summary>
    [Fact]
    public void The_speech_to_text_routes_match_what_the_runtime_handles() =>
        Assert.Equal(
            ["Native", "AssemblyAI", "Whisper.cpp"],
            ComboValues(LoadMarkup(), "SpeechToTextProviderBox"));

    /// <summary>
    /// Each route's fields sit in the panel that route reveals, so choosing a
    /// provider shows exactly the fields it needs and none that it does not.
    /// </summary>
    [Fact]
    public void Dictation_fields_live_in_their_matching_route_panels()
    {
        var document = LoadMarkup();

        AssertDescendsFrom(document, "AssemblyAiApiKeyBox", "AssemblyAiPanel");
        AssertDescendsFrom(document, "AssemblyAiModelBox", "AssemblyAiPanel");
        AssertDescendsFrom(document, "WhisperCppExecutablePathBox", "WhisperPanel");
        AssertDescendsFrom(document, "WhisperCppModelPathBox", "WhisperPanel");
    }

    [Fact]
    public void Provider_fields_live_in_their_matching_capability_panels()
    {
        var document = LoadMarkup();

        AssertDescendsFrom(document, "GeminiApiKeyBox", "GeminiCard");
        AssertDescendsFrom(document, "OpenAiApiKeyBox", "OpenAiCard");
        AssertDescendsFrom(document, "ClaudeModelBox", "ClaudeCard");
        AssertDescendsFrom(document, "OpenRouterModelBox", "OpenRouterCard");
        AssertDescendsFrom(document, "OllamaEndpointBox", "OllamaCard");
    }

    /// <summary>
    /// The gateway route has no key box, and that is the point of it: there is
    /// nothing to paste, because it runs on Metis's own provider account. A key
    /// field here would be asking for a credential the route does not use.
    /// </summary>
    [Fact]
    public void The_managed_route_asks_for_no_credential()
    {
        var card = FindNamed(LoadMarkup(), "MetisManagedCard");

        Assert.DoesNotContain(
            card.Descendants(),
            element => element.Name == Presentation + "PasswordBox");
    }

    /// <summary>
    /// Metis is a learning tool: there is no Autopilot and no mode to pick.
    /// </summary>
    [Fact]
    public void Settings_offers_no_mode_choice() =>
        Assert.DoesNotContain(
            LoadMarkup().Descendants(),
            element => NameOf(element) == "OperatingModeBox");

    /// <summary>
    /// No page scrolls inside the shell's own scroller.
    ///
    /// The settings panel used to hold a ScrollViewer capped at MaxHeight=520 —
    /// the last copy of a number NotchGeometry was written to delete. It capped
    /// every section at 520 points whatever the monitor was, reported 520 as its
    /// desired height so the shell could never discover there was more to show,
    /// and put a second scrollbar down the same edge as the first with the inner
    /// one capturing the wheel.
    /// </summary>
    [Fact]
    public void The_settings_page_does_not_nest_its_own_scroller()
    {
        var scrollers = LoadMarkup()
            .Descendants(Presentation + "ScrollViewer")
            .ToArray();

        Assert.Empty(scrollers);
    }

    /// <summary>
    /// The panel does not pin its own width either. It did, at 640, which
    /// defeated the shell's "measure again in the space actually available"
    /// step: a control with an explicit Width ignores the constraint it is
    /// measured under, so a narrower screen could never be adapted to.
    /// </summary>
    [Fact]
    public void The_settings_panel_does_not_pin_its_own_width()
    {
        var root = LoadMarkup().Root!;

        Assert.Null(root.Attribute("Width"));
    }

    private static XDocument LoadMarkup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotchSettings.xaml");
        return XDocument.Load(path, LoadOptions.SetLineInfo);
    }

    private static string[] ComboValues(XDocument document, string comboName) =>
        FindNamed(document, comboName)
            .Elements(Presentation + "ComboBoxItem")
            .Select(item => (string?)item.Attribute("Content"))
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

    private static void AssertDescendsFrom(XDocument document, string childName, string ancestorName)
    {
        var child = FindNamed(document, childName);

        Assert.True(
            child.Ancestors().Any(ancestor => NameOf(ancestor) == ancestorName),
            $"{childName} should live inside {ancestorName}, so showing that panel shows the field.");
    }

    private static XElement FindNamed(XDocument document, string name) =>
        document.Descendants().SingleOrDefault(element => NameOf(element) == name)
        ?? throw new Xunit.Sdk.XunitException($"NotchSettings.xaml has no element named {name}.");

    private static string? NameOf(XElement element) => (string?)element.Attribute(Xaml + "Name");
}
