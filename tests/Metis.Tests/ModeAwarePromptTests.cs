using System.Text.Json;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// The intent Metis read from the user's words has to survive all the way into
/// the provider payload, otherwise the prompt and the action filter would
/// disagree about what Metis is allowed to do.
/// </summary>
public sealed class ModeAwarePromptTests
{
    [Theory]
    [InlineData(OperatingMode.Learn, "TEACH")]
    [InlineData(OperatingMode.Guide, "TEACH")]
    [InlineData(OperatingMode.Assist, "TAKE CONTROL")]
    [InlineData(OperatingMode.Autopilot, "TAKE CONTROL")]
    public void The_system_instruction_carries_the_detected_intent(OperatingMode mode, string expected)
    {
        var instruction = SystemInstructionFor(new GeminiRequest("Help me", [1, 2, 3], Mode: mode));

        Assert.Contains($"assistance_intent: {expected}", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void Teaching_tells_the_provider_not_to_act_even_when_asked()
    {
        var instruction = SystemInstructionFor(
            new GeminiRequest("Click export for me", [1, 2, 3], Mode: OperatingMode.Learn));

        Assert.Contains("Do not click, type, press keys", instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// The model is asked what it is pointing at and never what shape to draw.
    /// Asking for a shape is what produced a ring around everything.
    /// </summary>
    [Fact]
    public void The_instruction_asks_for_a_scope_rather_than_a_shape()
    {
        var instruction = SystemInstructionFor(new GeminiRequest("Where is save?", [1, 2, 3]));

        Assert.Contains("\"scope\"", instruction, StringComparison.Ordinal);
        Assert.Contains("Do not ask for a shape", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inspect_activation_adds_the_pointer_target_rules()
    {
        var instruction = SystemInstructionFor(new GeminiRequest(
            "What is this?",
            [1, 2, 3],
            Activation: ActivationKind.Inspect));

        Assert.Contains("activation: INSPECT", instruction, StringComparison.Ordinal);
        Assert.Contains("Resolve \"this\"", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_activation_leaves_out_the_inspect_rules()
    {
        var instruction = SystemInstructionFor(new GeminiRequest(
            "What is on screen?",
            [1, 2, 3],
            Activation: ActivationKind.Context));

        Assert.DoesNotContain("activation: INSPECT", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_carries_the_pointer_target_task_and_skills()
    {
        var prompt = UserPromptFor(new GeminiRequest(
            "What does this do?",
            [1, 2, 3],
            ActiveWindowTitle: "FL Studio",
            ScreenshotWidth: 1600,
            ScreenshotHeight: 900,
            ScreenshotSourceWidth: 1600,
            ScreenshotSourceHeight: 900,
            Mode: OperatingMode.Learn,
            Activation: ActivationKind.Inspect,
            Pointer: new PointerContext(820, 460, 512, 511, "Button \"Add reverb\""),
            TaskContext: "goal: mix the vocal",
            SkillContext: "FL Studio/Mixer routing: Advanced"));

        Assert.Contains("activation: inspect", prompt, StringComparison.Ordinal);
        Assert.Contains("mode: learn", prompt, StringComparison.Ordinal);
        Assert.Contains("pointer_position: x=512, y=511", prompt, StringComparison.Ordinal);
        Assert.Contains("pointer_target: Button \"Add reverb\"", prompt, StringComparison.Ordinal);
        Assert.Contains("goal: mix the vocal", prompt, StringComparison.Ordinal);
        Assert.Contains("FL Studio/Mixer routing: Advanced", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_without_pointer_task_or_skills_stays_clean()
    {
        var prompt = UserPromptFor(new GeminiRequest("Hello", [1, 2, 3]));

        Assert.DoesNotContain("pointer_position", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ongoing_task", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("user_skills", prompt, StringComparison.Ordinal);
    }

    private static string SystemInstructionFor(GeminiRequest request)
    {
        using var document = JsonDocument.Parse(GeminiRequestBuilder.BuildGenerateContentJson(request));
        return document.RootElement
            .GetProperty("systemInstruction")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private static string UserPromptFor(GeminiRequest request)
    {
        using var document = JsonDocument.Parse(GeminiRequestBuilder.BuildGenerateContentJson(request));
        return document.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }
}
