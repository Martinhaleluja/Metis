using Metis.Core.Models;
using Metis.Data;

namespace Metis.Tests;

public sealed class JsonMemoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "metis-memory-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task An_absent_memory_file_loads_as_an_empty_document()
    {
        using var store = new JsonMemoryStore(_directory);

        var document = await store.LoadAsync();

        Assert.Empty(document.Skills);
        Assert.Empty(document.Tasks);
        Assert.Empty(document.Preferences);
    }

    [Fact]
    public async Task Skill_uses_accumulate_across_reloads()
    {
        using (var store = new JsonMemoryStore(_directory))
        {
            await store.RecordSkillUseAsync("FL Studio", "Sidechain", succeeded: true, neededGuidance: true);
            await store.RecordSkillUseAsync("FL Studio", "Sidechain", succeeded: true, neededGuidance: false);
        }

        using var reopened = new JsonMemoryStore(_directory);
        var document = await reopened.LoadAsync();

        var skill = Assert.Single(document.Skills);
        Assert.Equal("Sidechain", skill.Skill);
        Assert.Equal(2, skill.SuccessfulUses);
        Assert.Equal(1, skill.GuidedUses);
    }

    [Fact]
    public async Task Recording_the_same_task_twice_updates_it_rather_than_duplicating_it()
    {
        using var store = new JsonMemoryStore(_directory);
        var state = AgentTaskState.Start("Export a WAV", "FL Studio", OperatingMode.Guide);

        await store.RecordTaskOutcomeAsync(state, success: false, "Stopped at the export dialog");
        await store.RecordTaskOutcomeAsync(state, success: true, "Exported");

        var document = await store.LoadAsync();
        var task = Assert.Single(document.Tasks);
        Assert.True(task.Success);
        Assert.Null(task.PendingStep);
    }

    [Fact]
    public async Task Preferences_round_trip()
    {
        using var store = new JsonMemoryStore(_directory);

        await store.SetPreferenceAsync("explanation-depth", "short");

        Assert.Equal("short", await store.GetPreferenceAsync("explanation-depth"));
        Assert.Null(await store.GetPreferenceAsync("unset-key"));
    }

    [Fact]
    public async Task Clearing_removes_everything_the_user_asked_to_forget()
    {
        using var store = new JsonMemoryStore(_directory);
        await store.RecordSkillUseAsync("Photoshop", "Layer masks", succeeded: true, neededGuidance: true);
        await store.SetPreferenceAsync("voice", "calm");

        await store.ClearAsync();

        var document = await store.LoadAsync();
        Assert.Empty(document.Skills);
        Assert.Empty(document.Preferences);
    }

    [Fact]
    public async Task A_corrupt_memory_file_does_not_stop_metis_from_starting()
    {
        Directory.CreateDirectory(_directory);
        using var store = new JsonMemoryStore(_directory);
        await File.WriteAllTextAsync(store.MemoryPath, "{ not json");

        var document = await store.LoadAsync();

        Assert.Empty(document.Skills);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }
}
