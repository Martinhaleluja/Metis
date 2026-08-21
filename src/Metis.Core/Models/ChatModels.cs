namespace Metis.Core.Models;

public sealed record ChatTurn(string Role, string Text, DateTimeOffset At)
{
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One conversation. Metis keeps several rather than one endless thread,
/// because the useful unit of recall is "the time we worked on the video", not
/// everything ever said. Each session carries the application it was about, so
/// a later conversation in the same program can find it.
/// </summary>
public sealed record ChatSession(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatTurn> Turns,
    string? Application = null)
{
    public const int MaxTurnsKept = 60;

    public static ChatSession Start(string? application = null) => new(
        Guid.NewGuid().ToString("n")[..8],
        "New chat",
        DateTimeOffset.Now,
        DateTimeOffset.Now,
        [],
        application);

    public bool IsEmpty => Turns.Count == 0;

    public ChatSession Append(ChatTurn turn)
    {
        var turns = Turns.Append(turn).ToList();
        if (turns.Count > MaxTurnsKept)
        {
            turns.RemoveRange(0, turns.Count - MaxTurnsKept);
        }

        return this with
        {
            Turns = turns,
            UpdatedAt = turn.At,
            // The first thing the user said is the best name for the
            // conversation, and naming it then avoids a session called
            // "New chat" forever if it is never revisited.
            Title = Title == "New chat" && turn.IsUser ? MakeTitle(turn.Text) : Title
        };
    }

    private static string MakeTitle(string text)
    {
        var cleaned = string.Join(' ', text.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (cleaned.Length == 0)
        {
            return "New chat";
        }

        const int limit = 48;
        if (cleaned.Length <= limit)
        {
            return cleaned;
        }

        var cut = cleaned[..limit];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > limit / 2 ? cut[..lastSpace] : cut) + "…";
    }
}
