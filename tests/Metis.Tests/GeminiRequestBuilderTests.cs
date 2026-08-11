using System.Text.Json;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class GeminiRequestBuilderTests
{
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
        Assert.Contains("Coordinates are mandatory", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at most 6", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("type_text", instruction, StringComparison.Ordinal);
        Assert.Contains("open_app", instruction, StringComparison.Ordinal);
        Assert.Contains("no screenshot", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never autonomously purchase", instruction, StringComparison.OrdinalIgnoreCase);
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
