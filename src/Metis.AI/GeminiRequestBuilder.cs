using System.Text.Json;
using Metis.Core.Models;

namespace Metis.AI;

public static class GeminiRequestBuilder
{
    // Inline data expands by roughly one third when base64 encoded. Keeping the
    // raw payload under 13 MiB leaves room below Gemini's 20 MiB inline-request
    // ceiling for JSON, the prompt, and response configuration.
    private const int MaxInlineRequestBytes = 13 * 1024 * 1024;
    public static string BuildGenerateContentJson(GeminiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("A prompt is required.", nameof(request));
        }

        var inlineBytes = (request.ScreenshotBytes?.Length ?? 0) + (request.RecordedAudioWav?.Length ?? 0);
        if (inlineBytes > MaxInlineRequestBytes)
        {
            throw new GeminiProviderException(
                GeminiErrorKind.InvalidRequest,
                "The screen and recording are too large to send inline. Try a shorter voice request or turn off active-window capture.");
        }

        var parts = new List<object>();
        parts.Add(new { text = ReasoningProviderSupport.BuildUserPrompt(request) });

        if (request.ScreenshotBytes is { Length: > 0 })
        {
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = ReasoningProviderSupport.NormalizeImageMimeType(request.ScreenshotMimeType),
                    data = Convert.ToBase64String(request.ScreenshotBytes)
                }
            });
        }

        if (request.RecordedAudioWav is { Length: > 0 })
        {
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = "audio/wav",
                    data = Convert.ToBase64String(request.RecordedAudioWav)
                }
            });
        }

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = ReasoningProviderSupport.BuildSystemInstruction(request)
                    }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = ReasoningProviderSupport.MaxPlanTokens,
                responseMimeType = "application/json",
                responseJsonSchema = ReasoningProviderSupport.AssistantPlanJsonSchema
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static readonly HashSet<string> ValidGeminiVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Puck", "Charon", "Kore", "Fenrir", "Aoede", "Zephyr", "Leda", "Orus",
        "Callirrhoe", "Autonoe", "Despina", "Erinome", "Helike", "Iapetus", "Thalassa", "Proteus"
    };

    public static string NormalizeVoice(string? voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName))
        {
            return "Kore";
        }

        var trimmed = voiceName.Trim();
        return ValidGeminiVoices.TryGetValue(trimmed, out var match) ? match : "Kore";
    }

    public static string BuildSpeechJson(string voiceName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Speech text cannot be empty.", nameof(text));
        }

        var normalizedVoice = NormalizeVoice(voiceName);
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = $"Speak this naturally and clearly:\n{text.Trim()}" } }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new
                    {
                        prebuiltVoiceConfig = new
                        {
                            voiceName = normalizedVoice
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
