namespace Metis.Core.Services;

using Metis.Core.Models;

/// <summary>
/// Maps a sound file's name onto the moment it should play. Matching is by
/// keyword rather than by exact filename so the pack keeps working when a file
/// is renamed, re-cased, numbered, or misspelled — the author of a sound pack
/// should not have to match a string table character for character.
/// </summary>
public static class SoundPackNaming
{
    /// <summary>
    /// Returns the moment a file name describes, or null when it matches none.
    /// </summary>
    public static MetisSound? Match(string? fileName)
    {
        var name = Normalize(fileName);
        if (name.Length == 0)
        {
            return null;
        }

        // Order matters where names overlap. "audio recording started" and
        // "app started" both contain "start", so the more specific subject is
        // tested first in every ambiguous pair.
        if (name.Contains("error") || name.Contains("fail"))
        {
            return MetisSound.Error;
        }

        if (name.Contains("inspect"))
        {
            return IsReleaseWord(name) ? MetisSound.InspectReleased : MetisSound.InspectPressed;
        }

        if (name.Contains("record") || name.Contains("listen"))
        {
            return MetisSound.RecordingStarted;
        }

        if (name.Contains("complete") || name.Contains("done") || name.Contains("finish"))
        {
            return MetisSound.TaskComplete;
        }

        if (name.Contains("setting") || name.Contains("saved"))
        {
            return MetisSound.SettingsSaved;
        }

        if (name.Contains("stop") || name.Contains("cancel"))
        {
            return MetisSound.Stopped;
        }

        if (name.Contains("sent") || name.Contains("send") ||
            name.Contains("woosh") || name.Contains("whoosh") || name.Contains("thinking"))
        {
            return MetisSound.RequestSent;
        }

        if (name.Contains("start") || name.Contains("launch") || name.Contains("ready"))
        {
            return MetisSound.AppStarted;
        }

        return null;
    }

    /// <summary>
    /// Recognises "released" alongside the common misspellings, because a cue
    /// silently failing to load over a typo is a miserable thing to debug.
    /// </summary>
    private static bool IsReleaseWord(string name) =>
        name.Contains("release") || name.Contains("relese") ||
        name.Contains("relesed") || name.Contains("realese") ||
        name.Contains(" up") || name.Contains("lifted");

    private static string Normalize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var withoutExtension = fileName.Trim();
        var lastDot = withoutExtension.LastIndexOf('.');
        if (lastDot > 0)
        {
            withoutExtension = withoutExtension[..lastDot];
        }

        // Digits go too, so "error 1" and "error 4" both read as "error" and
        // become variants of the same moment rather than separate names.
        var characters = withoutExtension
            .ToLowerInvariant()
            .Select(character => char.IsLetter(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
