using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>What the screen says about whether a lesson step was carried out.</summary>
public enum StepProgress
{
    /// <summary>The screen changed in the way the step said it would.</summary>
    Verified,

    /// <summary>The screen can be read, and it has not changed that way yet.</summary>
    NotYet,

    /// <summary>
    /// Nothing readable to judge by. Not the same as "not done" — a step whose
    /// result is a colour changing, a value typed into a box, or anything else
    /// the accessibility tree does not report is simply invisible to this. The
    /// lesson carries on exactly as it always did.
    /// </summary>
    Unknowable
}

/// <summary>
/// Whether the learner has actually done the step Metis just explained.
///
/// Metis used to speak a step, hold the mark for a few seconds, and move on
/// regardless — so a walkthrough marched ahead of anyone who paused, and every
/// mark after that pointed at a screen they had not reached. `done_when` was
/// being asked of the model, parsed, and then never looked at.
///
/// This reads the answer off the screen locally: no model call, no cost, and
/// nothing that can hang. It is deliberately crude, because the alternative to
/// a crude local check was no check at all, and because the cost of being wrong
/// is bounded in both directions — a missed completion nudges once more, and a
/// false completion behaves exactly like the old timer.
///
/// The rule that keeps this safe is <see cref="StepProgress.Unknowable"/>. A
/// lesson must never stall waiting for proof that cannot arrive, so anything
/// this cannot read falls straight back to the original behaviour.
/// </summary>
public static class StepCompletionEvidence
{
    /// <summary>
    /// Whether a step is worth waiting on at all.
    ///
    /// Only steps that named something checkable are. Asking "has it happened
    /// yet?" about a step with no stated outcome and no named target produces a
    /// guess, and a guess here costs the learner a pause on every step.
    /// </summary>
    public static bool CanBeChecked(LessonStep? step, LessonStep? nextStep) =>
        !string.IsNullOrWhiteSpace(step?.DoneWhen) ||
        !string.IsNullOrWhiteSpace(nextStep?.ElementName);

    /// <summary>
    /// Reads the evidence for one step.
    /// </summary>
    /// <param name="step">The step the learner was asked to do.</param>
    /// <param name="nextStep">
    /// The step after it. Its target appearing is the strongest signal
    /// available: the dialog the next step acts on cannot be there unless this
    /// step opened it.
    /// </param>
    /// <param name="nextTargetVisibleNow">Whether the next step's named control can be found now.</param>
    /// <param name="nextTargetVisibleBefore">Whether it could be found before this step was explained.</param>
    /// <param name="windowTitleNow">The foreground window title now.</param>
    /// <param name="windowTitleBefore">The foreground window title before the step.</param>
    /// <param name="screenTextNow">
    /// The accessibility snapshot as text. Searched for the words
    /// <c>done_when</c> named, which is coarse and meant to be.
    /// </param>
    public static StepProgress Read(
        LessonStep? step,
        LessonStep? nextStep,
        bool nextTargetVisibleNow,
        bool nextTargetVisibleBefore,
        string? windowTitleNow,
        string? windowTitleBefore,
        string? screenTextNow)
    {
        if (step is null || !CanBeChecked(step, nextStep))
        {
            return StepProgress.Unknowable;
        }

        // Strongest evidence: what the next step needs is on screen now and was
        // not before. Something opened it, and the step just explained is the
        // only thing that was supposed to.
        if (!string.IsNullOrWhiteSpace(nextStep?.ElementName))
        {
            if (nextTargetVisibleNow && !nextTargetVisibleBefore)
            {
                return StepProgress.Verified;
            }
        }

        // The window changed. Coarse, but a different foreground window after a
        // step that was meant to open one is exactly what was asked for.
        var titlesDiffer =
            !string.IsNullOrWhiteSpace(windowTitleNow) &&
            !string.IsNullOrWhiteSpace(windowTitleBefore) &&
            !string.Equals(windowTitleNow, windowTitleBefore, StringComparison.OrdinalIgnoreCase);

        if (titlesDiffer)
        {
            return StepProgress.Verified;
        }

        // Last resort: the names done_when talks about, looked for on screen.
        var wanted = SignificantWords(step.DoneWhen);
        if (wanted.Count > 0 && !string.IsNullOrWhiteSpace(screenTextNow))
        {
            var found = wanted.Count(word =>
                screenTextNow.Contains(word, StringComparison.OrdinalIgnoreCase));

            // Every named thing, not merely one of them. "the Save As dialog"
            // matching on "the" would verify anything at all.
            if (found == wanted.Count)
            {
                return StepProgress.Verified;
            }
        }

        // Readable, and no sign of it yet. Only say so when there was something
        // real to look for; otherwise this is an opinion dressed as a reading.
        var hadSomethingToLookFor =
            wanted.Count > 0 ||
            (!string.IsNullOrWhiteSpace(nextStep?.ElementName) && !nextTargetVisibleBefore);

        return hadSomethingToLookFor ? StepProgress.NotYet : StepProgress.Unknowable;
    }

    /// <summary>
    /// The words in a <c>done_when</c> phrase worth searching the screen for.
    ///
    /// Proper nouns and interface labels survive; ordinary English does not.
    /// "The Save As dialog opens" reduces to "Save" and "As" — searching for
    /// "the", "dialog" or "opens" would match half of Windows.
    /// </summary>
    public static IReadOnlyList<string> SignificantWords(string? doneWhen)
    {
        if (string.IsNullOrWhiteSpace(doneWhen))
        {
            return [];
        }

        var words = doneWhen.Split(
            [' ', '\t', '\n', '\r', ',', '.', ';', ':', '"', '\'', '(', ')', '[', ']'],
            StringSplitOptions.RemoveEmptyEntries);

        var keep = new List<string>();
        foreach (var word in words)
        {
            // Two characters, not three. "Save As" is one label and dropping
            // the "As" leaves "Save", which matches a Save button that was
            // always there and verifies a step nobody performed. Short ordinary
            // words are already excluded by the capitalisation test below.
            if (word.Length < 2 || Commonplace.Contains(word))
            {
                continue;
            }

            // Capitalised mid-phrase means a name on screen rather than a word
            // in a sentence. The first word is skipped by that test because a
            // sentence starts capitalised whatever it says.
            if (char.IsUpper(word[0]) && !string.Equals(word, words[0], StringComparison.Ordinal))
            {
                keep.Add(word);
            }
        }

        // More than three and the phrase is prose, not a name. Matching all of
        // a long list would almost never succeed, and a check that can only
        // fail is worse than no check.
        return keep.Count is > 0 and <= 3 ? keep : [];
    }

    private static readonly HashSet<string> Commonplace = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "you", "your", "will", "when", "then", "with", "that", "this",
        "has", "have", "been", "for", "from", "into", "onto", "over", "opens", "open",
        "opened", "shows", "show", "shown", "appears", "appear", "appeared", "changes",
        "changed", "becomes", "become", "see", "sees", "seen", "screen", "window",
        "dialog", "box", "button", "menu", "panel", "tab", "field", "list", "item",
        "new", "now", "its", "it's", "are", "was", "were", "displays", "displayed"
    };
}
