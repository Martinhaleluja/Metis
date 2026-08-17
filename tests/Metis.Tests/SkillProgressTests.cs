using Metis.Core.Models;
using Metis.Data;

namespace Metis.Tests;

/// <summary>
/// Progress has to be reported at the moment it is earned. A tool that claims
/// to build capability and then keeps the evidence in a file nobody opens has
/// not really shown the user anything.
/// </summary>
public sealed class SkillProgressTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "metis-progress-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task The_first_success_reports_a_level_up()
    {
        using var store = new JsonMemoryStore(_directory);

        var progress = await store.RecordSkillUseAsync("Blender", "Extrude", succeeded: true, neededGuidance: true);

        Assert.NotNull(progress);
        Assert.True(progress!.LevelledUp);
        Assert.Equal(SkillLevel.New, progress.Previous);
    }

    [Fact]
    public async Task A_repeat_at_the_same_level_reports_no_level_up()
    {
        using var store = new JsonMemoryStore(_directory);

        // The first guided success earns a level; the second sits at the same
        // one, and must not be announced as progress the user has not made.
        await store.RecordSkillUseAsync("Blender", "Extrude", succeeded: true, neededGuidance: true);
        var progress = await store.RecordSkillUseAsync("Blender", "Extrude", succeeded: true, neededGuidance: true);

        Assert.NotNull(progress);
        Assert.False(progress!.LevelledUp);
        Assert.Equal(progress.Previous, progress.Record.Level);
    }

    [Fact]
    public async Task Working_unaided_eventually_reports_independence()
    {
        using var store = new JsonMemoryStore(_directory);
        SkillProgress? last = null;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            last = await store.RecordSkillUseAsync("Excel", "Pivot tables", succeeded: true, neededGuidance: false);
        }

        Assert.NotNull(last);
        Assert.True(last!.Record.Level is SkillLevel.Advanced or SkillLevel.Mastered);
        Assert.True(
            await ReachedIndependenceAtSomePoint(store),
            "reaching independence should have been reported on the turn it happened");
    }

    [Fact]
    public async Task Each_report_starts_from_the_level_the_last_one_ended_at()
    {
        using var store = new JsonMemoryStore(_directory);
        SkillLevel? carried = null;

        // The invariant that matters, independent of where the thresholds sit:
        // one turn's reported level is the next turn's starting point.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var progress = await store.RecordSkillUseAsync(
                "Word", "Citations", succeeded: true, neededGuidance: false);

            Assert.NotNull(progress);
            if (carried is not null)
            {
                Assert.Equal(carried, progress!.Previous);
            }

            carried = progress!.Record.Level;
        }
    }

    [Fact]
    public async Task An_empty_skill_name_reports_nothing() =>
        Assert.Null(await new JsonMemoryStore(_directory)
            .RecordSkillUseAsync("Word", "   ", succeeded: true, neededGuidance: false));

    private async Task<bool> ReachedIndependenceAtSomePoint(JsonMemoryStore store)
    {
        // Practising a fresh skill unaided and watching for the milestone.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var progress = await store.RecordSkillUseAsync(
                "Excel", "Named ranges", succeeded: true, neededGuidance: false);
            if (progress?.ReachedIndependence == true)
            {
                return true;
            }
        }

        return false;
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
            // A leftover temp directory must not fail the run.
        }
    }
}
