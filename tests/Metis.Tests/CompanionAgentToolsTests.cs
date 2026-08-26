using Metis.Core.Agents;
using Metis.Core.Agents.Tools;
using Metis.Core.Models;
using Metis.Core.Services;
using Xunit;

namespace Metis.Tests;

public sealed class CompanionAgentToolsTests
{
    private static AgentToolContext CreateTestContext() =>
        new("test-task-1", AppContext.BaseDirectory, null, null, null);

    [Fact]
    public async Task PointAtElementTool_WithCoordinates_TriggersGuidanceAndOverlay()
    {
        CompanionGuidance? capturedGuidance = null;
        GuidanceOverlayRequest? capturedOverlay = null;

        var hooks = new CompanionTeachingHooks
        {
            ShowCompanionGuidance = g => capturedGuidance = g,
            ShowOverlay = o => capturedOverlay = o
        };

        var tool = new PointAtElementTool(hooks);
        var args = new Dictionary<string, object?>
        {
            ["label"] = "Settings Button",
            ["x"] = 250,
            ["y"] = 400
        };

        var result = await tool.ExecuteAsync(args, CreateTestContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(capturedGuidance);
        Assert.Equal(250, capturedGuidance.ScreenX);
        Assert.Equal(400, capturedGuidance.ScreenY);
        Assert.Equal("Settings Button", capturedGuidance.Cue);

        Assert.NotNull(capturedOverlay);
        Assert.Single(capturedOverlay.Marks);
        Assert.Equal(250, capturedOverlay.Marks[0].ScreenX);
        Assert.Equal(400, capturedOverlay.Marks[0].ScreenY);
    }

    [Fact]
    public async Task HighlightRegionTool_DrawsMarkWithGivenProperties()
    {
        GuidanceOverlayRequest? capturedOverlay = null;

        var hooks = new CompanionTeachingHooks
        {
            ShowOverlay = o => capturedOverlay = o
        };

        var tool = new HighlightRegionTool(hooks);
        var args = new Dictionary<string, object?>
        {
            ["kind"] = "capsule",
            ["x"] = 100,
            ["y"] = 200,
            ["width"] = 300,
            ["height"] = 50,
            ["label"] = "Search Bar",
            ["step_number"] = 1,
            ["dim_background"] = true
        };

        var result = await tool.ExecuteAsync(args, CreateTestContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(capturedOverlay);
        Assert.True(capturedOverlay.DimBackground);
        Assert.Single(capturedOverlay.Marks);

        var mark = capturedOverlay.Marks[0];
        Assert.Equal(GuidanceMarkKind.Capsule, mark.Kind);
        Assert.Equal(100, mark.ScreenX);
        Assert.Equal(200, mark.ScreenY);
        Assert.Equal(300, mark.Width);
        Assert.Equal(50, mark.Height);
        Assert.Equal("Search Bar", mark.Label);
        Assert.Equal(1, mark.StepNumber);
    }

    [Fact]
    public async Task DrawDiagramTool_RendersPolygonAndCircleMarks()
    {
        GuidanceOverlayRequest? capturedOverlay = null;

        var hooks = new CompanionTeachingHooks
        {
            ShowOverlay = o => capturedOverlay = o,
            GetDiagramCanvas = () => new DiagramCanvas(100, 100, 800)
        };

        var tool = new DrawDiagramTool(hooks);
        var args = new Dictionary<string, object?>
        {
            ["shape"] = "circle",
            ["center_x"] = 500,
            ["center_y"] = 500,
            ["size"] = 250,
            ["label"] = "Cell Nucleus"
        };

        var result = await tool.ExecuteAsync(args, CreateTestContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(capturedOverlay);
        Assert.True(capturedOverlay.Accumulate);
        Assert.Single(capturedOverlay.Marks);

        var mark = capturedOverlay.Marks[0];
        Assert.Equal(GuidanceMarkKind.Polygon, mark.Kind);
        Assert.True(mark.Persistent);
        Assert.Equal("Cell Nucleus", mark.Label);
    }

    [Fact]
    public async Task DemonstrateGestureTool_ValidPoints_DispatchesDemo()
    {
        CompanionDemo? capturedDemo = null;

        var hooks = new CompanionTeachingHooks
        {
            ShowCompanionDemo = d => capturedDemo = d
        };

        var tool = new DemonstrateGestureTool(hooks);
        var args = new Dictionary<string, object?>
        {
            ["points"] = "[[100, 200], [300, 400], [500, 600]]",
            ["label"] = "Swipe Right",
            ["hold_at_end"] = true
        };

        var result = await tool.ExecuteAsync(args, CreateTestContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(capturedDemo);
        Assert.Equal(3, capturedDemo.Path.Count);
        Assert.Equal("Swipe Right", capturedDemo.Label);
        Assert.True(capturedDemo.HoldAtEnd);
    }

    [Fact]
    public async Task ClearAnnotationsTool_TriggersClear()
    {
        bool cleared = false;
        var hooks = new CompanionTeachingHooks
        {
            ClearOverlay = () => cleared = true
        };

        var tool = new ClearAnnotationsTool(hooks);
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>(), CreateTestContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(cleared);
    }

    [Fact]
    public async Task QueryUiElementsTool_FindsHitElement()
    {
        var hooks = new CompanionObservationHooks
        {
            FindUiElementAsync = (q, ct) => Task.FromResult<UiElementHit?>(
                new UiElementHit("SaveButton", "Button", 150, 250, 80, 30))
        };

        var tool = new QueryUiElementsTool(hooks);
        var args = new Dictionary<string, object?> { ["query"] = "Save" };

        var result = await tool.ExecuteAsync(args, CreateTestContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("SaveButton", result.Output);
        Assert.Contains("Button", result.Output);
    }

    [Fact]
    public async Task SubAgentOrchestration_SpawnAndCheckStatus_Works()
    {
        var fakeTask = new AgentTaskRecord(
            Id: "agent-test01",
            Goal: "Organize downloads",
            Status: AgentTaskStatus.Running,
            CreatedAt: DateTimeOffset.Now,
            Progress: 0.5f,
            CurrentActivity: "Sorting images");

        var orchHooks = new SubAgentOrchestrationHooks
        {
            SpawnWorkerAsync = (goal, template, dir, parentId) => Task.FromResult(fakeTask),
            GetWorkerStatus = id => id == "agent-test01" ? fakeTask : null
        };

        var spawnTool = new SpawnBackgroundWorkerTool(orchHooks);
        var spawnRes = await spawnTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["goal"] = "Organize downloads"
        }, CreateTestContext(), CancellationToken.None);

        Assert.True(spawnRes.Success);
        Assert.Contains("agent-test01", spawnRes.Output);

        var checkTool = new CheckWorkerStatusTool(orchHooks);
        var checkRes = await checkTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["task_id"] = "agent-test01"
        }, CreateTestContext(), CancellationToken.None);

        Assert.True(checkRes.Success);
        Assert.Contains("Running", checkRes.Output);
        Assert.Contains("Sorting images", checkRes.Output);
    }

    [Fact]
    public void TeachingSessionManager_MultiStepLifecycle_TransitionsCorrectly()
    {
        var manager = new TeachingSessionManager();

        var steps = new List<LessonStep>
        {
            new("Click File menu", "To open options", "Menu open"),
            new("Click Export", "To start export", "Dialog open"),
            new("Click Save", "To finish", "File saved")
        };

        Assert.False(manager.HasActiveLesson);

        var lesson = manager.StartLesson("Export Video", steps);
        Assert.True(manager.HasActiveLesson);
        Assert.Equal(0, lesson.CurrentIndex);
        Assert.Equal(LessonStatus.Showing, lesson.Status);
        Assert.Equal("Click File menu", lesson.Current?.Instruction);

        manager.WaitForUser();
        Assert.Equal(LessonStatus.Waiting, manager.CurrentLesson?.Status);

        manager.NextStep();
        Assert.Equal(1, manager.CurrentLesson?.CurrentIndex);
        Assert.Equal("Click Export", manager.CurrentLesson?.Current?.Instruction);

        manager.NextStep();
        Assert.Equal(2, manager.CurrentLesson?.CurrentIndex);
        Assert.Equal("Click Save", manager.CurrentLesson?.Current?.Instruction);

        manager.NextStep();
        Assert.True(manager.CurrentLesson?.IsFinished);
        Assert.Equal(LessonStatus.Complete, manager.CurrentLesson?.Status);
        Assert.False(manager.HasActiveLesson);
    }
}
