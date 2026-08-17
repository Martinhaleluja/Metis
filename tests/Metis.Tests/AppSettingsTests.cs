using Metis.Core.Models;

namespace Metis.Tests;

public sealed class AppSettingsTests
{
    [Theory]
    [InlineData("gemini", "Gemini")]
    [InlineData(" OPENAI ", "OpenAI")]
    [InlineData("open-ai", "OpenAI")]
    [InlineData("anthropic", "Claude")]
    [InlineData("openclaw", "OpenClaw")]
    [InlineData("open claw", "OpenClaw")]
    [InlineData("ollama", "Ollama")]
    [InlineData("auto", "Automatic")]
    [InlineData("unknown", "Gemini")]
    public void Normalize_canonicalizes_reasoning_provider(string input, string expected)
    {
        var normalized = new AppSettings { AiProvider = input }.Normalize();

        Assert.Equal(expected, normalized.AiProvider);
    }

    [Fact]
    public void Normalize_clamps_companion_values_and_strips_model_prefix()
    {
        var settings = new AppSettings
        {
            AiProvider = " auto ",
            ReasoningModel = " models/gemini-3.5-flash ",
            OpenAiReasoningModel = " gpt-5-mini ",
            CompanionSize = 500,
            CursorDistance = -5,
            VoiceName = " "
        };

        var normalized = settings.Normalize();

        Assert.Equal("gemini-3.5-flash", normalized.ReasoningModel);
        Assert.Equal("Automatic", normalized.AiProvider);
        Assert.Equal("gpt-5-mini", normalized.OpenAiReasoningModel);
        Assert.Equal(112, normalized.CompanionSize);
        Assert.Equal(0, normalized.CursorDistance);
        Assert.Equal("Kore", normalized.VoiceName);
    }

    [Fact]
    public void Normalize_canonicalizes_independent_speech_providers_and_provider_fields()
    {
        var settings = new AppSettings
        {
            ClaudeReasoningModel = " models/claude-test ",
            OpenClawEndpoint = " http://127.0.0.1:18789/ ",
            OpenClawModel = " agent-main ",
            OllamaEndpoint = "http://127.0.0.1:11434/",
            OllamaModel = " qwen-vision ",
            LocalContextTokens = 9000,
            SpeechToTextProvider = " whisper cpp ",
            AssemblyAiModel = " universal-2 ",
            WhisperCppExecutablePath = "  whisper-cli.exe  ",
            WhisperCppModelPath = " ggml-tiny.bin ",
            TextToSpeechProvider = " chatterbox nano ",
            ElevenLabsModel = " eleven_multilingual_v2 ",
            ElevenLabsVoiceId = " voice-id ",
            ChatterboxEndpoint = "http://127.0.0.1:4123/v1/",
            ChatterboxModel = " chatterbox-nano ",
            ChatterboxVoice = " metis "
        };

        var normalized = settings.Normalize();

        Assert.Equal("claude-test", normalized.ClaudeReasoningModel);
        Assert.Equal("http://127.0.0.1:18789", normalized.OpenClawEndpoint);
        Assert.Equal("agent-main", normalized.OpenClawModel);
        Assert.Equal("http://127.0.0.1:11434", normalized.OllamaEndpoint);
        Assert.Equal("qwen-vision", normalized.OllamaModel);
        Assert.Equal(4096, normalized.LocalContextTokens);
        Assert.Equal("Whisper.cpp", normalized.SpeechToTextProvider);
        Assert.Equal("universal-2", normalized.AssemblyAiModel);
        Assert.Equal("whisper-cli.exe", normalized.WhisperCppExecutablePath);
        Assert.Equal("ggml-tiny.bin", normalized.WhisperCppModelPath);
        Assert.Equal("Chatterbox-Nano", normalized.TextToSpeechProvider);
        Assert.Equal("eleven_multilingual_v2", normalized.ElevenLabsModel);
        Assert.Equal("voice-id", normalized.ElevenLabsVoiceId);
        Assert.Equal("http://127.0.0.1:4123/v1", normalized.ChatterboxEndpoint);
        Assert.Equal("chatterbox-nano", normalized.ChatterboxModel);
        Assert.Equal("metis", normalized.ChatterboxVoice);
    }

    [Fact]
    public void Normalize_replaces_invalid_local_endpoints_with_safe_defaults()
    {
        var normalized = new AppSettings
        {
            OpenClawEndpoint = "file:///tmp/openclaw",
            OllamaEndpoint = "not-an-address"
        }.Normalize();

        Assert.Equal("http://127.0.0.1:18789", normalized.OpenClawEndpoint);
        Assert.Equal("http://127.0.0.1:11434", normalized.OllamaEndpoint);
    }

    [Theory]
    [InlineData("openrouter", "OpenRouter")]
    [InlineData(" OPEN ROUTER ", "OpenRouter")]
    [InlineData("open-router", "OpenRouter")]
    public void Normalize_canonicalizes_openrouter(string input, string expected)
    {
        var normalized = new AppSettings { AiProvider = input }.Normalize();

        Assert.Equal(expected, normalized.AiProvider);
    }

    [Fact]
    public void Normalize_repairs_openrouter_endpoint_and_model()
    {
        var normalized = new AppSettings
        {
            OpenRouterEndpoint = "not-an-address",
            OpenRouterModel = "  models/qwen/qwen-2.5-vl-72b-instruct:free  "
        }.Normalize();

        Assert.Equal("https://openrouter.ai/api", normalized.OpenRouterEndpoint);
        Assert.Equal("qwen/qwen-2.5-vl-72b-instruct:free", normalized.OpenRouterModel);
    }

    /// <summary>
    /// Metis reasons about a screenshot every turn, so the shipped default has
    /// to be a model that accepts images.
    /// </summary>
    [Fact]
    public void The_default_openrouter_model_is_a_free_vision_model()
    {
        var settings = new AppSettings();

        Assert.EndsWith(":free", settings.OpenRouterModel, StringComparison.Ordinal);
        Assert.Equal("https://openrouter.ai/api", settings.OpenRouterEndpoint);
    }

    [Theory]
    [InlineData("System", "System")]
    [InlineData("light", "Light")]
    [InlineData(" DARK ", "Dark")]
    [InlineData("solarized", "System")]
    [InlineData("", "System")]
    public void Normalize_canonicalizes_theme_preference(string input, string expected)
    {
        var normalized = new AppSettings { ThemePreference = input }.Normalize();

        Assert.Equal(expected, normalized.ThemePreference);
    }

    /// <summary>
    /// The property is declared non-nullable, but a hand-edited or truncated
    /// settings.json can still deserialise an explicit null into it, which is
    /// why the normaliser accepts one.
    /// </summary>
    [Fact]
    public void Normalize_treats_a_null_theme_preference_as_following_windows()
    {
        var normalized = new AppSettings { ThemePreference = null! }.Normalize();

        Assert.Equal("System", normalized.ThemePreference);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Normalize_repairs_an_impossible_settings_version(int input, int expected)
    {
        var normalized = new AppSettings { SettingsVersion = input }.Normalize();

        Assert.Equal(expected, normalized.SettingsVersion);
    }

    [Fact]
    public void A_fresh_install_follows_windows_and_has_not_seen_onboarding()
    {
        var settings = new AppSettings();

        Assert.Equal(1, settings.SettingsVersion);
        Assert.Equal("System", settings.ThemePreference);
        Assert.False(settings.OnboardingCompleted);
        Assert.False(settings.ReduceMotion);
    }

    /// <summary>
    /// Onboarding completion has to survive a save/load round trip or the
    /// wizard reappears on every launch, which is the bug this flag exists to
    /// fix.
    /// </summary>
    [Fact]
    public void Normalize_preserves_the_onboarding_and_motion_flags()
    {
        var normalized = new AppSettings
        {
            OnboardingCompleted = true,
            ReduceMotion = true
        }.Normalize();

        Assert.True(normalized.OnboardingCompleted);
        Assert.True(normalized.ReduceMotion);
    }
}
