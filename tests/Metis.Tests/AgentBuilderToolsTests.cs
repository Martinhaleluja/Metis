using Metis.Core.Agents;
using Metis.Core.Agents.Tools;

namespace Metis.Tests;

/// <summary>
/// The tools that decide whether an agent can work on software rather than only
/// move files about: find text inside files, change part of a file without
/// rewriting it, and start something that keeps running so it can be checked.
///
/// These run against a real temporary folder, because what they do is entirely
/// about the filesystem and a mock would only be testing itself.
/// </summary>
public sealed class AgentBuilderToolsTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "metis-tool-tests", Guid.NewGuid().ToString("N")[..8]);

    public AgentBuilderToolsTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // A background process may still hold a handle; the temp folder is
            // not worth failing a test over.
        }
    }

    private AgentToolContext Context(bool allowOutside = false) =>
        new("agent-test", _workspace, null, null, null, allowOutside);

    private string Write(string relativePath, string content)
    {
        var full = Path.Combine(_workspace, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    // ------------------------------------------------------------ searching

    [Fact]
    public async Task Search_finds_text_inside_files_with_line_numbers()
    {
        Write("src/app.ts", "const port = 3000;\nstartServer(port);\n");
        Write("src/other.ts", "// nothing here\n");

        var result = await new SearchContentTool().ExecuteAsync(
            Args(("query", "startServer")), Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("app.ts:2", result.Output);
        Assert.Contains("startServer(port)", result.Output);
    }

    [Fact]
    public async Task Search_skips_the_folders_that_would_drown_it()
    {
        // A recursive search through node_modules returns thousands of matches
        // in code nobody wrote and fills the whole result budget.
        Write("node_modules/lib/index.js", "export const findMe = 1;");
        Write("src/mine.js", "const findMe = 2;");

        var result = await new SearchContentTool().ExecuteAsync(
            Args(("query", "findMe")), Context(), CancellationToken.None);

        Assert.Contains("mine.js", result.Output);
        Assert.DoesNotContain("node_modules", result.Output);
    }

    [Fact]
    public async Task Search_refuses_to_look_outside_the_workspace()
    {
        var result = await new SearchContentTool().ExecuteAsync(
            Args(("query", "password"), ("directory_path", @"C:\Windows")),
            Context(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("outside this agent's workspace", result.ErrorMessage);
    }

    [Fact]
    public async Task A_bad_regular_expression_is_reported_not_thrown()
    {
        var result = await new SearchContentTool().ExecuteAsync(
            Args(("query", "([unclosed"), ("is_regex", true)), Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("valid regular expression", result.ErrorMessage);
    }

    // ------------------------------------------------------------- editing

    [Fact]
    public async Task Editing_changes_only_the_named_text()
    {
        var path = Write("config.json", "{\n  \"port\": 3000,\n  \"host\": \"localhost\"\n}");

        var result = await new EditFileTool().ExecuteAsync(
            Args(("file_path", "config.json"), ("find", "\"port\": 3000"), ("replace_with", "\"port\": 8080")),
            Context(),
            CancellationToken.None);

        Assert.True(result.Success);
        var updated = await File.ReadAllTextAsync(path);
        Assert.Contains("\"port\": 8080", updated);
        Assert.Contains("\"host\": \"localhost\"", updated);
    }

    [Fact]
    public async Task An_ambiguous_edit_is_refused_rather_than_guessed()
    {
        // Picking one of several matches is how an edit silently changes the
        // wrong line, which is worse than failing and saying so.
        Write("app.cs", "var x = 1;\nvar x = 1;\n");

        var result = await new EditFileTool().ExecuteAsync(
            Args(("file_path", "app.cs"), ("find", "var x = 1;"), ("replace_with", "var x = 2;")),
            Context(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("appears 2 times", result.ErrorMessage);
    }

    [Fact]
    public async Task Changing_every_occurrence_is_allowed_when_asked_for_explicitly()
    {
        var path = Write("app.cs", "var x = 1;\nvar x = 1;\n");

        var result = await new EditFileTool().ExecuteAsync(
            Args(("file_path", "app.cs"), ("find", "var x = 1;"), ("replace_with", "var x = 2;"),
                 ("expected_occurrences", 2)),
            Context(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("var x = 1;", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Text_that_is_not_there_says_so()
    {
        Write("app.cs", "var x = 1;");

        var result = await new EditFileTool().ExecuteAsync(
            Args(("file_path", "app.cs"), ("find", "var y = 9;"), ("replace_with", "")),
            Context(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does not appear", result.ErrorMessage);
    }

    [Fact]
    public async Task Editing_outside_the_workspace_is_refused()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"metis-outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "secret");

        try
        {
            var result = await new EditFileTool().ExecuteAsync(
                Args(("file_path", outside), ("find", "secret"), ("replace_with", "changed")),
                Context(),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("secret", await File.ReadAllTextAsync(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task A_task_granted_wider_access_may_edit_outside()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"metis-allowed-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "before");

        try
        {
            var result = await new EditFileTool().ExecuteAsync(
                Args(("file_path", outside), ("find", "before"), ("replace_with", "after")),
                Context(allowOutside: true),
                CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("after", await File.ReadAllTextAsync(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    // -------------------------------------------------- background processes

    [Fact]
    public async Task A_process_outlives_the_call_that_started_it()
    {
        // The whole point: execute_powershell waits and then kills the tree, so
        // a dev server could never be started and then checked. This is that
        // capability in miniature.
        using var registry = new BackgroundProcessRegistry();
        var context = Context();

        var started = await new StartProcessTool(registry).ExecuteAsync(
            Args(("command", "Write-Output 'listening on 3000'; Start-Sleep -Seconds 20")),
            context,
            CancellationToken.None);

        Assert.True(started.Success, started.ErrorMessage);

        var id = started.Output.Split('[', ']')[1];

        var checkResult = await new CheckProcessTool(registry).ExecuteAsync(
            Args(("process_id", id), ("wait_seconds", 3)),
            context,
            CancellationToken.None);

        Assert.True(checkResult.Success);
        Assert.Contains("listening on 3000", checkResult.Output);
        Assert.Contains("still running", checkResult.Output);

        var stopped = await new StopProcessTool(registry).ExecuteAsync(
            Args(("process_id", id)), context, CancellationToken.None);

        Assert.True(stopped.Success);
    }

    [Fact]
    public async Task Checking_an_unknown_process_fails_cleanly()
    {
        using var registry = new BackgroundProcessRegistry();

        var result = await new CheckProcessTool(registry).ExecuteAsync(
            Args(("process_id", "proc-nope")), Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No background process", result.ErrorMessage);
    }

    [Fact]
    public async Task Everything_a_task_started_can_be_stopped_at_once()
    {
        using var registry = new BackgroundProcessRegistry();
        var context = Context();
        var tool = new StartProcessTool(registry);

        await tool.ExecuteAsync(Args(("command", "Start-Sleep -Seconds 30")), context, CancellationToken.None);
        await tool.ExecuteAsync(Args(("command", "Start-Sleep -Seconds 30")), context, CancellationToken.None);

        Assert.Equal(2, registry.StopAllFor("agent-test"));
        Assert.Empty(registry.ListFor("agent-test"));
    }
}
