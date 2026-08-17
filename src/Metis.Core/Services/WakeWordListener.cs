namespace Metis.Core.Services;

/// <summary>
/// What was found in a stretch of transcribed speech.
/// </summary>
public sealed record WakeWordMatch(
    bool Heard,

    /// <summary>
    /// Everything said after the wake word. Empty when the user said only the
    /// name and is waiting to be answered, which is a normal way to start —
    /// "Metis?" then a pause then the question.
    /// </summary>
    string Request)
{
    public static readonly WakeWordMatch NotHeard = new(false, string.Empty);

    /// <summary>True when the wake word arrived with a request attached.</summary>
    public bool HasRequest => Heard && Request.Length > 0;
}

/// <summary>
/// Finds the wake word in transcribed speech, and separates the request that
/// follows it.
///
/// The difficulty is that this runs on transcription rather than on audio, and
/// transcription of a single short name is unreliable — "Metis" comes back as
/// "meetus", "medicine", "metus", "meet us". Requiring an exact match means the
/// user says the name and nothing happens, which is the failure that makes
/// people stop using a wake word at all. So matching is deliberately loose,
/// and the cost of that is bounded: a false wake produces a request Metis
/// answers unnecessarily, which is a wasted reply rather than an action, since
/// anything that touches the computer still runs through intent and safety.
/// </summary>
public static class WakeWordListener
{
    public const string DefaultWakeWord = "Metis";

    /// <summary>
    /// The longest a wake word may be. Long enough for "hey computer", short
    /// enough that it cannot become a sentence the user says by accident.
    /// </summary>
    public const int MaximumWakeWordLength = 24;

    /// <summary>
    /// Looks for the wake word and returns whatever followed it.
    /// </summary>
    public static WakeWordMatch Listen(string? transcript, string? wakeWord)
    {
        var words = Words(transcript);
        var wanted = Words(string.IsNullOrWhiteSpace(wakeWord) ? DefaultWakeWord : wakeWord);

        if (words.Length == 0 || wanted.Length == 0 || wanted.Length > words.Length)
        {
            return WakeWordMatch.NotHeard;
        }

        // Scan for the last occurrence rather than the first. Continuous
        // listening transcribes overlapping stretches of speech, so the name
        // can appear earlier in a segment that was already answered; the most
        // recent one is the one the user is waiting on.
        for (var start = words.Length - wanted.Length; start >= 0; start--)
        {
            if (!MatchesAt(words, wanted, start))
            {
                continue;
            }

            var request = string.Join(' ', words.Skip(start + wanted.Length));
            return new WakeWordMatch(true, request.Trim());
        }

        return WakeWordMatch.NotHeard;
    }

    private static bool MatchesAt(string[] words, string[] wanted, int start)
    {
        for (var offset = 0; offset < wanted.Length; offset++)
        {
            if (!IsCloseEnough(words[start + offset], wanted[offset]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// How far a heard word may be from the wake word and still count. Scaled
    /// to length, because one wrong letter in a four-letter word is a different
    /// thing from one wrong letter in a ten-letter one.
    /// </summary>
    public static bool IsCloseEnough(string heard, string wanted)
    {
        if (string.Equals(heard, wanted, StringComparison.Ordinal))
        {
            return true;
        }

        // A short name has too little information to allow edits without
        // matching half the language, so it must be heard exactly.
        if (wanted.Length <= 3)
        {
            return false;
        }

        var tolerance = wanted.Length <= 6 ? 1 : 2;
        return Distance(heard, wanted, tolerance) <= tolerance;
    }

    /// <summary>
    /// Levenshtein distance, abandoned as soon as it exceeds the tolerance so a
    /// long stretch of speech is not fully compared against every word.
    /// </summary>
    private static int Distance(string left, string right, int tolerance)
    {
        if (Math.Abs(left.Length - right.Length) > tolerance)
        {
            return tolerance + 1;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var best = current[0];

            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), substitution);
                best = Math.Min(best, current[column]);
            }

            if (best > tolerance)
            {
                return tolerance + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    /// <summary>
    /// Splits speech into comparable words: lowercase, and stripped of the
    /// punctuation a transcriber adds on its own — "Metis," and "Metis?" are
    /// the user saying the same thing.
    /// </summary>
    private static string[] Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .ToLowerInvariant()
            .Split((char[])[' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// Cleans up a wake word the user typed into settings. An empty or absurd
    /// one falls back to the default rather than leaving listening unable to
    /// trigger at all.
    /// </summary>
    public static string Normalize(string? wakeWord)
    {
        var trimmed = (wakeWord ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaximumWakeWordLength || trimmed.Any(char.IsControl))
        {
            return DefaultWakeWord;
        }

        return trimmed;
    }
}
