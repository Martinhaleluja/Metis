using System.Text.Json;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class GeminiRequestBuilderTests
{
    /// <summary>
    /// Gemini rejects the entire request once the response schema grows past a
    /// complexity budget it does not document, and it says only "Request
    /// contains an invalid argument" when it does. Adding the lesson steps
    /// crossed that line, and every voice request failed until the two maxItems
    /// keywords came out.
    ///
    /// They were never load-bearing: AssistantPlanParser caps steps and actions
    /// itself while reading, which is the limit that actually protects Metis.
    /// This pins the rule, because the schema is shared with four other
    /// providers and the temptation to restate a parser limit in it is
    /// permanent.
    /// </summary>
    [Fact]
    public void The_response_schema_states_no_array_length_limits()
    {
        var json = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("What is on screen?", [1, 2, 3]));

        Assert.DoesNotContain("maxItems", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minItems", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The annotation fields have to reach the provider, or the model is being
    /// asked in the instruction for something the schema forbids it to return.
    /// </summary>
    [Fact]
    public void The_response_schema_carries_the_annotation_and_lesson_fields()
    {
        var json = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("Where is save?", [1, 2, 3]));

        using var document = JsonDocument.Parse(json);
        var schema = document.RootElement
            .GetProperty("generationConfig")
            .GetProperty("responseJsonSchema");
        var properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("scope", out _));
        Assert.True(properties.TryGetProperty("element", out _));
        Assert.True(properties.TryGetProperty("annotation_text", out _));

        var stepProperties = properties.GetProperty("steps").GetProperty("items").GetProperty("properties");
        foreach (var field in new[] { "instruction", "done_when", "scope", "x", "y", "w", "h", "element", "text" })
        {
            Assert.True(stepProperties.TryGetProperty(field, out _), $"steps is missing '{field}'");
        }

        // Metis no longer operates the computer, so the schema carries no
        // actions at all — only what to say, where to mark, and the steps.
        Assert.False(properties.TryGetProperty("actions", out _));
    }

    [Fact]
    public void Generate_content_payload_contains_text_image_and_audio_parts()
    {
        var json = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest(
                "What is on screen?",
                [1, 2, 3],
                [4, 5, 6],
                "Calculator",
                ScreenshotMimeType: "image/jpeg",
                ScreenshotWidth: 1600,
                ScreenshotHeight: 900,
                ScreenshotScreenLeft: -1920,
                ScreenshotScreenTop: 0,
                ScreenshotSourceWidth: 3840,
                ScreenshotSourceHeight: 1080));

        using var document = JsonDocument.Parse(json);
        var parts = document.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts");

        Assert.Equal(3, parts.GetArrayLength());
        Assert.Contains("Calculator", parts[0].GetProperty("text").GetString());
        Assert.Contains("1600x900", parts[0].GetProperty("text").GetString());
        Assert.Contains("complete_windows_virtual_desktop_all_monitors", parts[0].GetProperty("text").GetString());
        Assert.Contains("left=-1920, top=0, width=3840, height=1080", parts[0].GetProperty("text").GetString());
        Assert.Contains("normalized 0-1000", parts[0].GetProperty("text").GetString());
        Assert.Equal("image/jpeg", parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.Equal("audio/wav", parts[2].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.Equal(
            "application/json",
            document.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        Assert.Equal(
            "object",
            document.RootElement
                .GetProperty("generationConfig")
                .GetProperty("responseJsonSchema")
                .GetProperty("type")
                .GetString());

        var instruction = document.RootElement
            .GetProperty("systemInstruction")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        Assert.Contains("spoken_text", instruction, StringComparison.Ordinal);
        Assert.Contains("screen_observed", instruction, StringComparison.Ordinal);
        Assert.Contains("steps", instruction, StringComparison.Ordinal);

        // Metis is a learning tool: the instruction must never ask the model to
        // operate the computer. If any of these reappear, control has crept back.
        Assert.DoesNotContain("type_text", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("open_app", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("left_click", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void Speech_payload_requests_audio_and_selected_voice()
    {
        var json = GeminiRequestBuilder.BuildSpeechJson("Kore", "Hello Max");

        Assert.Contains("AUDIO", json);
        Assert.Contains("Kore", json);
        Assert.Contains("Hello Max", json);
    }

}
