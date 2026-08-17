using System.IO;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Data;

/// <summary>
/// Loads the user's skills from a folder of markdown files. A folder was chosen
/// over a database or an in-app editor because adding a skill should be as
/// simple as dropping in a text file — the user can write one in Notepad, keep
/// them in a synced folder, or share one with a colleague.
/// </summary>
public sealed class FileSkillStore
{
    private static readonly string[] Extensions = [".md", ".txt", ".markdown"];
    private const long MaxFileBytes = 512 * 1024;

    private readonly Action<string>? _log;

    public FileSkillStore(string? folderPath, Action<string>? log = null)
    {
        FolderPath = folderPath;
        _log = log;
    }

    public string? FolderPath { get; }

    /// <summary>
    /// Reads every skill in the folder. Failures are reported and skipped: one
    /// unreadable file must not cost the user their other skills.
    /// </summary>
    public IReadOnlyList<SkillPack> Load()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            return [];
        }

        var skills = new List<SkillPack>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(FolderPath))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (new FileInfo(file).Length > MaxFileBytes)
                    {
                        _log?.Invoke($"The skill '{Path.GetFileName(file)}' is too large to load.");
                        continue;
                    }

                    var skill = SkillLibrary.Parse(Path.GetFileName(file), File.ReadAllText(file));
                    if (skill is null)
                    {
                        continue;
                    }

                    skills.Add(skill with { SourcePath = file });
                }
                catch (Exception exception)
                {
                    _log?.Invoke($"The skill '{Path.GetFileName(file)}' could not be read. {exception.Message}");
                }
            }

            if (skills.Count > 0)
            {
                _log?.Invoke($"Loaded {skills.Count} user skill(s) from '{FolderPath}'.");
            }
        }
        catch (Exception exception)
        {
            _log?.Invoke($"The skills folder '{FolderPath}' could not be read. {exception.Message}");
        }

        return skills;
    }

    /// <summary>
    /// Creates the folder and writes a worked example, so a user who enables
    /// skills finds something to copy rather than an empty directory.
    /// </summary>
    public void EnsureFolderWithExample()
    {
        if (string.IsNullOrWhiteSpace(FolderPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(FolderPath);
            var example = Path.Combine(FolderPath, "example-skill.md");
            if (File.Exists(example))
            {
                return;
            }

            File.WriteAllText(
                example,
                """
                # Example skill
                description: Shows how to teach Metis about a program
                applies-to: example, notepad

                Replace this file with what you know about a program you use.

                Anything you would tell a new colleague works here:

                - Where the important menus and panels live
                - What the in-house jargon means
                - The order of steps in a workflow you repeat
                - Mistakes that are easy to make and how to spot them

                Metis loads a skill when the window title or your request mentions
                anything in `applies-to`, so list the program name and any file
                extensions it uses.
                """);
        }
        catch (Exception exception)
        {
            _log?.Invoke($"The skills folder could not be created. {exception.Message}");
        }
    }
}
