using System.Xml.Linq;

namespace Metis.Tests;

public sealed class SetupWindowMarkupTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Setup_exposes_independent_reasoning_transcription_and_voice_selectors()
    {
        var document = LoadMarkup();

        Assert.Equal(
            ["Gemini", "OpenAI", "Claude", "OpenClaw", "Ollama", "Automatic"],
            ComboValues(document, "ProviderBox"));
        Assert.Equal(
            ["Native", "Whisper.cpp", "AssemblyAI"],
            ComboValues(document, "SpeechToTextProviderBox"));
        // "Windows" sits second because it is the offline voice that is always
        // present. Piper is offline too, but ships as a separate executable and
        // a voice model the installer does not carry, so it is unavailable on a
        // fresh install and cannot be the offline option a user is steered to.
        Assert.Equal(
            ["Native", "Piper", "Chatterbox-Nano", "ElevenLabs"],
            ComboValues(document, "TextToSpeechProviderBox"));
    }

    /// <summary>
    /// Two modes, not the four Setup used to open with. The four asked a new
    /// user to predict how they would want to work before they had used Metis
    /// at all; these two ask only whether Metis may touch the computer, which
    /// is a question anyone can answer on day one.
    /// </summary>
    [Fact]
    public void Setup_no_longer_offers_a_mode_choice()
    {
        var document = LoadMarkup();

        // Metis is a learning tool now: there is no Autopilot, and no mode to
        // pick. The mode picker is gone from Setup entirely.
        Assert.DoesNotContain(document.Descendants(), element => NameOf(element) == "OperatingModeBox");
    }

    [Fact]
    public void Provider_fields_live_in_their_matching_capability_panels()
    {
        var document = LoadMarkup();

        AssertDescendsFrom(document, "ClaudeReasoningModelBox", "ClaudeCard");
        AssertDescendsFrom(document, "OpenClawEndpointBox", "OpenClawCard");
        AssertDescendsFrom(document, "OllamaEndpointBox", "OllamaCard");
        AssertDescendsFrom(document, "LocalContextTokensBox", "OllamaCard");
        AssertDescendsFrom(document, "OpenAiTranscriptionModelBox", "NativeSpeechToTextCard");
        AssertDescendsFrom(document, "WhisperCppModelPathBox", "WhisperCppCard");
        AssertDescendsFrom(document, "AssemblyAiModelBox", "AssemblyAiCard");
        AssertDescendsFrom(document, "OpenAiSpeechModelBox", "NativeTextToSpeechCard");
        AssertDescendsFrom(document, "PiperVoiceModelPathBox", "PiperCard");
        AssertDescendsFrom(document, "ChatterboxEndpointBox", "ChatterboxNanoCard");
        AssertDescendsFrom(document, "ElevenLabsVoiceIdBox", "ElevenLabsCard");
    }

    private static XDocument LoadMarkup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SetupWindow.xaml");
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
        Assert.Contains(child.Ancestors(), element => NameOf(element) == ancestorName);
    }

    private static XElement FindNamed(XDocument document, string name) =>
        document.Descendants().Single(element => NameOf(element) == name);

    private static string? NameOf(XElement element) => (string?)element.Attribute(Xaml + "Name");
}
