using System.IO;
using System.Text.Json;
using Metis.Core.Agents;
using Metis.Core.Agents.Tools;
using Xunit;

namespace Metis.Tests;

public sealed class AgentBatchAndToolingTests : IDisposable
{
    private readonly string _testDir;

    public AgentBatchAndToolingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "metis_tooling_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    private AgentToolContext CreateContext(IProgress<string>? progress = null, Action<AgentLogEntry>? logger = null, Action<AgentArtifact>? artifactEmitter = null) =>
        new("batch-task-1", _testDir, progress, logger, artifactEmitter);

    [Fact]
    public async Task ReadFileTool_PaginationAndLineNumbers_WorksCorrectly()
    {
        var testFile = Path.Combine(_testDir, "multiline.txt");
        var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}: Sample data {i * 10}").ToList();
        File.WriteAllLines(testFile, lines);

        var tool = new ReadFileTool();

        // 1. Read lines 10 to 19 (10 lines)
        var args = new Dictionary<string, object?>
        {
            ["file_path"] = "multiline.txt",
            ["start_line"] = 10,
            ["max_lines"] = 10,
            ["show_line_numbers"] = true
        };

        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Line 10: Sample data 100", result.Output);
        Assert.Contains("Line 19: Sample data 190", result.Output);
        Assert.DoesNotContain("Line 9: ", result.Output);
        Assert.DoesNotContain("Line 20: ", result.Output);
    }

    [Fact]
    public async Task ListDirectoryTool_With100PlusItemsAndRecursion_ListsAccurately()
    {
        // Create 120 files across 3 subdirectories
        for (int d = 1; d <= 3; d++)
        {
            var subDir = Path.Combine(_testDir, $"subfolder_{d}");
            Directory.CreateDirectory(subDir);
            for (int f = 1; f <= 40; f++)
            {
                File.WriteAllText(Path.Combine(subDir, $"doc_{f}.txt"), $"File content {f}");
            }
        }

        var tool = new ListDirectoryTool();
        var args = new Dictionary<string, object?>
        {
            ["directory_path"] = ".",
            ["recursive"] = true,
            ["max_items"] = 300,
            ["search_pattern"] = "*.txt"
        };

        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("120 files", result.Output);
        Assert.Contains("doc_1.txt", result.Output);
        Assert.Contains("doc_40.txt", result.Output);
    }

    [Fact]
    public async Task SearchFilesTool_BatchSearchUpTo500FilesWithFilters_FindsMatches()
    {
        // Create 150 files: 100 .json and 50 .csv across subdirectories
        var jsonDir = Path.Combine(_testDir, "json_data");
        var csvDir = Path.Combine(_testDir, "csv_data");
        Directory.CreateDirectory(jsonDir);
        Directory.CreateDirectory(csvDir);

        for (int i = 1; i <= 100; i++)
        {
            File.WriteAllText(Path.Combine(jsonDir, $"item_{i:D3}.json"), $"{{\"id\": {i}, \"name\": \"item {i}\"}}");
        }

        for (int i = 1; i <= 50; i++)
        {
            File.WriteAllText(Path.Combine(csvDir, $"data_{i:D3}.csv"), $"id,value\n{i},{i * 2}");
        }

        var tool = new SearchFilesTool();

        // 1. Search with max_results = 200 and extension filter .json
        var argsJson = new Dictionary<string, object?>
        {
            ["search_pattern"] = "*item*",
            ["directory_path"] = ".",
            ["recursive"] = true,
            ["max_results"] = 200,
            ["extension_filter"] = ".json"
        };

        var resultJson = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);
        Assert.True(resultJson.Success);
        Assert.Contains("Found 100 matching files", resultJson.Output);
        Assert.Contains("item_001.json", resultJson.Output);
        Assert.Contains("item_100.json", resultJson.Output);
        Assert.DoesNotContain(".csv", resultJson.Output);

        // 2. Search CSVs
        var argsCsv = new Dictionary<string, object?>
        {
            ["search_pattern"] = "*.csv",
            ["directory_path"] = ".",
            ["recursive"] = true,
            ["max_results"] = 100
        };

        var resultCsv = await tool.ExecuteAsync(argsCsv, CreateContext(), CancellationToken.None);
        Assert.True(resultCsv.Success);
        Assert.Contains("Found 50 matching files", resultCsv.Output);
    }

    [Fact]
    public async Task ExecutePowerShellTool_MultiCommandScript_ExecutesAndStreamsProgress()
    {
        var progressMessages = new List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));

        var tool = new ExecutePowerShellTool();
        var multiScript = @"
$sum = 0
for ($i = 1; $i -le 5; $i++) {
    $sum += $i
    Write-Output ""Step $i : cumulative $sum""
}
Write-Output ""FINAL_SUM: $sum""
";

        var args = new Dictionary<string, object?>
        {
            ["command"] = multiScript,
            ["timeout_seconds"] = 30
        };

        var context = CreateContext(progress: progress);
        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("FINAL_SUM: 15", result.Output);
        Assert.Contains("Step 1 : cumulative 1", result.Output);
        Assert.Contains("Step 5 : cumulative 15", result.Output);
    }

    [Fact]
    public async Task VerifyTaskOutputTool_FileExistsAndSize_PassesAndFails()
    {
        var targetFile = Path.Combine(_testDir, "verified_file.txt");
        await File.WriteAllTextAsync(targetFile, "Sample output for verification test.");

        var tool = new VerifyTaskOutputTool();

        // 1. Successful verification
        var passArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "file_exists",
            ["target_path"] = "verified_file.txt",
            ["min_size_bytes"] = 10
        };

        var passResult = await tool.ExecuteAsync(passArgs, CreateContext(), CancellationToken.None);
        Assert.True(passResult.Success);
        Assert.Contains("VERIFICATION PASSED", passResult.Output);

        // 2. Failing verification on non-existent file
        var failArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "file_exists",
            ["target_path"] = "non_existent.txt"
        };

        var failResult = await tool.ExecuteAsync(failArgs, CreateContext(), CancellationToken.None);
        Assert.False(failResult.Success);
        Assert.Contains("VERIFICATION FAILED", failResult.ErrorMessage);
    }

    [Fact]
    public async Task VerifyTaskOutputTool_FileContainsAndRegex_PassesAndFails()
    {
        var targetFile = Path.Combine(_testDir, "report.json");
        var jsonContent = "{\n  \"status\": \"completed\",\n  \"items_processed\": 425,\n  \"batch_id\": \"batch-9812\"\n}";
        await File.WriteAllTextAsync(targetFile, jsonContent);

        var tool = new VerifyTaskOutputTool();

        // Substring pass
        var containsArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "file_contains",
            ["target_path"] = "report.json",
            ["expected_text"] = "\"items_processed\": 425"
        };
        var containsRes = await tool.ExecuteAsync(containsArgs, CreateContext(), CancellationToken.None);
        Assert.True(containsRes.Success);

        // Regex pass
        var regexArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "file_regex",
            ["target_path"] = "report.json",
            ["expected_text"] = @"\""batch_id\"":\s*\""batch-\d+\"""
        };
        var regexRes = await tool.ExecuteAsync(regexArgs, CreateContext(), CancellationToken.None);
        Assert.True(regexRes.Success);
        Assert.Contains("batch-9812", regexRes.Output);

        // Min lines pass
        var linesArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "file_min_lines",
            ["target_path"] = "report.json",
            ["min_count"] = 4
        };
        var linesRes = await tool.ExecuteAsync(linesArgs, CreateContext(), CancellationToken.None);
        Assert.True(linesRes.Success);
    }

    [Fact]
    public async Task VerifyTaskOutputTool_JsonValid_ChecksStructureAndKeys()
    {
        var targetFile = Path.Combine(_testDir, "data.json");
        await File.WriteAllTextAsync(targetFile, "{\"success\": true, \"count\": 100, \"items\": [1,2,3]}");

        var tool = new VerifyTaskOutputTool();

        var args = new Dictionary<string, object?>
        {
            ["check_type"] = "json_valid",
            ["target_path"] = "data.json",
            ["expected_text"] = "count"
        };

        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("VERIFICATION PASSED", result.Output);
    }

    [Fact]
    public async Task VerifyTaskOutputTool_CommandSuccess_VerifiesPowerShellExecution()
    {
        var tool = new VerifyTaskOutputTool();

        var args = new Dictionary<string, object?>
        {
            ["check_type"] = "command_success",
            ["command"] = "$val = 40 + 2; Write-Output \"Calculated: $val\"",
            ["expected_text"] = "Calculated: 42"
        };

        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("VERIFICATION PASSED", result.Output);
        Assert.Contains("Calculated: 42", result.Output);
    }

    [Fact]
    public async Task VerifyTaskOutputTool_ExitCodeCheck_PassesAndFails()
    {
        var tool = new VerifyTaskOutputTool();

        var passArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "exit_code",
            ["actual_exit_code"] = 0,
            ["expected_exit_code"] = 0
        };
        var passRes = await tool.ExecuteAsync(passArgs, CreateContext(), CancellationToken.None);
        Assert.True(passRes.Success);

        var failArgs = new Dictionary<string, object?>
        {
            ["check_type"] = "exit_code",
            ["actual_exit_code"] = 1,
            ["expected_exit_code"] = 0
        };
        var failRes = await tool.ExecuteAsync(failArgs, CreateContext(), CancellationToken.None);
        Assert.False(failRes.Success);
        Assert.Contains("Expected exit code 0, but received 1", failRes.ErrorMessage);
    }

    [Fact]
    public async Task ListProcessesTool_SortByMemoryAndFilter_ReturnsAccurateList()
    {
        var tool = new ListProcessesTool();
        var args = new Dictionary<string, object?>
        {
            ["top_count"] = 50,
            ["sort_by"] = "memory"
        };

        var result = await tool.ExecuteAsync(args, CreateContext(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Active System Processes", result.Output);
    }

    [Fact]
    public async Task EmitArtifactTool_InfersMimeTypeAndEmits()
    {
        var targetFile = Path.Combine(_testDir, "summary_report.md");
        await File.WriteAllTextAsync(targetFile, "# Agent Execution Summary\nAll items processed cleanly.");

        AgentArtifact? capturedArtifact = null;
        var tool = new EmitArtifactTool();

        var args = new Dictionary<string, object?>
        {
            ["file_path"] = "summary_report.md",
            ["summary"] = "Final summary of execution results."
        };

        var context = CreateContext(artifactEmitter: a => capturedArtifact = a);
        var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(capturedArtifact);
        Assert.Equal("summary_report.md", capturedArtifact.Name);
        Assert.Equal("text/markdown", capturedArtifact.MimeType);
        Assert.Equal("Final summary of execution results.", capturedArtifact.Summary);
    }

    [Fact]
    public async Task SubAgentOrchestration_ListAndCancelWorkers_Works()
    {
        var worker1 = new AgentTaskRecord("worker-01", "Process batch A", AgentTaskStatus.Running, DateTimeOffset.Now, Progress: 0.25f);
        var worker2 = new AgentTaskRecord("worker-02", "Process batch B", AgentTaskStatus.Running, DateTimeOffset.Now, Progress: 0.80f);

        string? cancelledId = null;

        var hooks = new SubAgentOrchestrationHooks
        {
            ListActiveWorkers = () => new[] { worker1, worker2 },
            CancelWorker = id => cancelledId = id
        };

        var listTool = new ListWorkersTool(hooks);
        var listRes = await listTool.ExecuteAsync(new Dictionary<string, object?>(), CreateContext(), CancellationToken.None);
        Assert.True(listRes.Success);
        Assert.Contains("worker-01", listRes.Output);
        Assert.Contains("worker-02", listRes.Output);
        Assert.Contains("Process batch A", listRes.Output);

        var cancelTool = new CancelWorkerTool(hooks);
        var cancelRes = await cancelTool.ExecuteAsync(new Dictionary<string, object?> { ["task_id"] = "worker-01" }, CreateContext(), CancellationToken.None);
        Assert.True(cancelRes.Success);
        Assert.Equal("worker-01", cancelledId);
    }
}
