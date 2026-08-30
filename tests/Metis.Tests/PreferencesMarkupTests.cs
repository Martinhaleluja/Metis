using System.Xml.Linq;

namespace Metis.Tests;

/// <summary>
/// That the Preferences window's markup still says what the application assumes
/// it says.
///
/// These used to point at SetupWindow.xaml, which had been superseded by
/// Preferences and was no longer constructed anywhere — so the assertions were
/// passing against a file nobody could open. Aimed at the live window they mean
/// something again: a control renamed in XAML but not in the code-behind throws
/// at load, in a window a user reaches by clicking Setup, and there is no
/// compiler error to catch it first.
/// </summary>
public sealed class PreferencesMarkupTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Preferences_exposes_independent_reasoning_transcription_and_voice_selectors()
    {
        var document = LoadMarkup();

        // "Metis" is the gateway route, and it sits second on purpose: after the
        // provider most people use their own key for, and before the rest.
        Assert.Equal(
            ["Gemini", "Metis", "OpenAI", "Claude", "OpenRouter", "OpenClaw", "Ollama", "Automatic"],
            ComboValues(document, "ProviderBox"));
        Assert.Equal(
            ["Native", "Whisper.cpp", "AssemblyAI"],
            ComboValues(document, "SpeechToTextProviderBox"));
        // "Native" is the offline Windows voice that is always present. Piper is
        // offline too, but ships as a separate executable and a voice model the
        // installer does not carry, so it is unavailable on a fresh install and
        // cannot be the offline option a user is steered to.
        Assert.Equal(
            ["Native", "Piper", "Chatterbox-Nano", "ElevenLabs"],
            ComboValues(document, "TextToSpeechProviderBox"));
    }

    /// <summary>
    /// Two modes, not the four Setup used to open with. The four asked a new
    /// user to predict how they would want to work before they had used Metis
    /// at all. Metis is a learning tool now: there is no Autopilot, and no mode
    /// to pick.
    /// </summary>
    [Fact]
    public void Preferences_offers_no_mode_choice()
    {
        var document = LoadMarkup();

        Assert.DoesNotContain(document.Descendants(), element => NameOf(element) == "OperatingModeBox");
    }

    [Fact]
    public void Provider_fields_live_in_their_matching_capability_panels()
    {
        var document = LoadMarkup();

        AssertDescendsFrom(document, "GeminiApiKeyBox", "GeminiCard");
        AssertDescendsFrom(document, "ClaudeModelBox", "ClaudeCard");
        AssertDescendsFrom(document, "OpenRouterEndpointBox", "OpenRouterCard");
        AssertDescendsFrom(document, "OpenClawEndpointBox", "OpenClawCard");
        AssertDescendsFrom(document, "OllamaEndpointBox", "OllamaCard");
        AssertDescendsFrom(document, "LocalContextTokensBox", "OllamaCard");
        AssertDescendsFrom(document, "WhisperCppModelPathBox", "WhisperPanel");
        AssertDescendsFrom(document, "AssemblyAiModelBox", "AssemblyAiPanel");
        AssertDescendsFrom(document, "OpenAiTranscriptionModelBox", "NativeSttPanel");
        AssertDescendsFrom(document, "OpenAiSpeechModelBox", "NativeTtsPanel");
        AssertDescendsFrom(document, "PiperVoiceModelPathBox", "PiperPanel");
        AssertDescendsFrom(document, "ChatterboxEndpointBox", "ChatterboxPanel");
        AssertDescendsFrom(document, "ElevenLabsVoiceIdBox", "ElevenLabsPanel");
    }

    /// <summary>
    /// The gateway route has no key box, and that is the point of it: there is
    /// nothing to paste, because it runs on Metis's own provider account. A key
    /// field appearing here would be asking for a credential the route does not
    /// use.
    /// </summary>
    [Fact]
    public void The_managed_route_asks_for_no_credential()
    {
        var document = LoadMarkup();
        var card = FindNamed(document, "MetisCard");

        Assert.DoesNotContain(
            card.Descendants(),
            element => element.Name == Presentation + "PasswordBox");
    }

    /// <summary>
    /// The account page exists and carries the controls its code-behind reaches
    /// for by name.
    /// </summary>
    [Theory]
    [InlineData("PageAccount")]
    [InlineData("AccountPlanBadgeText")]
    [InlineData("AccountPlanTitle")]
    [InlineData("AccountUsageCard")]
    [InlineData("AccountUsageFill")]
    [InlineData("AccountFeatureList")]
    [InlineData("MetisProviderItem")]
    [InlineData("ProviderPlanNote")]
    public void The_account_page_carries_the_controls_the_code_behind_expects(string name) =>
        Assert.Contains(LoadMarkup().Descendants(), element => NameOf(element) == name);

    private static XDocument LoadMarkup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PreferencesWindow.xaml");
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
        ?? throw new Xunit.Sdk.XunitException($"PreferencesWindow.xaml has no element named {name}.");

    private static string? NameOf(XElement element) => (string?)element.Attribute(Xaml + "Name");
}
