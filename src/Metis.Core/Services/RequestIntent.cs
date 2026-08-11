namespace Metis.Core.Services;

/// <summary>
/// Reads what the user wants from their own words, before any model sees them.
/// This is kept out of the reasoning layer on purpose: in Guide mode the
/// difference between "asked Metis to act" and "asked Metis about the screen"
/// decides whether a click is permitted, and that decision must not be
/// something a provider response can influence.
/// </summary>
public static class RequestIntent
{
    private static readonly string[] ActionTerms =
    [
        "click", "double-click", "double click", "right-click", "right click",
        "press", "tap", "hover", "point to", "point at", "move the cursor", "move cursor",
        "open", "launch", "start", "run", "close", "quit", "exit",
        "minimize", "minimise", "maximize", "maximise", "restore", "switch to", "focus",
        "type", "write", "enter", "fill in", "paste", "copy", "select", "choose",
        "scroll", "drag", "resize", "play", "pause", "mute",
        "search for", "look up", "navigate", "go to", "browse", "visit",
        "turn on", "turn off", "toggle", "enable", "disable",
        "save", "rename", "create", "make a", "add a", "set the", "change the",
        "do it", "for me"
    ];

    private static readonly string[] QuestionOpeners =
    [
        "how ", "what ", "why ", "where ", "when ", "which ", "who ",
        "explain", "teach me", "tell me", "show me where", "help me understand"
    ];

    private static readonly string[] ScreenTerms =
    [
        "screen", "this window", "active window", "desktop", "what do you see",
        "where is", "where's", "find the", "button", "menu", "icon", "this", "that"
    ];

    /// <summary>
    /// True when the user told Metis to operate the computer. Recognising a
    /// plain command such as "open Chrome" matters: matching only longer forms
    /// like "open the" silently withheld most real requests in Guide mode.
    /// </summary>
    public static bool IsComputerActionRequest(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length != 0 &&
               !IsQuestion(trimmed) &&
               ActionTerms.Any(term => trimmed.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A question about the screen is not an instruction to act on it. Without
    /// this, "how do I close this tab?" would be treated as "close this tab"
    /// and Metis would do the very thing the user asked to be taught.
    /// </summary>
    public static bool IsQuestion(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.EndsWith('?') ||
               QuestionOpeners.Any(opener => trimmed.StartsWith(opener, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when answering honestly requires having looked at the screen, so
    /// Metis can refuse rather than describe a desktop it never captured.
    /// </summary>
    public static bool RequiresScreenObservation(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length != 0 &&
               ScreenTerms.Any(term => trimmed.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
