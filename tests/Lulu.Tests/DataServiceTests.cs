using Lulu.Core.Models;
using Lulu.Data;

namespace Lulu.Tests;

public sealed class DataServiceTests
{
    [Fact]
    public async Task Settings_round_trip_without_secret_fields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var store = new JsonSettingsStore(directory);
            var expected = new AppSettings
            {
                AiProvider = "OpenClaw",
                ReasoningModel = "gemini-2.5-flash",
                SpeechModel = "gemini-voice-model",
                VoiceName = "Aoede",
                OpenAiReasoningModel = "gpt-reasoning-model",
                OpenAiTranscriptionModel = "gpt-transcription-model",
                OpenAiSpeechModel = "gpt-speech-model",
                OpenAiVoiceName = "nova",
                ClaudeReasoningModel = "claude-reasoning-model",
                OpenClawEndpoint = "http://127.0.0.1:18789",
                OpenClawModel = "desktop-agent",
                OllamaEndpoint = "http://127.0.0.1:11434",
                OllamaModel = "local-vision-model",
                SpeechToTextProvider = "AssemblyAI",
                AssemblyAiModel = "universal-2",
                TextToSpeechProvider = "ElevenLabs",
                ElevenLabsModel = "eleven_multilingual_v2",
                ElevenLabsVoiceId = "voice-id",
                CompanionSize = 72,
                CursorDistance = 16
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();
            var json = await File.ReadAllTextAsync(store.SettingsPath);

            Assert.Equal(expected.ReasoningModel, actual.ReasoningModel);
            Assert.Equal(expected, actual);
            Assert.Equal(expected.CompanionSize, actual.CompanionSize);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Diagnostic_log_redacts_secret_shaped_values()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var log = new FileDiagnosticLog(directory);
            var fakeKey = "AQ." + new string('A', 32);

            log.Error($"request failed ?key={fakeKey} x-goog-api-key: {fakeKey}");
            var contents = File.ReadAllText(log.LogPath);

            Assert.DoesNotContain(fakeKey, contents, StringComparison.Ordinal);
            Assert.Contains("[redacted", contents, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lulu.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
