using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Metis.App.Runtime;
using Metis.Core.Agents;

using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

using System.Diagnostics;

using System.IO;

namespace Metis.App.Windows;

public sealed record AgentStepViewModel(
    int Index,
    string StepNumberText,
    string Description,
    string ToolInfo,
    string StatusText,
    Brush StatusBrush,
    Brush StatusBgBrush,
    Visibility ErrorVisibility,
    string ErrorMessage);

/// <summary>A file an agent produced, and where it is.</summary>
public sealed record AgentArtifactViewModel(
    string Name,
    string FilePath,
    string SizeText,
    string Summary);

public sealed record AgentTaskViewModel(
    string Id,
    string Goal,
    string StatusText,
    Brush StatusBrush,
    double ProgressValue,
    string ProgressPercentText,
    string CurrentActivity,
    Visibility StepsVisibility,
    string StepsSummaryText,
    IReadOnlyList<AgentStepViewModel> Steps,
    Visibility ApprovalVisibility,
    string? ApprovalReason,
    Visibility PauseResumeVisibility,
    string PauseResumeLabel,
    Visibility CancelVisibility,
    Visibility RetryVisibility,
    string TimeAgoText,

    /// <summary>
    /// What is actually being approved. The card used to show only the reason,
    /// so the user was asked to allow something without being told which tool
    /// it was or what arguments it had — which is not consent, it is a guess.
    /// </summary>
    string ApprovalTool,
    string ApprovalArguments,
    string ApprovalRisk,

    /// <summary>
    /// The files the agent produced. Nothing in the interface showed these at
    /// all: an agent could write a report and the only way to find it was to go
    /// looking on disk.
    /// </summary>
    IReadOnlyList<AgentArtifactViewModel> Artifacts,
    Visibility ArtifactsVisibility,
    string ArtifactsSummaryText,

    /// <summary>Where the agent is working, which was never shown either.</summary>
    string WorkingDirectory,

    /// <summary>How long it took, rather than two clock times to subtract by eye.</summary>
    string DurationText,

    /// <summary>Who asked for this agent — the panel, a command, or Metis itself.</summary>
    string OriginText,

    /// <summary>Finished tasks can be cleared out; running ones cannot.</summary>
    Visibility DismissVisibility);

public partial class NotchAgentDrawer : UserControl
{
    private MetisRuntime? _runtime;
    private readonly ObservableCollection<AgentTaskViewModel> _tasks = [];

    public event EventHandler? CloseRequested;
    public event EventHandler? SpawnAgentRequested;
    public event EventHandler? ContentSizeChanged;

    public NotchAgentDrawer()
    {
        InitializeComponent();
        TasksList.ItemsSource = _tasks;
    }

    public void Attach(MetisRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;

        if (runtime.AgentTasks is not null)
        {
            runtime.AgentTasks.TaskCreated += (_, _) => Dispatcher.InvokeAsync(RefreshTasks);
            runtime.AgentTasks.TaskUpdated += (_, _) => Dispatcher.InvokeAsync(RefreshTasks);
            runtime.AgentTasks.TaskCompleted += (_, _) => Dispatcher.InvokeAsync(RefreshTasks);
            runtime.AgentTasks.TaskCancelled += (_, _) => Dispatcher.InvokeAsync(RefreshTasks);
            runtime.AgentTasks.ApprovalRequested += (_, _) => Dispatcher.InvokeAsync(RefreshTasks);
        }

        RefreshTasks();
    }

    public double MeasureDesiredHeight(double width)
    {
        Measure(new System.Windows.Size(Math.Max(width, 1), double.PositiveInfinity));
        return DesiredSize.Height;
    }

    /// <summary>
    /// One brush per colour, made once and frozen.
    ///
    /// RefreshTasks rebuilds every card on every agent event, and it used to
    /// allocate a new SolidColorBrush for each task and each step as it went —
    /// so a running agent with a hundred steps churned a couple of hundred
    /// brushes a tick. Worse, because the view models hold Brush by reference,
    /// two freshly-allocated identical brushes never compared equal, which
    /// defeated the change check underneath and forced a full re-render anyway.
    /// </summary>
    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    private static Brush Swatch(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        BrushCache[hex] = brush;
        return brush;
    }

    private static Brush StatusBrushFor(AgentTaskStatus status) => status switch
    {
        AgentTaskStatus.Running => Swatch("#0A7CFF"),
        AgentTaskStatus.Completed => Swatch("#30D158"),
        AgentTaskStatus.AwaitingApproval => Swatch("#FF9F0A"),
        AgentTaskStatus.Failed => Swatch("#FF6257"),
        AgentTaskStatus.Paused => Swatch("#FFD60A"),
        _ => Swatch("#8E8E93")
    };

    /// <summary>A duration a person can read, rather than two clock times to subtract.</summary>
    private static string Describe(TimeSpan span) => span.TotalSeconds switch
    {
        < 1 => "under a second",
        < 60 => $"{span.TotalSeconds:F0}s",
        < 3600 => $"{span.Minutes}m {span.Seconds}s",
        _ => $"{(int)span.TotalHours}h {span.Minutes}m"
    };

    public void RefreshTasks()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(RefreshTasks);
            return;
        }

        if (_runtime?.AgentTasks is null) return;

        var all = _runtime.AgentTasks.GetAllTasks();
        var newVms = new List<AgentTaskViewModel>(all.Count);

        foreach (var task in all)
        {
            var statusBrush = StatusBrushFor(task.Status);

            var pauseResumeLabel = task.Status == AgentTaskStatus.Paused ? "Resume" : "Pause";
            var pauseResumeVis = task.IsActive ? Visibility.Visible : Visibility.Collapsed;
            var cancelVis = task.IsActive ? Visibility.Visible : Visibility.Collapsed;
            var retryVis = task.Status == AgentTaskStatus.Failed ? Visibility.Visible : Visibility.Collapsed;
            var approvalVis = task.Status == AgentTaskStatus.AwaitingApproval && task.PendingApproval is not null
                ? Visibility.Visible
                : Visibility.Collapsed;

            var timeAgo = $"Started {task.CreatedAt:HH:mm:ss}";
            if (task.CompletedAt.HasValue)
            {
                timeAgo += $" · Finished {task.CompletedAt.Value:HH:mm:ss}";
            }

            var steps = task.AllSteps;
            var stepVms = new List<AgentStepViewModel>(steps.Count);
            var completedSteps = 0;

            for (var sIdx = 0; sIdx < steps.Count; sIdx++)
            {
                var step = steps[sIdx];
                if (step.Status == AgentStepStatus.Success) completedSteps++;

                var stepBrushHex = step.Status switch
                {
                    AgentStepStatus.Success => "#30D158",
                    AgentStepStatus.Running => "#0A7CFF",
                    AgentStepStatus.Failed => "#FF6257",
                    AgentStepStatus.Skipped => "#8E8E93",
                    _ => "#8E8E93"
                };

                var stepBgHex = step.Status switch
                {
                    AgentStepStatus.Success => "#2630D158",
                    AgentStepStatus.Running => "#260A7CFF",
                    AgentStepStatus.Failed => "#26FF6257",
                    AgentStepStatus.Skipped => "#1A8E8E93",
                    _ => "#1AFFFFFF"
                };

                var stepStatusLabel = step.Status switch
                {
                    AgentStepStatus.Success => "✓ VERIFIED",
                    AgentStepStatus.Running => "▶ RUNNING",
                    AgentStepStatus.Failed => "✕ FAILED",
                    AgentStepStatus.Skipped => "↷ SKIPPED",
                    _ => "⋯ PENDING"
                };

                var toolInfo = string.Empty;
                if (!string.IsNullOrWhiteSpace(step.ToolName))
                {
                    toolInfo = $"Tool: {step.ToolName}";
                    if (!string.IsNullOrWhiteSpace(step.ToolArguments))
                    {
                        var argsClean = step.ToolArguments.Replace("\r", " ").Replace("\n", " ");
                        if (argsClean.Length > 80) argsClean = argsClean[..77] + "…";
                        toolInfo += $" ({argsClean})";
                    }
                }

                stepVms.Add(new AgentStepViewModel(
                    sIdx + 1,
                    $"#{sIdx + 1}",
                    step.Description,
                    toolInfo,
                    stepStatusLabel,
                    Swatch(stepBrushHex),
                    Swatch(stepBgHex),
                    string.IsNullOrWhiteSpace(step.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible,
                    step.ErrorMessage ?? string.Empty));
            }

            var artifactVms = task.AllArtifacts
                .Select(a => new AgentArtifactViewModel(
                    a.Name,
                    a.FilePath,
                    a.SizeBytes < 1024 ? $"{a.SizeBytes} B" : $"{a.SizeBytes / 1024.0:F1} KB",
                    a.Summary ?? string.Empty))
                .ToList();

            var started = task.StartedAt ?? task.CreatedAt;
            var duration = task.CompletedAt.HasValue
                ? $"took {Describe(task.CompletedAt.Value - started)}"
                : task.IsActive
                    ? $"running for {Describe(DateTimeOffset.Now - started)}"
                    : string.Empty;

            var progressPercent = Math.Clamp((int)Math.Round(task.Progress * 100), 0, 100);
            var stepsSummary = steps.Count > 0
                ? $"Steps ({completedSteps}/{steps.Count} verified)"
                : "Execution Plan";

            newVms.Add(new AgentTaskViewModel(
                task.Id,
                task.Goal,
                task.Status.ToString().ToUpperInvariant(),
                statusBrush,
                progressPercent,
                $"{progressPercent}%",
                task.CurrentActivity ?? (task.Status == AgentTaskStatus.Completed ? task.ResultSummary ?? "Complete" : string.Empty),
                steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
                stepsSummary,
                stepVms,
                approvalVis,
                task.PendingApproval?.Reason ?? "High risk action requires confirmation.",
                pauseResumeVis,
                pauseResumeLabel,
                cancelVis,
                retryVis,
                timeAgo,
                task.PendingApproval?.ToolName ?? string.Empty,
                Shorten(task.PendingApproval?.Arguments, 220),
                task.PendingApproval is null ? string.Empty : $"{task.PendingApproval.RiskLevel} risk",
                artifactVms,
                artifactVms.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
                artifactVms.Count == 1 ? "1 file produced" : $"{artifactVms.Count} files produced",
                task.WorkingDirectory ?? string.Empty,
                duration,
                OriginLabel(task.Origin, task.Depth),
                task.IsActive ? Visibility.Collapsed : Visibility.Visible));
        }

        // Any change at all, not just a change in how many there are.
        //
        // This used to compare only the counts, which meant a card that *grew* —
        // steps arriving, an approval banner opening, a file being written —
        // never told the notch to make room, and the new content was silently
        // cut off by the body's clip until some unrelated task happened to be
        // added or removed. The records are value-equal, so this is the same
        // comparison the loop below already makes.
        var layoutChanged = _tasks.Count != newVms.Count;

        for (var i = 0; i < newVms.Count; i++)
        {
            if (i < _tasks.Count)
            {
                if (_tasks[i] != newVms[i])
                {
                    _tasks[i] = newVms[i];
                    layoutChanged = true;
                }
            }
            else
            {
                _tasks.Add(newVms[i]);
                layoutChanged = true;
            }
        }

        while (_tasks.Count > newVms.Count)
        {
            _tasks.RemoveAt(_tasks.Count - 1);
        }

        EmptyState.Visibility = _tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (layoutChanged)
        {
            ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Where the goal came from, in words. This drives how much the agent is
    /// allowed to do unattended, so it is worth the user being able to see it.
    /// </summary>
    private static string OriginLabel(AgentSpawnOrigin origin, int depth) => origin switch
    {
        AgentSpawnOrigin.Panel => "you started this",
        AgentSpawnOrigin.SlashCommand => "you asked for this",
        AgentSpawnOrigin.ModelProposed => "Metis suggested this",
        AgentSpawnOrigin.SubAgent => $"helper for another agent (depth {depth})",
        _ => string.Empty
    };

    private static string Shorten(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var single = value.ReplaceLineEndings(" ").Trim();
        return single.Length <= maximum ? single : single[..maximum] + "…";
    }

    /// <summary>
    /// Opens a file an agent produced, using whatever the user normally opens
    /// it with.
    ///
    /// This is the whole reason artifacts are now listed. An agent could write
    /// a report, register it, persist it, and report it to another agent -- and
    /// the person it was written for had no way to reach it except knowing
    /// where to look on disk.
    /// </summary>
    private void OpenArtifact_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else
            {
                // The agent recorded a file that has since moved or been
                // deleted. Showing the folder is more use than doing nothing.
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
            }
        }
        catch (Exception exception)
        {
            _runtime?.Log.Error($"Could not open the agent's file at {path}.", exception);
        }
    }

    /// <summary>
    /// Removes a finished task from the list.
    ///
    /// The drawer is also the history, so without this every task from every
    /// session stayed visible until the 150-record cap quietly dropped the
    /// oldest. DeleteTaskHistory existed for exactly this and nothing called it.
    /// </summary>
    private void DismissTask_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { Tag: string id } || _runtime?.AgentTasks is null)
        {
            return;
        }

        _runtime.AgentTasks.DeleteTaskHistory(id);
        RefreshTasks();
    }

    /// <summary>
    /// Runs a failed task again, and clears the attempt that failed.
    ///
    /// It used to start a new task and leave the old one sitting in the list
    /// permanently, so retrying three times left four entries with the same
    /// goal and no indication which was current. Removing the failed attempt is
    /// what makes the button mean what it says.
    /// </summary>
    private void RetryTask_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { Tag: string taskId } || _runtime?.AgentTasks is null)
        {
            return;
        }

        var task = _runtime.AgentTasks.GetTask(taskId);
        if (task is null)
        {
            return;
        }

        // The same origin as well as the same goal, so a retried agent keeps
        // whatever latitude the original had rather than silently gaining or
        // losing some.
        _runtime.AgentTasks.SpawnTask(
            task.Goal,
            task.TemplateId,
            task.WorkingDirectory,
            maxTurns: null,
            origin: task.Origin,
            depth: task.Depth);

        _runtime.AgentTasks.DeleteTaskHistory(taskId);
        RefreshTasks();
    }

    private void NewAgent_OnClick(object sender, MouseButtonEventArgs e)
    {
        SpawnAgentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_OnClick(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EmergencyStop_OnClick(object sender, MouseButtonEventArgs e)
    {
        _runtime?.AgentTasks?.CancelAll();
    }

    private void PauseResume_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string taskId && _runtime?.AgentTasks is not null)
        {
            var task = _runtime.AgentTasks.GetTask(taskId);
            if (task?.Status == AgentTaskStatus.Paused)
            {
                _runtime.AgentTasks.ResumeTask(taskId);
            }
            else
            {
                _runtime.AgentTasks.PauseTask(taskId);
            }
        }
    }

    private void CancelTask_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string taskId)
        {
            _runtime?.AgentTasks?.CancelTask(taskId);
        }
    }

    private void ApproveTask_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string taskId)
        {
            _runtime?.AgentTasks?.ApproveAction(taskId, true);
        }
    }

    private void DenyTask_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string taskId)
        {
            _runtime?.AgentTasks?.ApproveAction(taskId, false);
        }
    }
}
