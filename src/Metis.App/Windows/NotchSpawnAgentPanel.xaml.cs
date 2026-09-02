using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Metis.App.Runtime;
using Metis.Core.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Metis.App.Windows;

public partial class NotchSpawnAgentPanel : UserControl
{
    private MetisRuntime? _runtime;
    private string? _selectedTemplateId;

    public event EventHandler? CloseRequested;
    public event EventHandler<string>? AgentSpawned;
    public event EventHandler? ContentSizeChanged;

    public NotchSpawnAgentPanel()
    {
        InitializeComponent();
        WorkingDirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void Attach(MetisRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Reset(string? prefillGoal = null)
    {
        GoalBox.Text = prefillGoal ?? string.Empty;
        _selectedTemplateId = null;
        WorkingDirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        UpdatePlaceholder();
    }

    public void FocusGoalBox()
    {
        GoalBox.Focus();
        Keyboard.Focus(GoalBox);
    }

    public double MeasureDesiredHeight(double width)
    {
        Measure(new System.Windows.Size(Math.Max(width, 1), double.PositiveInfinity));
        return DesiredSize.Height;
    }

    private void UpdatePlaceholder()
    {
        GoalPlaceholder.Visibility = string.IsNullOrWhiteSpace(GoalBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void GoalBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GoalBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            Spawn();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Preset_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string template)
        {
            _selectedTemplateId = template;
            GoalBox.Text = template switch
            {
                "study_tutor" => "Explain key concepts step-by-step with simple examples and practice questions.",
                "writing_assistant" => "Draft a clear, structured summary and proofread my latest notes.",
                "web_research" => "Search the web for insights on this topic and synthesize findings into a concise report.",
                "organize_downloads" => "Scan my Downloads folder, categorize files into Documents/Images/Archives/Code, and create an organized summary.",
                "system_logs" => "Audit recent Windows application logs, identify any errors, and summarize in system_audit.md.",
                "find_extract" => "Search all files in working directory for key metrics and aggregate into summary.json.",
                _ => GoalBox.Text
            };
            FocusGoalBox();
            GoalBox.CaretIndex = GoalBox.Text.Length;
        }
    }

    private void BrowseDir_OnClick(object sender, MouseButtonEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select working directory for background helper",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(WorkingDirBox.Text)
                ? WorkingDirBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            WorkingDirBox.Text = dialog.SelectedPath;
        }
    }

    private void Close_OnClick(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Spawn_OnClick(object sender, MouseButtonEventArgs e)
    {
        Spawn();
    }

    private void Spawn()
    {
        var goal = GoalBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(goal))
        {
            GoalBox.Focus();
            return;
        }

        if (_runtime is not null && !_runtime.Can(MetisFeature.AutonomousAgents))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            _runtime.ShowPlanNotice(
                "Background Helpers (Plus & Pro)",
                _runtime.ExplainCapability(MetisFeature.AutonomousAgents));
            return;
        }

        var dir = WorkingDirBox.Text.Trim();
        if (!Directory.Exists(dir))
        {
            dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (_runtime?.AgentTasks is not null)
        {
            var task = _runtime.AgentTasks.SpawnTask(goal, _selectedTemplateId, dir);
            AgentSpawned?.Invoke(this, task.Id);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        Reset();
    }
}
