namespace Metis.Core.Services;

/// <summary>
/// Decides what, if anything, the companion bar writes while Metis talks.
///
/// The bar sits over the user's work, so it earns its place only for a remark
/// short enough to take in at a glance. A long explanation belongs in the chat
/// window, where it can be read properly, rather than as a slab of text
/// floating over whatever the user is doing.
/// </summary>
public static class CompanionSpeech
{
    /// <summary>
    /// The longest line the bar will write. Roughly one spoken sentence.
    /// </summary>
    public const int MaxInlineLength = 110;

    /// <summary>
    /// The longest written answer the bar will hold when Metis is not speaking.
    /// A typed question deserves the whole answer beside the cursor — the user
    /// may be somewhere they cannot listen — but it still cannot run on
    /// forever, so this is the runaway guard rather than an editorial limit.
    /// </summary>
    public const int MaxWrittenLength = 420;

    /// <summary>
    /// How long the same words would have taken to say, so a written answer
    /// appears at the pace it would have been spoken instead of instantly.
    /// Based on roughly 150 words per minute, an unhurried speaking rate.
    /// </summary>
    public static TimeSpan ReadingDuration(string? text)
    {
        var words = CountWords(text);
        return words == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Math.Clamp(words * GuidanceTuning.ScaleMs(400d), GuidanceTuning.ScaleMs(700d), 30_000d));
    }

    public static int CountWords(string? text) =>
        (text ?? string.Empty).Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    /// <summary>
    /// The line for a written reply: the whole answer, capped only to stop it
    /// running away. Unlike the spoken path this does not fall back to the
    /// pointing cue, because with no voice the text is the entire answer.
    /// </summary>
    public static string? ChooseWrittenLine(string? spokenText, string? bubbleCue)
    {
        var text = (spokenText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            var cue = (bubbleCue ?? string.Empty).Trim();
            return cue.Length > 0 ? cue : null;
        }

        return text.Length <= MaxWrittenLength ? text : text[..MaxWrittenLength].TrimEnd() + "…";
    }

    public static bool IsShortEnough(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length > 0 && trimmed.Length <= MaxInlineLength && CountsAsOneRemark(trimmed);
    }

    /// <summary>
    /// Chooses the line for the bar: the spoken words when they are short, the
    /// pointing cue otherwise, and nothing at all when neither fits. Returning
    /// null is a real answer — silence is better than a truncated sentence.
    /// </summary>
    public static string? ChooseLine(string? spokenText, string? bubbleCue)
    {
        if (IsShortEnough(spokenText))
        {
            return spokenText!.Trim();
        }

        var cue = (bubbleCue ?? string.Empty).Trim();
        return cue.Length > 0 && cue.Length <= MaxInlineLength ? cue : null;
    }

    /// <summary>
    /// Two sentences can still be short, but three is a paragraph being read
    /// out, and that belongs in the window rather than over the user's work.
    /// </summary>
    private static bool CountsAsOneRemark(string text) =>
        text.Count(character => character is '.' or '!' or '?') <= 2;
}
