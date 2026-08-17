using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Reads whether the user wants to be taught or wants the job done, from their
/// own words and nothing else.
///
/// It runs before any model sees the request, for the same reason
/// <see cref="RequestIntent"/> does: whether Metis may touch the computer is
/// not a question a provider's answer should be able to influence. A model that
/// decides it has been asked to act has, by that decision, granted itself
/// permission to act.
///
/// Ordering is the whole design. Every rule below can fire on the same
/// sentence, so which one is asked first decides the answer, and the order is
/// chosen so that the more specific reading of a sentence beats the more
/// general one: "show me how to open Chrome" is a teaching request that happens
/// to contain "open", not an instruction to open anything.
/// </summary>
public static class IntentDetector
{
    /// <summary>
    /// Phrases where the user is asking to be shown or told. These are checked
    /// first because nearly all of them also contain an action word — you
    /// cannot ask how to do something without naming the something.
    /// </summary>
    private static readonly string[] TeachingTerms =
    [
        "show me how", "teach me", "walk me through", "talk me through",
        "how do i", "how would i", "how can i", "how does", "how do you",
        "explain", "what does", "what is", "what's this", "why does", "why is",
        "guide me", "help me understand", "step by step", "step-by-step",
        "i want to learn", "learn how", "so i can do it", "myself next time"
    ];

    /// <summary>
    /// Phrases where the user has handed the task over. Nothing softer counts:
    /// an inference that the user probably wanted Metis to act is exactly the
    /// inference that must not be allowed to move a mouse.
    /// </summary>
    private static readonly string[] HandoverTerms =
    [
        "do it for me", "do this for me", "do that for me", "just do it",
        "you do it", "can you do it", "could you do it", "please do it",
        "go ahead and", "handle it", "handle this", "take care of it",
        "take over", "sort it out", "sort this out", "deal with it",
        "instead of me", "i don't want to do it", "dont want to do it",
        "on my behalf", "for me please"
    ];

    /// <summary>
    /// Decides what this request is asking for. An empty request teaches,
    /// because with nothing to go on the reading that changes nothing is the
    /// only safe one.
    /// </summary>
    public static IntentDecision Detect(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new IntentDecision(AssistanceIntent.Teach, "Nothing was asked.", IsExplicit: false);
        }

        // Asking to be taught wins over every action word in the sentence.
        if (Matches(trimmed, TeachingTerms, out var teachingTerm))
        {
            return new IntentDecision(
                AssistanceIntent.Teach,
                $"You asked to be shown — \"{teachingTerm}\".",
                IsExplicit: true);
        }

        // A handover is the only thing that beats a teaching phrase, and it has
        // to be said outright.
        if (Matches(trimmed, HandoverTerms, out var handoverTerm))
        {
            return new IntentDecision(
                AssistanceIntent.TakeControl,
                $"You handed it over — \"{handoverTerm}\".",
                IsExplicit: true);
        }

        // "Where is the export button?" is answered by marking the screen, not
        // by pressing it.
        if (RequestIntent.IsPointingRequest(trimmed))
        {
            return new IntentDecision(
                AssistanceIntent.Teach,
                "You asked where something is.",
                IsExplicit: true);
        }

        if (RequestIntent.IsQuestion(trimmed))
        {
            return new IntentDecision(
                AssistanceIntent.Teach,
                "You asked a question.",
                IsExplicit: false);
        }

        // A plain imperative aimed at an assistant is an instruction. This is
        // the same test that already decided whether a click was permitted, so
        // moving to intent does not quietly widen what Metis may do.
        if (RequestIntent.IsComputerActionRequest(trimmed))
        {
            return new IntentDecision(
                AssistanceIntent.TakeControl,
                "You told Metis to do something.",
                IsExplicit: true);
        }

        return new IntentDecision(
            AssistanceIntent.Teach,
            "Nothing in the request asked Metis to act.",
            IsExplicit: false);
    }

    /// <summary>
    /// True when the user is continuing a lesson rather than starting
    /// something new — "and then?", "what next", "ok done". Kept separate from
    /// <see cref="Detect"/> because a continuation carries no intent of its
    /// own: it inherits whatever the turn before it established, and treating
    /// "done" as a fresh request is how a lesson loses its place.
    /// </summary>
    public static bool IsContinuation(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        return trimmed is
            "next" or "what next" or "whats next" or "what's next" or "and then" or "then what" or
            "ok" or "okay" or "done" or "ok done" or "got it" or "yes" or "yep" or "yeah" or
            "continue" or "carry on" or "go on" or "keep going" or "finished" or "i did it";
    }

    private static bool Matches(string text, string[] terms, out string matched)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matched = term;
                return true;
            }
        }

        matched = string.Empty;
        return false;
    }
}
