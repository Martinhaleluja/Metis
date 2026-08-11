using System.Text.RegularExpressions;

namespace Metis.Core.Services;

/// <summary>
/// Turns a diagnostic error message into something worth hearing. Written
/// errors can afford a file path and three sentences of remedy; a spoken one
/// has to land in a couple of seconds, so this keeps the first sentence and
/// replaces anything that does not survive being read aloud.
/// </summary>
public static partial class SpokenErrorSummarizer
{
    private const int MaxSpokenLength = 120;

    public static string Summarize(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        text = WindowsPathPattern().Replace(text, "the saved path");
        text = UrlPattern().Replace(text, "the address");
        text = WhitespacePattern().Replace(text, " ").Trim();

        var sentence = FirstSentence(text);
        return sentence.Length <= MaxSpokenLength ? sentence : TrimToWord(sentence, MaxSpokenLength);
    }

    private static string FirstSentence(string text)
    {
        // A sentence only ends where punctuation is followed by a space, so
        // version numbers and abbreviations do not cut the message in half.
        for (var index = 0; index < text.Length - 1; index++)
        {
            if (text[index] is '.' or '!' or '?' && char.IsWhiteSpace(text[index + 1]))
            {
                return text[..(index + 1)].Trim();
            }
        }

        return text;
    }

    private static string TrimToWord(string text, int maxLength)
    {
        var cut = text[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd(' ', ',', ';', ':', '-') + ".";
    }

    [GeneratedRegex(@"[A-Za-z]:\\[^\s""']+")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
