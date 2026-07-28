using Lulu.Core.Models;

namespace Lulu.Tests;

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
            ChatterboxVoice = " lulu "
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
        Assert.Equal("lulu", normalized.ChatterboxVoice);
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
}
