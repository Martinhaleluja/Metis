using System.IO;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Windows;

/// <summary>
/// A folder of sound files resolved onto Metis's interaction moments by name.
/// Files are decoded once and cached, because a cue that has to be decoded from
/// disk on the keyboard hook's path would arrive after the thing it announces.
/// </summary>
public sealed class SoundPack
{
    private static readonly string[] SupportedExtensions = [".wav", ".mp3", ".wma", ".aiff", ".aif"];

    private readonly object _gate = new();
    private readonly Dictionary<MetisSound, List<string>> _files = [];
    private readonly Dictionary<string, SpeechAudio?> _decoded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private readonly Action<string>? _log;
    private string? _lastPlayedVariant;

    public SoundPack(string? folderPath, Action<string>? log = null)
    {
        _log = log;
        FolderPath = folderPath;
        Load();
    }

    public string? FolderPath { get; }

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _files.Count == 0;
            }
        }
    }

    public IReadOnlyCollection<MetisSound> AvailableSounds
    {
        get
        {
            lock (_gate)
            {
                return _files.Keys.ToArray();
            }
        }
    }

    /// <summary>
    /// Returns the audio for a moment, or null when the pack has nothing for it
    /// so the caller can fall back to a built-in cue or stay silent. When a
    /// moment has several files, one is chosen at random without repeating the
    /// previous pick, so a repeated error does not sound like a stuck record.
    /// </summary>
    public SpeechAudio? TryGet(MetisSound sound)
    {
        string? chosen;
        lock (_gate)
        {
            if (!_files.TryGetValue(sound, out var candidates) || candidates.Count == 0)
            {
                return null;
            }

            chosen = Choose(candidates);
            if (_decoded.TryGetValue(chosen, out var cached))
            {
                return cached;
            }
        }

        var audio = SoundCueFile.TryLoad(chosen, out var error);
        if (audio is null)
        {
            _log?.Invoke($"The sound '{Path.GetFileName(chosen)}' could not be used. {error}");
        }

        lock (_gate)
        {
            // Failures are cached as null too. Re-reading a broken file on every
            // activation would cost disk access for a result already known.
            _decoded[chosen] = audio;
        }

        return audio;
    }

    private string Choose(List<string> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var available = candidates.Where(file => file != _lastPlayedVariant).ToArray();
        var chosen = available.Length > 0
            ? available[_random.Next(available.Length)]
            : candidates[_random.Next(candidates.Count)];
        _lastPlayedVariant = chosen;
        return chosen;
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(FolderPath))
            {
                if (!SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sound = SoundPackNaming.Match(Path.GetFileName(file));
                if (sound is null)
                {
                    _log?.Invoke($"The sound '{Path.GetFileName(file)}' does not match a known Metis moment; it was ignored.");
                    continue;
                }

                if (!_files.TryGetValue(sound.Value, out var candidates))
                {
                    candidates = [];
                    _files[sound.Value] = candidates;
                }

                candidates.Add(file);
            }

            var summary = string.Join(
                ", ",
                _files.OrderBy(entry => entry.Key.ToString()).Select(entry => $"{entry.Key} x{entry.Value.Count}"));
            _log?.Invoke($"Sound pack loaded from '{FolderPath}': {summary}");
        }
        catch (Exception exception)
        {
            _log?.Invoke($"The sound pack at '{FolderPath}' could not be read. {exception.Message}");
        }
    }
}
