using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

public sealed class AssistantPlanParserTests
{
    [Theory]
    [InlineData("click", DesktopActionKind.LeftClick)]
    [InlineData("leftclick", DesktopActionKind.LeftClick)]
    [InlineData("hover", DesktopActionKind.MovePointer)]
    [InlineData("doubleclick", DesktopActionKind.DoubleClick)]
    [InlineData("rightclick", DesktopActionKind.RightClick)]
    public void Parse_AcceptsCommonGeminiActionAliases(string actionType, DesktopActionKind expected)
    {
        var json = $$"""
            {"spoken_text":"Done","bubble_cue":null,"actions":[{"type":"{{actionType}}","x":500,"y":250}]}
            """;

        var plan = AssistantPlanParser.Parse(json, hasScreenshot: true, "Do the safe action");

        var action = Assert.Single(plan.Actions);
        Assert.Equal(expected, action.Kind);
    }

    [Fact]
    public void Parse_rejects_accessibility_target_without_mandatory_coordinates()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"spoken_text":"Minimizing it.","bubble_cue":"Minimize","actions":[{"type":"left_click","automation_id":"MinimizeButton","label":"Minimize"}]}
            """,
            hasScreenshot: true,
            "Minimize this window");

        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Parse_keeps_accessibility_id_when_coordinate_fallback_is_present()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"screen_observed":true,"spoken_text":"Minimizing it.","bubble_cue":"Minimize","actions":[{"type":"left_click","x":950,"y":25,"automation_id":"MinimizeButton","label":"Minimize"}]}
            """,
            hasScreenshot: true,
            "Minimize this window");

        var action = Assert.Single(plan.Actions);
        Assert.Equal("MinimizeButton", action.AutomationId);
        Assert.True(action.HasCoordinates);
        Assert.True(plan.ScreenObserved);
    }

    [Fact]
    public void Screen_observed_is_never_trusted_without_an_attached_screenshot()
    {
        const string json = """
            {"screen_observed":true,"spoken_text":"I see it.","bubble_cue":null,"actions":[]}
            """;

        Assert.True(AssistantPlanParser.Parse(json, hasScreenshot: true).ScreenObserved);
        Assert.False(AssistantPlanParser.Parse(json, hasScreenshot: false).ScreenObserved);
    }

    [Fact]
    public void Fenced_json_is_parsed_and_coordinates_are_clamped()
    {
        var plan = AssistantPlanParser.Parse(
            """
            ```json
            {
              "spoken_text": "The button is beside the address bar.",
              "bubble_cue": "Press here",
              "actions": [
                {"type":"move_pointer","x":1250,"y":-20,"label":"Settings"},
                {"type":"left_click","x":510.6,"y":200}
              ]
            }
            ```
            """,
            hasScreenshot: true);

        Assert.Equal("The button is beside the address bar.", plan.SpokenText);
        Assert.Equal("Press here", plan.BubbleCue);
        Assert.Collection(
            plan.Actions,
            action =>
            {
                Assert.Equal(DesktopActionKind.MovePointer, action.Kind);
                Assert.Equal(1000, action.NormalizedX);
                Assert.Equal(0, action.NormalizedY);
            },
            action =>
            {
                Assert.Equal(DesktopActionKind.LeftClick, action.Kind);
                Assert.Equal(511, action.NormalizedX);
                Assert.Equal(200, action.NormalizedY);
            });
    }

    [Fact]
    public void Pointer_and_click_actions_are_dropped_without_a_screenshot()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"spoken_text":"Waiting briefly.","bubble_cue":null,"actions":[
              {"type":"move_pointer","x":100,"y":200},
              {"type":"left_click","x":100,"y":200},
              {"type":"wait","delay_ms":25000}
            ]}
            """,
            hasScreenshot: false);

        var wait = Assert.Single(plan.Actions);
        Assert.Equal(DesktopActionKind.Wait, wait.Kind);
        Assert.Equal(10_000, wait.DelayMilliseconds);
    }

    [Fact]
    public void Parser_limits_actions_and_drops_unknown_or_incomplete_actions()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"spoken_text":"On it.","actions":[
              {"type":"unknown","x":1,"y":1},
              {"type":"left_click","x":1},
              {"type":"wait","delay_ms":1},
              {"type":"wait","delay_ms":2},
              {"type":"wait","delay_ms":3},
              {"type":"wait","delay_ms":4},
              {"type":"wait","delay_ms":5},
              {"type":"wait","delay_ms":6},
              {"type":"wait","delay_ms":7},
              {"type":"wait","delay_ms":8},
              {"type":"wait","delay_ms":9},
              {"type":"wait","delay_ms":10},
              {"type":"wait","delay_ms":11},
              {"type":"wait","delay_ms":12},
              {"type":"wait","delay_ms":13}
            ]}
            """,
            hasScreenshot: true);

        Assert.Equal(6, plan.Actions.Count);
        Assert.All(plan.Actions, action => Assert.Equal(DesktopActionKind.Wait, action.Kind));
    }

    [Fact]
    public void Parser_preserves_closed_loop_metadata_and_checkpoints()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {
              "plan_id":"open-browser-1",
              "replan_number":2,
              "goal":"Open the browser and search for Metis",
              "status":"continue",
              "screen_observed":true,
              "spoken_text":"The browser is open.",
              "bubble_cue":null,
              "actions":[
                {"type":"wait_for_element","id":"wait-address","automation_id":"AddressBar","timeout_ms":1500},
                {"type":"verify","id":"verify-browser","expected_state":"The browser address bar is visible"},
                {"type":"observe","id":"observe-next"}
              ]
            }
            """,
            hasScreenshot: true,
            userRequest: "Open the browser and search for Metis");

        Assert.Equal("open-browser-1", plan.PlanId);
        Assert.Equal(2, plan.ReplanNumber);
        Assert.Equal("continue", plan.Status);
        Assert.Equal("Open the browser and search for Metis", plan.Goal);
        Assert.Collection(
            plan.Actions,
            action =>
            {
                Assert.Equal(DesktopActionKind.WaitForElement, action.Kind);
                Assert.Equal("wait-address", action.Id);
                Assert.Equal("AddressBar", action.AutomationId);
                Assert.Equal(1500, action.TimeoutMilliseconds);
            },
            action =>
            {
                Assert.Equal(DesktopActionKind.Verify, action.Kind);
                Assert.Equal("The browser address bar is visible", action.ExpectedState);
            },
            action => Assert.Equal(DesktopActionKind.Observe, action.Kind));
    }

    [Fact]
    public void Parser_preserves_ordered_typing_and_navigation_plan()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"screen_observed":true,"spoken_text":"Opening it.","actions":[
              {"type":"open_app","text":"Notepad","label":"Open Notepad"},
              {"type":"wait","delay_ms":800},
              {"type":"type_text","text":"Hello Max","label":"Type greeting"},
              {"type":"key_press","key":"ctrl+a","label":"Select text"},
              {"type":"open_url","text":"https://example.com","label":"Open page"}
            ]}
            """,
            hasScreenshot: true,
            userRequest: "Open Notepad, type Hello Max, and navigate to example.com");

        Assert.Collection(
            plan.Actions,
            action =>
            {
                Assert.Equal(DesktopActionKind.OpenApp, action.Kind);
                Assert.Equal("Notepad", action.Text);
                Assert.False(action.HasCoordinates);
            },
            action => Assert.Equal(DesktopActionKind.Wait, action.Kind),
            action =>
            {
                Assert.Equal(DesktopActionKind.TypeText, action.Kind);
                Assert.Equal("Hello Max", action.Text);
            },
            action =>
            {
                Assert.Equal(DesktopActionKind.KeyPress, action.Kind);
                Assert.Equal("ctrl+a", action.Key);
            },
            action =>
            {
                Assert.Equal(DesktopActionKind.OpenUrl, action.Kind);
                Assert.Equal("https://example.com", action.Text);
            });
    }

    [Fact]
    public void Parser_rejects_unsafe_navigation_data()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"spoken_text":"No.","actions":[
              {"type":"open_url","text":"file:///C:/secret.txt"},
              {"type":"key_press","key":"ctrl+unknown"}
            ]}
            """,
            hasScreenshot: true,
            userRequest: "Open those targets");

        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Restricted_request_keeps_guidance_but_drops_clicks()
    {
        var plan = AssistantPlanParser.Parse(
            """
            {"spoken_text":"I can point it out, but I won't confirm a purchase.","bubble_cue":"Press here","actions":[
              {"type":"move_pointer","x":800,"y":900},
              {"type":"left_click","x":800,"y":900},
              {"type":"type_text","text":"card number"}
            ]}
            """,
            hasScreenshot: true,
            userRequest: "Buy this and click Pay now");

        var action = Assert.Single(plan.Actions);
        Assert.Equal(DesktopActionKind.MovePointer, action.Kind);
    }

    [Fact]
    public void Ordinary_text_falls_back_to_speech_only()
    {
        var plan = AssistantPlanParser.Parse("Here is the answer.", hasScreenshot: true);

        Assert.Equal("Here is the answer.", plan.SpokenText);
        Assert.Null(plan.BubbleCue);
        Assert.Empty(plan.Actions);
    }
}
