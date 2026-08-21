using Metis.AI;

namespace Metis.Tests;

/// <summary>
/// A reply that runs past the model's output limit arrives cut off mid-object,
/// and a half-written plan cannot be parsed. What Metis must never do in that
/// case is read the wreckage aloud: the user hears every brace, quote, and
/// colon spelled out, learns nothing, and sees no movement because no actions
/// survived. These cover the rescue path and the guarantee behind it.
/// </summary>
public sealed class TruncatedPlanTests
{
    /// <summary>
    /// The exact shape observed in the wild: the reply was cut off after
    /// plan_id, long before spoken_text was written.
    /// </summary>
    private const string TruncatedBeforeSpokenText =
        """
        {
          "screen_observed": true,
          "plan_id": "teach-claude-code",
        """;

    [Fact]
    public void A_reply_cut_off_before_the_answer_is_never_read_aloud()
    {
        var plan = AssistantPlanParser.Parse(TruncatedBeforeSpokenText, hasScreenshot: true);

        Assert.DoesNotContain("{", plan.SpokenText);
        Assert.DoesNotContain("\"", plan.SpokenText);
        Assert.DoesNotContain("plan_id", plan.SpokenText);
        Assert.DoesNotContain("screen_observed", plan.SpokenText);
    }

    [Fact]
    public void A_reply_cut_off_before_the_answer_says_something_useful_instead()
    {
        var plan = AssistantPlanParser.Parse(TruncatedBeforeSpokenText, hasScreenshot: true);

        Assert.False(string.IsNullOrWhiteSpace(plan.SpokenText));
        Assert.Contains("again", plan.SpokenText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_answer_is_rescued_when_the_reply_was_cut_off_after_it()
    {
        // The common case. spoken_text is written early, so a reply that runs
        // out of room part-way through a steps array still carries the sentence
        // Metis was going to say.
        var truncated =
            """
            {
              "plan_id": "export-a-wav",
              "spoken_text": "Open the File menu to start the export.",
              "steps": [{"instruction": "Click File", "why": "It holds the ex
            """;

        var plan = AssistantPlanParser.Parse(truncated, hasScreenshot: true);

        Assert.Equal("Open the File menu to start the export.", plan.SpokenText);
    }

    [Fact]
    public void A_rescued_answer_keeps_its_escaped_characters_readable()
    {
        var truncated =
            """
            {"spoken_text": "That’s the \"Export\" button.\nClick it next
            """;

        var plan = AssistantPlanParser.Parse(truncated, hasScreenshot: true);

        Assert.Contains("That’s", plan.SpokenText);
        Assert.Contains("\"Export\"", plan.SpokenText);
        Assert.DoesNotContain("\\u2019", plan.SpokenText);
        Assert.DoesNotContain("\\n", plan.SpokenText);
    }

    [Fact]
    public void A_plan_that_parses_normally_is_untouched_by_the_rescue_path()
    {
        var plan = AssistantPlanParser.Parse(
            """{"spoken_text":"Here is the save button.","bubble_cue":"Press here"}""",
            hasScreenshot: true);

        Assert.Equal("Here is the save button.", plan.SpokenText);
        Assert.Equal("Press here", plan.BubbleCue);
    }

    [Fact]
    public void Prose_is_still_spoken_exactly_as_the_model_wrote_it()
    {
        // The rescue must not fire on an ordinary spoken answer. A model that
        // replies in words has answered correctly.
        const string prose = "The save button is in the top left, next to the folder icon.";

        var plan = AssistantPlanParser.Parse(prose, hasScreenshot: true);

        Assert.Equal(prose, plan.SpokenText);
    }

    [Fact]
    public void Json_the_user_actually_asked_for_is_not_mistaken_for_a_broken_plan()
    {
        // Someone asking Metis about a config file should get it back, not a
        // message about the answer being cut off. Only Metis's own plan fields
        // mark a reply as internal plumbing.
        const string userJson = """{"name":"metis","version":2,"enabled":true}""";

        var plan = AssistantPlanParser.Parse(userJson, hasScreenshot: false);

        Assert.Equal(userJson, plan.SpokenText);
    }

    [Fact]
    public void A_truncated_reply_leaves_no_steps_behind()
    {
        var plan = AssistantPlanParser.Parse(TruncatedBeforeSpokenText, hasScreenshot: true);

        Assert.False(plan.HasAnnotation);
        Assert.Empty(plan.LessonSteps);
    }

    [Fact]
    public void An_empty_reply_does_not_produce_speech()
    {
        Assert.Equal(string.Empty, AssistantPlanParser.Parse("", hasScreenshot: true).SpokenText);
        Assert.Equal(string.Empty, AssistantPlanParser.Parse(null, hasScreenshot: true).SpokenText);
    }
}
