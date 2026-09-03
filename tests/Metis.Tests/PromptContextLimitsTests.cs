using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// How much of what Metis remembers travels into a turn it is paying for.
///
/// This is the enforceable half of the memory limit, and the distinction is the
/// whole point of the class. Memory lives in a JSON file on the user's own
/// machine; nothing on a server can stop somebody adding another entry to it,
/// and a client-side count is a courtesy rather than a rule. What a server can
/// see is how much of that memory arrives in the prompt — which is also the part
/// that costs money and the part that makes memory useful.
///
/// So the tests here are about a budget in characters, not a count of entries,
/// and about trimming rather than refusing.
/// </summary>
public sealed class PromptContextLimitsTests
{
    private static PlanLimits Plan(int memoryEntries) =>
        new(1m, 1_048_576, 3, 5, 0, 0, memoryEntries, ["gemini-2.5-flash-lite"], 120);

    private static readonly PlanLimits Free = Plan(50);
    private static readonly PlanLimits Plus = Plan(500);
    private static readonly PlanLimits Pro = Plan(5_000);

    [Fact]
    public void A_bigger_plan_carries_more_memory()
    {
        Assert.True(PromptContextLimits.RecallBudget(Plus) > PromptContextLimits.RecallBudget(Free));
        Assert.True(PromptContextLimits.RecallBudget(Pro) > PromptContextLimits.RecallBudget(Plus));
    }

    /// <summary>
    /// No plan ever ends up with no memory at all. A plan row with a mistaken
    /// zero in it should give somebody a little context, not make Metis appear
    /// to have forgotten who they are — which reads as the product being broken
    /// rather than as a limit.
    /// </summary>
    [Fact]
    public void No_plan_gets_nothing()
    {
        Assert.True(PromptContextLimits.RecallBudget(Plan(0)) > 0);
        Assert.True(PromptContextLimits.TurnBudget(Plan(0)) > 0);
    }

    /// <summary>
    /// And no plan gets an unbounded one. Pro's five thousand entries would be
    /// over a million characters — more than the model will read, and more than
    /// anyone should pay for in a single turn.
    /// </summary>
    [Fact]
    public void No_plan_gets_everything() =>
        Assert.True(PromptContextLimits.RecallBudget(Plan(1_000_000)) < 200_000);

    /// <summary>
    /// Text inside the budget is returned exactly as it was. A trim that
    /// rewrote short input would be corrupting the common case to enforce the
    /// rare one.
    /// </summary>
    [Fact]
    public void Short_context_is_untouched()
    {
        const string recall = "They prefer short answers.\nThey use a Norwegian keyboard.";

        Assert.Equal(recall, PromptContextLimits.Trim(recall, 5_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_stays_nothing(string? text) =>
        Assert.Equal(text, PromptContextLimits.Trim(text, 1_000));

    /// <summary>
    /// The end is kept, not the beginning.
    ///
    /// Recall and turn history are both written oldest-first, so keeping the
    /// front of the text would hand the model a user's oldest facts and drop
    /// everything that had happened since — the exact opposite of useful.
    /// </summary>
    [Fact]
    public void The_recent_end_is_what_survives()
    {
        var text = string.Join('\n', Enumerable.Range(0, 400).Select(i => $"fact {i}"));

        var trimmed = PromptContextLimits.Trim(text, 600)!;

        Assert.Contains("fact 399", trimmed);
        Assert.DoesNotContain("fact 0\n", trimmed);
    }

    /// <summary>
    /// The model is told history was dropped rather than simply handed less of
    /// it. Without the marker it reads a sentence starting mid-thought as
    /// something the user actually said.
    /// </summary>
    [Fact]
    public void The_model_is_told_that_something_was_dropped()
    {
        var trimmed = PromptContextLimits.Trim(new string('x', 10_000), 1_000)!;

        Assert.Contains("not included in this plan", trimmed);
    }

    [Fact]
    public void The_result_never_exceeds_the_budget()
    {
        foreach (var budget in new[] { 300, 1_000, 5_000, 50_000 })
        {
            var trimmed = PromptContextLimits.Trim(new string('y', 200_000), budget);
            Assert.True(trimmed!.Length <= budget, $"budget {budget} produced {trimmed.Length}");
        }
    }

    /// <summary>
    /// A budget too small to hold anything meaningful returns the marker alone
    /// rather than the marker plus a fragment, which would read to the model as
    /// though those few words were the whole of the user's history.
    /// </summary>
    [Fact]
    public void An_impossible_budget_returns_only_the_notice()
    {
        var trimmed = PromptContextLimits.Trim(new string('z', 10_000), 60)!;

        Assert.Contains("not included in this plan", trimmed);
        Assert.DoesNotContain("z", trimmed);
    }

    /// <summary>
    /// The conversation is trimmed harder than the memory. They are different
    /// things: recall is what Metis knows about someone across sessions, turns
    /// are what was just said — and the conversation is the cheaper of the two
    /// to lose, because the user can say it again.
    /// </summary>
    [Fact]
    public void The_conversation_is_trimmed_before_the_memory() =>
        Assert.True(PromptContextLimits.TurnBudget(Pro) < PromptContextLimits.RecallBudget(Pro));

    /// <summary>
    /// Applying both at once is the same as applying each, which is what the
    /// gateway relies on when it does this in one call.
    /// </summary>
    [Fact]
    public void Applying_both_matches_applying_each()
    {
        var recall = new string('a', 500_000);
        var turns = new string('b', 500_000);

        var (trimmedRecall, trimmedTurns) = PromptContextLimits.Apply(recall, turns, Free);

        Assert.Equal(PromptContextLimits.Trim(recall, PromptContextLimits.RecallBudget(Free)), trimmedRecall);
        Assert.Equal(PromptContextLimits.Trim(turns, PromptContextLimits.TurnBudget(Free)), trimmedTurns);
    }

    /// <summary>
    /// Free really is limited, which is the decision this exists to carry out —
    /// while still being a usable amount rather than a token gesture.
    /// </summary>
    [Fact]
    public void Free_is_limited_but_workable()
    {
        var budget = PromptContextLimits.RecallBudget(Free);

        Assert.InRange(budget, 2_000, 20_000);
    }
}
