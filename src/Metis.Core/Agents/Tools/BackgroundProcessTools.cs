using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Processes that outlive the tool call that started them.
///
/// Nothing could before. <c>execute_powershell</c> waits for the process to
/// exit, caps at ten minutes, and kills the whole tree on the way out — so
/// <c>npm run dev</c> blocked for ten minutes, was killed, and came back as
/// "Execution timed out". That single limitation is what made "build me a web
/// app" impossible: an agent could write the code and even compile it, but
/// could never start it and see whether it actually served a page. It had no
/// way to reach the only question that matters.
///
/// A server started here keeps running, its output accumulates where the agent
/// can read it, and it is stopped when the task ends whether or not the agent
/// remembered to.
/// </summary>
public sealed class BackgroundProcessRegistry : IDisposable
{
    private sealed class Entry : IDisposable
    {
        public required string Id { get; init; }
        public required string TaskId { get; init; }
        public required string Command { get; init; }
        public required Process Process { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public StringBuilder Output { get; } = new();
        public object Gate { get; } = new();

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Already gone, or not ours to kill any more.
            }

            Process.Dispose();
        }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// How much output is kept per process. Enough to see a stack trace and the
    /// lines around it; not so much that a chatty server fills memory over an
    /// afternoon.
    /// </summary>
    private const int MaxRetainedChars = 200_000;

    public string Start(string taskId, string command, string workingDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = $"proc-{Guid.NewGuid().ToString("N")[..6]}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var entry = new Entry
        {
            Id = id,
            TaskId = taskId,
            Command = command,
            Process = process,
            StartedAt = DateTimeOffset.Now
        };

        void Capture(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (entry.Gate)
            {
                entry.Output.AppendLine(line);

                // Keep the tail. For a server the recent output is the useful
                // part, and the startup banner has usually already been read.
                if (entry.Output.Length > MaxRetainedChars)
                {
                    entry.Output.Remove(0, entry.Output.Length - MaxRetainedChars);
                }
            }
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _entries[id] = entry;
        return id;
    }

    public (bool Found, bool Running, int? ExitCode, string Output, TimeSpan Age, string Command) Peek(
        string id,
        int maxChars)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return (false, false, null, string.Empty, TimeSpan.Zero, string.Empty);
        }

        string text;
        lock (entry.Gate)
        {
            text = entry.Output.ToString();
        }

        var running = !SafeHasExited(entry.Process);
        int? exitCode = running ? null : SafeExitCode(entry.Process);

        return (
            true,
            running,
            exitCode,
            ToolOutputDigest.Summarize(text, maxChars),
            DateTimeOffset.Now - entry.StartedAt,
            entry.Command);
    }

    public bool Stop(string id)
    {
        if (!_entries.TryRemove(id, out var entry))
        {
            return false;
        }

        entry.Dispose();
        return true;
    }

    /// <summary>Everything this task left running, so nothing is orphaned.</summary>
    public int StopAllFor(string taskId)
    {
        var stopped = 0;

        foreach (var pair in _entries.ToArray())
        {
            if (!string.Equals(pair.Value.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_entries.TryRemove(pair.Key, out var entry))
            {
                entry.Dispose();
                stopped++;
            }
        }

        return stopped;
    }

    public IReadOnlyList<(string Id, string Command, bool Running)> ListFor(string taskId) =>
        _entries.Values
            .Where(e => string.Equals(e.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Id, e.Command, !SafeHasExited(e.Process)))
            .ToList();

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
    }
}

/// <summary>Starts something that keeps running: a dev server, a watcher, a long build.</summary>
public sealed class StartProcessTool(BackgroundProcessRegistry registry) : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "start_process",
        Description: "Starts a long-running command in the background and returns immediately with a process id. Use this for dev servers and watchers - anything that does not exit on its own. Read its output with check_process.",
        Category: "process",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("command", "string", "The command to run, such as 'npm run dev'", Required: true),
            new("working_directory", "string", "Folder to run it in. Defaults to the workspace.", Required: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var command = arguments.GetValueOrDefault("command")?.ToString();
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'command' is required."));
        }

        var workingDir = context.WorkingDirectory;
        var raw = arguments.GetValueOrDefault("working_directory")?.ToString();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var decision = context.ResolvePath(raw);
            if (!decision.Allowed)
            {
                return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
            }

            workingDir = decision.FullPath!;
        }

        try
        {
            var id = registry.Start(context.TaskId, command, workingDir);
            return Task.FromResult(AgentToolResult.Ok(
                $"Started [{id}]: {command}\n"
                + "It is running in the background. Give it a few seconds, then use check_process to read its output."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(AgentToolResult.Fail($"Could not start that: {exception.Message}"));
        }
    }
}

/// <summary>Reads what a background process has printed, and whether it is still alive.</summary>
public sealed class CheckProcessTool(BackgroundProcessRegistry registry) : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "check_process",
        Description: "Reads the output of a background process started with start_process, and reports whether it is still running.",
        Category: "process",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("process_id", "string", "The id returned by start_process", Required: true),
            new("max_characters", "number", "How much output to return (default 2000)", Required: false),
            new("wait_seconds", "number", "Wait this long before reading, to give it time to start (max 30)", Required: false)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var id = arguments.GetValueOrDefault("process_id")?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return AgentToolResult.Fail("Parameter 'process_id' is required.");
        }

        var wait = SearchContentTool.ReadInt(arguments, "wait_seconds", 0, 0, 30);
        if (wait > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken);
        }

        var maxChars = SearchContentTool.ReadInt(arguments, "max_characters", 2000, 200, 16000);
        var (found, running, exitCode, output, age, command) = registry.Peek(id, maxChars);

        if (!found)
        {
            return AgentToolResult.Fail($"No background process with id '{id}'. It may already have been stopped.");
        }

        var state = running
            ? $"still running after {age.TotalSeconds:F0}s"
            : $"exited with code {exitCode?.ToString() ?? "unknown"} after {age.TotalSeconds:F0}s";

        var body = string.IsNullOrWhiteSpace(output) ? "(no output yet)" : output;

        return AgentToolResult.Ok($"[{id}] {command} — {state}.\n{body}");
    }
}

/// <summary>Stops a background process.</summary>
public sealed class StopProcessTool(BackgroundProcessRegistry registry) : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "stop_process",
        Description: "Stops a background process started with start_process.",
        Category: "process",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("process_id", "string", "The id returned by start_process", Required: true)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var id = arguments.GetValueOrDefault("process_id")?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'process_id' is required."));
        }

        return Task.FromResult(registry.Stop(id)
            ? AgentToolResult.Ok($"Stopped [{id}].")
            : AgentToolResult.Fail($"No background process with id '{id}'."));
    }
}
