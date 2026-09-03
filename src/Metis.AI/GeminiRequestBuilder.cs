using System.Text.Json;
using System.Text.Json.Nodes;
using Metis.Core.Models;

namespace Metis.AI;

public static class GeminiRequestBuilder
{
    // Inline data expands by roughly one third when base64 encoded. Keeping the
    // raw payload under 13 MiB leaves room below Gemini's 20 MiB inline-request
    // ceiling for JSON, the prompt, and response configuration.
    private const int MaxInlineRequestBytes = 13 * 1024 * 1024;

    public static string BuildGenerateContentJson(GeminiRequest request) =>
        BuildGenerateContentJson(request, model: null);

    public static string BuildGenerateContentJson(GeminiRequest request, string? model)
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

        var json = JsonSerializer.SerializeToNode(payload, SerializerOptions)!;
        var generationConfig = json["generationConfig"]!.AsObject();

        // Tell Gemini which field to write first. Without this the order is not
        // promised, and the whole point of streaming the reply is lost if the
        // sentence turns up after a twelve-step lesson array.
        generationConfig["responseJsonSchema"]!.AsObject()["propertyOrdering"] =
            new JsonArray([.. ReasoningProviderSupport.AssistantPlanPropertyOrder.Select(name => JsonValue.Create(name))]);

        if (BuildThinkingConfig(model, request.AcademicTeaching) is { } thinking)
        {
            generationConfig["thinkingConfig"] = thinking;
        }

        return json.ToJsonString(SerializerOptions);
    }

    /// <summary>
    /// How hard to let the model think before answering.
    ///
    /// Nothing was sent here before, which meant every reply paid for whatever
    /// the model chose on its own — and the flash models choose to think by
    /// default. That thinking is invisible: it produces no text, so the user
    /// watches an empty panel for the whole of it. Metis asks a narrow question
    /// against a screenshot it has already attached, which is not the kind of
    /// question that repays deliberation.
    ///
    /// Drawing a lesson is the exception, so an academic turn is left on the
    /// model's own judgement.
    ///
    /// The field differs by generation and an unrecognised one is rejected
    /// outright, so anything not positively known is left alone.
    /// </summary>
    private static JsonNode? BuildThinkingConfig(string? model, bool academicTeaching)
    {
        if (academicTeaching || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var normalized = model.Trim().ToLowerInvariant();
        if (normalized.StartsWith("gemini-3", StringComparison.Ordinal))
        {
            return new JsonObject { ["thinkingLevel"] = "low" };
        }

        if (normalized.StartsWith("gemini-2.5-flash", StringComparison.Ordinal))
        {
            return new JsonObject { ["thinkingBudget"] = 0 };
        }

        return null;
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
