using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Metis.App.Runtime;
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

    public void Reset()
    {
        GoalBox.Clear();
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
                "organize_downloads" => "Find files in my Downloads folder, sort them into Documents/Images/Archives/Code subfolders, and write a summary log.",
                "web_research" => "Search the web for top emerging technologies in 2026, synthesize findings into an executive report in research_report.md.",
                "system_logs" => "Audit recent Windows application error logs, identify top crash sources, and save findings in system_audit.md.",
                "find_extract" => "Search all text and CSV files in working directory for key metrics and aggregate summary into metrics.json.",
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
            Description = "Select working directory for background agent",
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

        var dir = WorkingDirBox.Text.Trim();
        if (!Directory.Exists(dir))
        {
            dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (_runtime?.AgentTasks is not null)
        {
            var task = _runtime.AgentTasks.SpawnTask(goal, _selectedTemplateId, dir);
            AgentSpawned?.Invoke(this, task.Id);
        }

        Reset();
    }
}
