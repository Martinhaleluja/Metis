using System.Text.Json;
using System.Text.Json.Serialization;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Data;

/// <summary>
/// Persists Metis's structured memory next to its settings. Screen content is
/// never written here: only the skill names, goals, and preferences needed to
/// adapt future guidance.
/// </summary>
public sealed class JsonMemoryStore : IMemoryService, IDisposable
{
    private const int MaxTasks = 40;
    private const int MaxSkills = 400;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private bool _disposed;

    public JsonMemoryStore(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Metis");
        MemoryPath = Path.Combine(directory, "memory.json");
    }

    public string MemoryPath { get; }

    public async Task<MemoryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordSkillUseAsync(
        string application,
        string skill,
        bool succeeded,
        bool neededGuidance,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            return;
        }

        await MutateAsync(
            document =>
            {
                var normalizedApplication = Shorten(application, 80);
                var normalizedSkill = Shorten(skill, 80);
                var existing = document.Skills.FirstOrDefault(record =>
                    string.Equals(record.Application, normalizedApplication, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.Skill, normalizedSkill, StringComparison.OrdinalIgnoreCase))
                    ?? new SkillRecord { Application = normalizedApplication, Skill = normalizedSkill };

                var updated = SkillMemoryEngine.Record(existing, succeeded, neededGuidance);
                var skills = document.Skills
                    .Where(record => record.Key != updated.Key)
                    .Append(updated)
                    .OrderByDescending(record => record.LastUsed ?? DateTimeOffset.MinValue)
                    .Take(MaxSkills)
                    .ToArray();
                return document with { Skills = skills };
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordTaskOutcomeAsync(
        AgentTaskState state,
        bool success,
        string summary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await MutateAsync(
            document =>
            {
                var entry = new TaskMemoryEntry
                {
                    TaskId = state.TaskId,
                    Goal = Shorten(state.OriginalUserGoal, 400),
                    Application = Shorten(state.CurrentApplication, 120),
                    Mode = state.CurrentMode,
                    CompletedSteps = state.PreviousActions.Select(step => Shorten(step, 160)).ToArray(),
                    PendingStep = success ? null : Shorten(summary, 200),
                    Success = success,
                    UpdatedAt = DateTimeOffset.Now
                };

                var tasks = document.Tasks
                    .Where(task => task.TaskId != entry.TaskId)
                    .Append(entry)
                    .OrderByDescending(task => task.UpdatedAt)
                    .Take(MaxTasks)
                    .ToArray();
                return document with { Tasks = tasks };
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetPreferenceAsync(string key, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Preferences.TryGetValue(key, out var value) ? value : null;
    }

    public Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.CompletedTask;
        }

        return MutateAsync(
            document =>
            {
                var preferences = new Dictionary<string, string>(document.Preferences, StringComparer.OrdinalIgnoreCase)
                {
                    [key.Trim()] = Shorten(value, 400)
                };
                return document with { Preferences = preferences };
            },
            cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(new MemoryDocument(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MutateAsync(
        Func<MemoryDocument, MemoryDocument> mutate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(mutate(document), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MemoryDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(MemoryPath))
        {
            return new MemoryDocument();
        }

        try
        {
            await using var stream = new FileStream(
                MemoryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer
                .DeserializeAsync<MemoryDocument>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return document ?? new MemoryDocument();
        }
        catch (JsonException)
        {
            // Unreadable memory must never block Metis from starting; a fresh
            // document is safer than refusing to answer.
            return new MemoryDocument();
        }
    }

    private async Task WriteAsync(MemoryDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(MemoryPath)
            ?? throw new InvalidOperationException("The Metis memory directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = MemoryPath + ".tmp";

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, MemoryPath, true);
    }

    private static string Shorten(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
