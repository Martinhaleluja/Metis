using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Metis.Core.Agents.Tools;

namespace Metis.Core.Agents;

/// <summary>
/// Central manager and orchestrator for all background autonomous agents in Metis.
/// Supports multi-tasking with concurrent parallel workers, 100+ turns per task, and lock-free thread safety.
/// </summary>
public sealed class AgentTaskManager : IDisposable
{
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IAgentReasoningClient _reasoningClient;
    private readonly string _storageFolder;
    private readonly string _storageFilePath;
    private readonly ConcurrentDictionary<string, AgentTaskRecord> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (AutonomousAgentWorker Worker, CancellationTokenSource Cts)> _activeWorkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingApprovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveLock = new();
    private bool _disposed;

    public event EventHandler<AgentTaskRecord>? TaskCreated;
    public event EventHandler<AgentTaskRecord>? TaskUpdated;
    public event EventHandler<AgentApprovalRequest>? ApprovalRequested;
    public event EventHandler<AgentTaskRecord>? TaskCompleted;
    public event EventHandler<AgentTaskRecord>? TaskFailed;
    public event EventHandler<AgentTaskRecord>? TaskCancelled;

    public Func<string>? GetAutonomyMode { get; set; }
    public int MaxTurnsPerTask { get; set; } = 100;

    public IAgentToolRegistry ToolRegistry => _toolRegistry;

    public AgentTaskManager(
        IAgentReasoningClient reasoningClient,
        IAgentToolRegistry? toolRegistry = null,
        string? storageFolder = null)
    {
        _reasoningClient = reasoningClient ?? throw new ArgumentNullException(nameof(reasoningClient));
        _toolRegistry = toolRegistry ?? CreateDefaultToolRegistry();

        _storageFolder = storageFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Metis",
            "agents");

        _storageFilePath = Path.Combine(_storageFolder, "tasks.json");
        LoadPersistedTasks();
    }

    public void RegisterCompanionTools(
        CompanionTeachingHooks? teachingHooks = null,
        CompanionObservationHooks? observationHooks = null,
        SubAgentOrchestrationHooks? orchestrationHooks = null)
    {
        // Companion Teaching Tools
        _toolRegistry.Register(new PointAtElementTool(teachingHooks));
        _toolRegistry.Register(new HighlightRegionTool(teachingHooks));
        _toolRegistry.Register(new DrawDiagramTool(teachingHooks));
        _toolRegistry.Register(new DemonstrateGestureTool(teachingHooks));
        _toolRegistry.Register(new ClearAnnotationsTool(teachingHooks));

        // Companion Observation Tools
        _toolRegistry.Register(new InspectScreenTool(observationHooks));
        _toolRegistry.Register(new QueryUiElementsTool(observationHooks));

        // Sub-Agent Orchestration Tools
        _toolRegistry.Register(new SpawnBackgroundWorkerTool(orchestrationHooks));
        _toolRegistry.Register(new CheckWorkerStatusTool(orchestrationHooks));
        _toolRegistry.Register(new ListWorkersTool(orchestrationHooks));
        _toolRegistry.Register(new CancelWorkerTool(orchestrationHooks));
    }

    /// <summary>
    /// Not static any more: the process tools need the registry that ties a
    /// running server to the task that started it.
    /// </summary>
    private IAgentToolRegistry CreateDefaultToolRegistry()
    {
        var registry = new AgentToolRegistry();
        // File system tools
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new ListDirectoryTool());
        registry.Register(new SearchFilesTool());
        registry.Register(new MoveFileTool());
        registry.Register(new DeleteFileTool());
        // Process & Shell tools
        registry.Register(new ExecutePowerShellTool());
        // Verification tools
        registry.Register(new VerifyTaskOutputTool());
        // Web tools
        registry.Register(new WebSearchTool());
        registry.Register(new FetchUrlContentTool());
        registry.Register(new DownloadFileTool());
        // System tools
        registry.Register(new ListProcessesTool());
        registry.Register(new EmitArtifactTool());
        // Companion Teaching Tools with default fallback hooks
        registry.Register(new PointAtElementTool());
        registry.Register(new HighlightRegionTool());
        registry.Register(new DrawDiagramTool());
        registry.Register(new DemonstrateGestureTool());
        registry.Register(new ClearAnnotationsTool());
        // Companion Observation Tools with default fallback hooks
        registry.Register(new InspectScreenTool());
        registry.Register(new QueryUiElementsTool());
        // The three tools that let an agent work on software rather than only
        // on files: find text inside files, change part of a file, and start
        // something that keeps running so it can be checked afterwards.
        registry.Register(new SearchContentTool());
        registry.Register(new EditFileTool());
        registry.Register(new StartProcessTool(BackgroundProcesses));
        registry.Register(new CheckProcessTool(BackgroundProcesses));
        registry.Register(new StopProcessTool(BackgroundProcesses));
        registry.Register(new BrowserOpenTool(Browsers));
        registry.Register(new BrowserReadTool(Browsers));
        registry.Register(new BrowserClickTool(Browsers));
        registry.Register(new BrowserTypeTool(Browsers));
        // Sub-Agent Orchestration Tools with default fallback hooks
        registry.Register(new SpawnBackgroundWorkerTool());
        registry.Register(new CheckWorkerStatusTool());
        registry.Register(new ListWorkersTool());
        registry.Register(new CancelWorkerTool());
        return registry;
    }

    private void LoadPersistedTasks()
    {
        try
        {
            if (File.Exists(_storageFilePath))
            {
                var json = File.ReadAllText(_storageFilePath);
                var loaded = JsonSerializer.Deserialize<List<AgentTaskRecord>>(json);
                if (loaded is not null)
                {
                    foreach (var task in loaded)
                    {
                        // If a task was running when app exited, mark it as Interrupted/Failed
                        var normalized = task.IsActive
                            ? task with { Status = AgentTaskStatus.Failed, ErrorMessage = "Interrupted by application restart." }
                            : task;
                        _tasks[normalized.Id] = normalized;
                    }
                }
            }
        }
        catch
        {
            // Ignore load errors on corrupted store
        }
    }

    public void QueueSaveToDisk()
    {
        Task.Run(SaveTasksToDisk);
    }

    private void SaveTasksToDisk()
    {
        lock (_saveLock)
        {
            try
            {
                if (!Directory.Exists(_storageFolder))
                {
                    Directory.CreateDirectory(_storageFolder);
                }

                var snapshot = _tasks.Values.OrderByDescending(t => t.CreatedAt).Take(150).ToList();
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storageFilePath, json);
            }
            catch
            {
                // Non-fatal if persistence fails
            }
        }
    }

    /// <summary>
    /// Spawns a new autonomous agent in the background.
    /// Supports unlimited concurrent agents running in parallel.
    /// </summary>
    /// <summary>
    /// How deep the chain of agents spawning agents may go.
    ///
    /// One helper is a reasonable thing for an agent to want. A helper that
    /// wants a helper is the start of a tree that nothing was stopping, since
    /// the sub-agent tool called straight back into this method with no notion
    /// of depth at all.
    /// </summary>
    /// <summary>
    /// Long-running processes agents have started.
    ///
    /// Held here rather than per-worker so that a dev server can be stopped
    /// when its task ends, whether the agent thought to stop it or not. An
    /// orphaned server holding a port is exactly the kind of mess an autonomous
    /// tool should not be able to leave behind.
    /// </summary>
    public BackgroundProcessRegistry BackgroundProcesses { get; } = new();

    /// <summary>
    /// Browsers agents are driving. Empty until a browser implementation is
    /// supplied by the host, which is how Core stays free of any browser
    /// library — the same arrangement the companion tools use.
    /// </summary>
    public BrowserSessions Browsers { get; private set; } = new(null);

    /// <summary>Gives agents a real browser. Called once at startup.</summary>
    public void UseBrowser(Metis.Core.Agents.Browsing.IBrowserSessionFactory factory)
    {
        Browsers = new BrowserSessions(factory);

        _toolRegistry.Register(new BrowserOpenTool(Browsers));
        _toolRegistry.Register(new BrowserReadTool(Browsers));
        _toolRegistry.Register(new BrowserClickTool(Browsers));
        _toolRegistry.Register(new BrowserTypeTool(Browsers));
    }

    public const int MaxSpawnDepth = 1;

    /// <summary>
    /// How much latitude an agent gets, given who asked for it.
    ///
    /// FullAutonomy is a statement the user makes about goals they wrote
    /// themselves. It cannot carry over to a goal the model composed, because
    /// the model composes it from a conversation that also contains everything
    /// visible on screen — a document, a web page, an email — and none of that
    /// is the user speaking. So a model-proposed agent, and any helper an agent
    /// asks for, is held at AskApproval however the setting is configured. It
    /// still runs; it just pauses before anything that could not be undone.
    ///
    /// Strict is never loosened, because that direction is always the user
    /// asking for more caution rather than less.
    /// </summary>
    private string ResolveAutonomyMode(AgentSpawnOrigin origin)
    {
        var configured = GetAutonomyMode?.Invoke() ?? "AskApproval";

        if (origin is AgentSpawnOrigin.Panel or AgentSpawnOrigin.SlashCommand)
        {
            return configured;
        }

        return string.Equals(configured, "Strict", StringComparison.OrdinalIgnoreCase)
            ? configured
            : "AskApproval";
    }

    public AgentTaskRecord SpawnTask(
        string goal,
        string? templateId = null,
        string? workingDir = null,
        int? maxTurns = null,
        AgentSpawnOrigin origin = AgentSpawnOrigin.Panel,
        int depth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);

        if (depth > MaxSpawnDepth)
        {
            throw new InvalidOperationException(
                $"Agents may only be nested {MaxSpawnDepth} deep. Refusing to spawn at depth {depth}.");
        }

        var taskId = $"agent-{Guid.NewGuid().ToString("N")[..8]}";

        // No folder chosen means the agent gets one of its own, and is confined
        // to it. This used to default to the user's entire profile with no
        // containment at all, so an agent asked to write a report could write
        // anywhere in it.
        //
        // Choosing a folder is the user saying "work in here", and that is the
        // only thing that unlocks reading and writing outside the workspace --
        // the permission travels with the task rather than being a global
        // setting somebody forgets is on.
        var chose = !string.IsNullOrWhiteSpace(workingDir);
        var resolvedDir = chose ? workingDir! : AgentWorkspace.RootFor(taskId);

        try
        {
            Directory.CreateDirectory(resolvedDir);
        }
        catch
        {
            // The worker reports this properly when it starts; failing the
            // spawn outright would lose the goal for a folder problem.
        }
        var turns = maxTurns.GetValueOrDefault(MaxTurnsPerTask);
        if (turns <= 0) turns = MaxTurnsPerTask;

        var record = new AgentTaskRecord(
            Id: taskId,
            Goal: goal.Trim(),
            Status: AgentTaskStatus.Queued,
            CreatedAt: DateTimeOffset.Now,
            TemplateId: templateId,
            WorkingDirectory: resolvedDir,
            Progress: 0f,
            CurrentActivity: "Queued",
            MaxTurns: turns,
            Origin: origin,
            Depth: depth,
            AllowOutsideWorkspace: chose);

        var cts = new CancellationTokenSource();

        var worker = new AutonomousAgentWorker(
            record,
            _toolRegistry,
            _reasoningClient,
            onTaskUpdated: updated =>
            {
                var oldStatus = _tasks.TryGetValue(updated.Id, out var prev) ? prev.Status : AgentTaskStatus.Queued;
                _tasks[updated.Id] = updated;

                TaskUpdated?.Invoke(this, updated);
                QueueSaveToDisk();

                if (oldStatus != updated.Status)
                {
                    if (updated.Status == AgentTaskStatus.Completed)
                    {
                        TaskCompleted?.Invoke(this, updated);
                    }
                    else if (updated.Status == AgentTaskStatus.Failed)
                    {
                        TaskFailed?.Invoke(this, updated);
                    }
                    else if (updated.Status == AgentTaskStatus.Cancelled)
                    {
                        TaskCancelled?.Invoke(this, updated);
                    }
                }
            },
            onApprovalRequired: req =>
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingApprovals[req.TaskId] = tcs;

                AgentTaskRecord? updated = null;
                if (_tasks.TryGetValue(req.TaskId, out var current))
                {
                    updated = current with { PendingApproval = req, Status = AgentTaskStatus.AwaitingApproval };
                    _tasks[req.TaskId] = updated;
                }

                if (updated is not null)
                {
                    TaskUpdated?.Invoke(this, updated);
                }

                ApprovalRequested?.Invoke(this, req);
                return tcs.Task;
            },
            // Origin is folded in here rather than inside the worker, so the
            // worker keeps asking one simple question and the policy about who
            // earns full autonomy lives in one place.
            getAutonomyMode: () => ResolveAutonomyMode(origin),
            maxTurns: turns);

        _tasks[taskId] = record;
        _activeWorkers[taskId] = (worker, cts);

        TaskCreated?.Invoke(this, record);
        QueueSaveToDisk();

        // Launch concurrent background task
        _ = Task.Run(async () =>
        {
            try
            {
                await worker.RunAsync(cts.Token);
            }
            finally
            {
                // Anything the agent left running goes with it. A dev server
                // that outlives its task holds a port and shows up as a
                // mysterious process nobody started.
                await Browsers.CloseAsync(taskId);

                var orphans = BackgroundProcesses.StopAllFor(taskId);
                if (orphans > 0)
                {
                    Debug.WriteLine($"[Metis] Stopped {orphans} background process(es) left by {taskId}.");
                }

                _activeWorkers.TryRemove(taskId, out _);
                worker.Dispose();
                cts.Dispose();
            }
        });

        return record;
    }

    public void PauseTask(string taskId)
    {
        if (_activeWorkers.TryGetValue(taskId, out var item))
        {
            item.Worker.Pause();
        }
    }

    public void ResumeTask(string taskId)
    {
        if (_activeWorkers.TryGetValue(taskId, out var item))
        {
            item.Worker.Resume();
        }
    }

    public void CancelTask(string taskId)
    {
        if (_activeWorkers.TryGetValue(taskId, out var item))
        {
            try
            {
                item.Cts.Cancel();
            }
            catch { }
        }
    }

    /// <summary>
    /// Emergency Stop: cancels all active background agent tasks immediately.
    /// </summary>
    public void CancelAll()
    {
        var activeSnapshots = _activeWorkers.Values.ToList();

        foreach (var (_, cts) in activeSnapshots)
        {
            try
            {
                cts.Cancel();
            }
            catch { }
        }
    }

    public void ApproveAction(string taskId, bool approved)
    {
        _pendingApprovals.TryRemove(taskId, out var tcs);
        AutonomousAgentWorker? worker = null;

        if (_activeWorkers.TryGetValue(taskId, out var item))
        {
            worker = item.Worker;
        }

        AgentTaskRecord? updated = null;
        if (_tasks.TryGetValue(taskId, out var current))
        {
            updated = current with { PendingApproval = null };
            _tasks[taskId] = updated;
        }

        tcs?.TrySetResult(approved);
        worker?.ResolveApproval(approved);

        if (updated is not null)
        {
            TaskUpdated?.Invoke(this, updated);
        }
    }

    public IReadOnlyList<AgentTaskRecord> GetAllTasks()
    {
        return _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public IReadOnlyList<AgentTaskRecord> GetActiveTasks()
    {
        return _tasks.Values.Where(t => t.IsActive).OrderByDescending(t => t.CreatedAt).ToList();
    }

    public AgentTaskRecord? GetTask(string taskId)
    {
        return _tasks.GetValueOrDefault(taskId);
    }

    public void DeleteTaskHistory(string taskId)
    {
        if (_activeWorkers.TryGetValue(taskId, out var item))
        {
            try
            {
                item.Cts.Cancel();
            }
            catch { }
        }

        _tasks.TryRemove(taskId, out _);
        QueueSaveToDisk();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAll();
        BackgroundProcesses.Dispose();
    }
}
