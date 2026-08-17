using System.Text;
using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Finds the parts of past conversations worth bringing into the current one.
///
/// Recall is deliberately a digest rather than a transcript: pasting whole old
/// chats in would crowd out the screenshot and the live conversation, and most
/// of an old chat is irrelevant to the question being asked now. What earns its
/// place is a line or two from a conversation that was about the same thing.
/// </summary>
public static class ChatRecall
{
    public const int MaxRecalledSessions = 3;
    private const int MaxDigestCharacters = 900;

    private static readonly string[] Stopwords =
    [
        "the", "and", "for", "with", "this", "that", "how", "what", "why", "where", "when",
        "you", "your", "can", "does", "did", "was", "were", "into", "from", "about", "please",
        "metis", "have", "has", "are", "its", "it's", "there", "then", "than", "just", "want"
    ];

    /// <summary>
    /// Scores a past session against the current request and application.
    /// Higher is more relevant; zero means nothing in common.
    /// </summary>
    public static int Score(ChatSession session, string? request, string? application)
    {
        var terms = Keywords(request);
        if (terms.Count == 0 && string.IsNullOrWhiteSpace(application))
        {
            return 0;
        }

        var score = 0;

        // Same application is strong evidence on its own: two conversations in
        // the same program are usually about the same work.
        if (!string.IsNullOrWhiteSpace(application) &&
            !string.IsNullOrWhiteSpace(session.Application) &&
            session.Application!.Contains(application!, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        var haystack = (session.Title + " " + string.Join(' ', session.Turns.Select(turn => turn.Text)))
            .ToLowerInvariant();

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score;
    }

    /// <summary>
    /// Builds the recall digest, or null when nothing past is relevant. Only
    /// the user's own lines are quoted: what the user said they wanted is a
    /// fact, whereas an old Metis reply is just a previous guess.
    /// </summary>
    public static string? Describe(
        IEnumerable<ChatSession> sessions,
        string? currentSessionId,
        string? request,
        string? application)
    {
        var relevant = sessions
            .Where(session => session.Id != currentSessionId && !session.IsEmpty)
            .Select(session => (Session: session, Score: Score(session, request, application)))
            .Where(entry => entry.Score >= 2)
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Session.UpdatedAt)
            .Take(MaxRecalledSessions)
            .ToArray();

        if (relevant.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Earlier conversations that may be relevant:");

        foreach (var (session, _) in relevant)
        {
            var asked = session.Turns
                .Where(turn => turn.IsUser)
                .TakeLast(2)
                .Select(turn => turn.Text.Trim())
                .Where(text => text.Length > 0);

            builder.AppendLine($"- {session.Title} ({session.UpdatedAt:d}): {string.Join(" / ", asked)}");
        }

        var digest = builder.ToString().Trim();
        return digest.Length <= MaxDigestCharacters ? digest : digest[..MaxDigestCharacters] + "…";
    }

    /// <summary>
    /// Whether a request looks like a change of subject, which is when a new
    /// chat should be started rather than extending the current one.
    /// </summary>
    public static bool StartsNewSubject(ChatSession current, string request)
    {
        if (current.IsEmpty)
        {
            return false;
        }

        var now = Keywords(request);
        if (now.Count == 0)
        {
            return false;
        }

        var before = Keywords(string.Join(
            ' ',
            current.Turns.Where(turn => turn.IsUser).TakeLast(3).Select(turn => turn.Text)));

        // No shared vocabulary at all with the recent conversation means the
        // user has moved on to something else.
        return before.Count > 0 && !now.Overlaps(before);
    }

    private static HashSet<string> Keywords(string? text) =>
        (text ?? string.Empty)
            .ToLowerInvariant()
            .Split(
                [' ', '\t', '\r', '\n', ',', '.', '?', '!', ';', ':', '"', '\'', '(', ')', '/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length > 2 && !Stopwords.Contains(word))
            .ToHashSet(StringComparer.Ordinal);
}
