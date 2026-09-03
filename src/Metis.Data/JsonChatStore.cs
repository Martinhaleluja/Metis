using System.IO;
using System.Text.Json;
using Metis.Core.Models;

namespace Metis.Data;

/// <summary>
/// Persists conversations as one JSON file per chat under
/// <c>%LOCALAPPDATA%\Metis\chats</c>. One file per chat rather than a single
/// index keeps a corrupt or hand-edited file from costing the user every
/// conversation, and makes a chat easy to delete or copy on its own.
/// </summary>
public sealed class JsonChatStore
{
    private const int MaxSessionsKept = 60;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Action<string>? _log;

    public JsonChatStore(string? folderPath = null, Action<string>? log = null)
    {
        FolderPath = folderPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Metis",
            "chats");
        _log = log;
    }

    public string FolderPath { get; }

    /// <summary>
    /// Loads every stored chat, newest first. An unreadable file is skipped
    /// rather than thrown, so one bad chat cannot stop Metis from starting.
    /// </summary>
    public IReadOnlyList<ChatSession> LoadAll()
    {
        if (!Directory.Exists(FolderPath))
        {
            return [];
        }

        var sessions = new List<ChatSession>();
        foreach (var file in Directory.EnumerateFiles(FolderPath, "*.json"))
        {
            try
            {
                var text = LocalVault.Unprotect(File.ReadAllBytes(file));
                if (text is null)
                {
                    // Written by a different Windows account or on another
                    // machine. There is nothing to recover from it.
                    _log?.Invoke($"The chat '{Path.GetFileName(file)}' belongs to another Windows account and was skipped.");
                    continue;
                }

                var session = JsonSerializer.Deserialize<ChatSession>(text, Options);
                if (session is not null && session.Turns.Count > 0)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception exception)
            {
                _log?.Invoke($"The chat '{Path.GetFileName(file)}' could not be read. {exception.Message}");
            }
        }

        return sessions.OrderByDescending(session => session.UpdatedAt).ToArray();
    }

    public void Save(ChatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsEmpty)
        {
            // An untouched chat is not worth a file on disk.
            return;
        }

        try
        {
            Directory.CreateDirectory(FolderPath);
            // A chat is a transcript of what Metis was shown, so it is encrypted
            // to this Windows account rather than left as readable JSON in a
            // predictable folder. A file written by an older version is plain
            // text and stays readable; it becomes encrypted the next time that
            // chat is saved.
            File.WriteAllBytes(
                PathFor(session.Id),
                LocalVault.Protect(JsonSerializer.Serialize(session, Options)));
            TrimOldest();
        }
        catch (Exception exception)
        {
            _log?.Invoke($"The chat could not be saved. {exception.Message}");
        }
    }

    public void Delete(string sessionId)
    {
        try
        {
            var path = PathFor(sessionId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            _log?.Invoke($"The chat could not be deleted. {exception.Message}");
        }
    }

    public void DeleteAll()
    {
        try
        {
            if (Directory.Exists(FolderPath))
            {
                Directory.Delete(FolderPath, recursive: true);
            }
        }
        catch (Exception exception)
        {
            _log?.Invoke($"The chats could not be cleared. {exception.Message}");
        }
    }

    /// <summary>
    /// Chat history is a convenience, not an archive, so the oldest are dropped
    /// once there are more than a person would ever scroll back through.
    /// </summary>
    private void TrimOldest()
    {
        try
        {
            var files = new DirectoryInfo(FolderPath)
                .GetFiles("*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(MaxSessionsKept)
                .ToArray();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (Exception exception)
        {
            _log?.Invoke($"Old chats could not be trimmed. {exception.Message}");
        }
    }

    private string PathFor(string sessionId)
    {
        var safe = new string(sessionId.Where(char.IsLetterOrDigit).ToArray());
        return Path.Combine(FolderPath, $"{(safe.Length == 0 ? "chat" : safe)}.json");
    }
}
