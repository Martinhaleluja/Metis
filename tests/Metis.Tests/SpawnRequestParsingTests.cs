using Metis.AI;

namespace Metis.Tests;

/// <summary>
/// Every goal read here becomes a background worker that can write files and
/// run commands, so what the parser will and will not accept is a safety
/// boundary rather than a convenience.
/// </summary>
public sealed class SpawnRequestParsingTests
{
    [Fact]
    public void A_reply_with_no_spawn_field_asks_for_nothing()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"It is the blue one.","screen_observed":false}""",
            hasScreenshot: false);

        Assert.Empty(plan.AgentsToSpawn);
    }

    [Fact]
    public void An_empty_list_asks_for_nothing()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Nothing to do.","spawn_agents":[]}""",
            hasScreenshot: false);

        Assert.Empty(plan.AgentsToSpawn);
    }

    [Fact]
    public void A_null_list_asks_for_nothing()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Sure.","spawn_agents":null}""", hasScreenshot: false);

        Assert.Empty(plan.AgentsToSpawn);
    }

    [Fact]
    public void One_goal_is_read()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"On it.","spawn_agents":["Tidy the Downloads folder"]}""",
            hasScreenshot: false);

        var goal = Assert.Single(plan.AgentsToSpawn);
        Assert.Equal("Tidy the Downloads folder", goal);
    }

    [Fact]
    public void Several_goals_become_several_agents()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Two of them.","spawn_agents":["Tidy Downloads","Research WPF animation"]}""",
            hasScreenshot: false);

        Assert.Equal(2, plan.AgentsToSpawn.Count);
        Assert.Equal("Research WPF animation", plan.AgentsToSpawn[1]);
    }

    [Fact]
    public void Objects_carrying_a_goal_are_accepted_as_well_as_bare_strings()
    {
        // Models produce both shapes. Losing a request over the difference
        // would be a worse outcome than reading either.
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Ok.","spawn_agents":[{"goal":"Tidy Downloads"},"Research WPF"]}""",
            hasScreenshot: false);

        Assert.Equal(2, plan.AgentsToSpawn.Count);
        Assert.Equal("Tidy Downloads", plan.AgentsToSpawn[0]);
    }

    [Fact]
    public void Blank_and_unreadable_entries_are_dropped_rather_than_spawned()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Ok.","spawn_agents":["","   ",null,42,{"unrelated":"x"},"Real goal"]}""",
            hasScreenshot: false);

        var goal = Assert.Single(plan.AgentsToSpawn);
        Assert.Equal("Real goal", goal);
    }

    [Fact]
    public void The_number_of_agents_one_reply_can_ask_for_is_capped()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Lots.","spawn_agents":["a","b","c","d","e","f","g","h"]}""",
            hasScreenshot: false);

        Assert.Equal(4, plan.AgentsToSpawn.Count);
    }

    [Fact]
    public void A_spawn_request_does_not_need_a_screenshot()
    {
        // Handing over a job is not a claim about the screen, so the
        // screenshot-grounding rules that govern annotations must not eat it.
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"On it.","screen_observed":false,"spawn_agents":["Tidy Downloads"]}""",
            hasScreenshot: false);

        Assert.Single(plan.AgentsToSpawn);
    }

    [Fact]
    public void A_second_look_needs_both_the_flag_and_something_to_look_for()
    {
        var vague = AssistantPlanParser.Parse(
            """{"spoken_text":"Hmm.","needs_another_look":true,"look_for":null}""",
            hasScreenshot: true);
        Assert.False(vague.WantsSecondLook);

        var named = AssistantPlanParser.Parse(
            """{"spoken_text":"Hmm.","needs_another_look":true,"look_for":"Record button"}""",
            hasScreenshot: true);
        Assert.True(named.WantsSecondLook);
        Assert.Equal("Record button", named.LookFor);
    }

    [Fact]
    public void An_ordinary_reply_does_not_ask_to_look_again()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"It's the blue one, top left."}""", hasScreenshot: true);

        Assert.False(plan.WantsSecondLook);
    }
}
