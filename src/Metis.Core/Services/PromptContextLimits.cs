using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// How much remembered context a plan may carry into one managed turn.
///
/// Memory is the hardest of the plan limits to make real, and it is worth being
/// precise about why. Metis's memory lives in a JSON file on the user's own
/// computer. Nothing on a server can stop somebody adding a ten-thousandth
/// entry to a local file, and a client-side count is a courtesy that anyone who
/// wants to can remove — so selling "500 memories" as though it were enforced
/// would be selling a number the code does not keep.
///
/// What the server can see, and therefore what it can actually enforce, is how
/// much of that memory arrives in the prompt. Recall and recent turns travel as
/// text on every managed request, they are the part that costs money, and they
/// are the part that makes memory useful at all — a memory that never reaches
/// the model does nothing. So the limit is expressed here, in characters of
/// context, applied on the gateway to requests Metis is paying for.
///
/// Three consequences worth stating, because each one is a deliberate choice
/// rather than an oversight:
///
/// A local model and a Pro account's own key never come through here. Metis is
/// not paying for those requests, so it has no business trimming them, and a
/// user on either route keeps the whole of their memory.
///
/// The trim keeps the end of the text rather than the beginning. Recall and
/// turn history are both written oldest-first, and the recent end is the part
/// that makes the next answer good.
///
/// And it trims rather than refuses. Somebody who has accumulated a lot of
/// history should get a slightly less well-informed answer, not an error — a
/// refusal here would make the product stop working the longer it was used,
/// which is precisely backwards.
/// </summary>
public static class PromptContextLimits
{
    /// <summary>
    /// Characters of context allowed per memory entry the plan includes.
    ///
    /// Roughly a short paragraph. This is the bridge between the number the
    /// pricing page can honestly quote — entries — and the thing the gateway
    /// can actually measure.
    /// </summary>
    private const int CharactersPerEntry = 220;

    /// <summary>
    /// The floor, so that no plan ever ends up with no memory at all.
    ///
    /// A plan row with a mistaken zero in it should degrade to "a little
    /// context" rather than to "Metis has forgotten who you are", which is the
    /// sort of failure that reads as the product being broken.
    /// </summary>
    private const int MinimumCharacters = 2_000;

    /// <summary>
    /// The ceiling, whatever the plan says.
    ///
    /// Pro's five thousand entries would be over a million characters, which is
    /// more than any model here will read and more than anyone should pay for
    /// in one turn. The cap is on cost as much as on context.
    /// </summary>
    private const int MaximumCharacters = 120_000;

    /// <summary>How much remembered context this plan may send in one turn.</summary>
    public static int RecallBudget(PlanLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var budget = (long)limits.MemoryEntriesMax * CharactersPerEntry;
        return (int)Math.Clamp(budget, MinimumCharacters, MaximumCharacters);
    }

    /// <summary>
    /// How much of the current conversation may travel with the question.
    ///
    /// Half the recall budget. The two are different things — recall is what
    /// Metis remembers about the user across sessions, turns are what was just
    /// said — and the conversation is the cheaper of the two to lose, because
    /// the user can simply say it again.
    /// </summary>
    public static int TurnBudget(PlanLimits limits) =>
        Math.Max(MinimumCharacters / 2, RecallBudget(limits) / 2);

    /// <summary>
    /// Trims text to a budget, keeping the most recent end of it.
    ///
    /// The marker is left in deliberately rather than cutting silently: the
    /// model reads this, and telling it that history was dropped is what stops
    /// it treating a sentence beginning mid-word as something the user said.
    /// </summary>
    public static string? Trim(string? text, int budget)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= budget)
        {
            return text;
        }

        const string marker = "[…earlier context not included in this plan…]\n";

        // A budget too small to hold the marker and anything else is treated as
        // no room at all, rather than returning a marker with two words after
        // it that read as the whole of the user's history.
        if (budget <= marker.Length + 200)
        {
            return marker;
        }

        var keep = budget - marker.Length;

        // Cut at a line break where there is one nearby, so the kept text starts
        // at the beginning of a remembered fact rather than halfway through one.
        var tail = text[^keep..];
        var breakAt = tail.IndexOf('\n');
        if (breakAt >= 0 && breakAt < 400)
        {
            tail = tail[(breakAt + 1)..];
        }

        return marker + tail;
    }

    /// <summary>
    /// Applies both budgets to a request. The one call the gateway makes.
    /// </summary>
    public static (string? Recall, string? Turns) Apply(
        string? chatRecall, string? recentTurns, PlanLimits limits) =>
        (Trim(chatRecall, RecallBudget(limits)), Trim(recentTurns, TurnBudget(limits)));
}
