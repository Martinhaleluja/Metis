using System.Text.Json;
using Metis.AI;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Windows;

namespace Metis.Tests;

/// <summary>
/// What a turn costs before the model has done anything: how hard it is asked
/// to think, and how large a picture of the screen it is handed.
/// </summary>
public sealed class TurnCostTests
{
    private static JsonElement GenerationConfig(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("generationConfig").Clone();
    }

    [Fact]
    public void An_ordinary_question_asks_for_a_quick_answer()
    {
        var config = GenerationConfig(GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("Where is the save button?"),
            "gemini-3.5-flash"));

        Assert.Equal("low", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    /// <summary>
    /// Drawing a lesson is the one job worth deliberating over, so an academic
    /// turn is left on the model's own judgement rather than being hurried.
    /// </summary>
    [Fact]
    public void Drawing_a_lesson_is_left_to_think()
    {
        var config = GenerationConfig(GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("Explain a triangle") { AcademicTeaching = true },
            "gemini-3.5-flash"));

        Assert.False(config.TryGetProperty("thinkingConfig", out _));
    }

    /// <summary>
    /// The field differs by model generation and an unrecognised one is
    /// rejected outright, so a model Metis does not recognise is left alone
    /// rather than guessed at.
    /// </summary>
    [Fact]
    public void An_unfamiliar_model_is_not_told_how_to_think()
    {
        var config = GenerationConfig(GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("Hello"),
            "some-future-model"));

        Assert.False(config.TryGetProperty("thinkingConfig", out _));
    }

    [Fact]
    public async Task A_screen_question_is_sent_a_smaller_picture_than_a_pointing_one()
    {
        // A desktop far larger than either ceiling, so both profiles scale it.
        var capture = new VirtualDesktopCaptureService(
            () => new System.Drawing.Rectangle(0, 0, 3840, 2160));

        var standard = await capture.CaptureActiveWindowAsync(ScreenCaptureDetail.Standard);
        var full = await capture.CaptureActiveWindowAsync(ScreenCaptureDetail.Full);

        Assert.NotNull(standard);
        Assert.NotNull(full);

        Assert.True(standard!.Width <= 1280, $"standard capture was {standard.Width}px wide");
        Assert.True(full!.Width > standard.Width, "pointing should keep more detail than an ordinary question");

        // Both still describe the same desktop, so coordinates the model
        // returns mean the same thing either way.
        Assert.Equal(3840, standard.SourceWidth);
        Assert.Equal(3840, full.SourceWidth);
    }

    [Fact]
    public void The_bounds_a_capture_will_use_can_be_read_before_taking_it()
    {
        var capture = new VirtualDesktopCaptureService(
            () => new System.Drawing.Rectangle(-1920, 0, 3840, 1080));

        var bounds = capture.PeekCaptureBounds();

        Assert.NotNull(bounds);
        Assert.Equal(-1920, bounds!.Left);
        Assert.Equal(3840, bounds.Width);
        Assert.Equal(1080, bounds.Height);
    }
}
