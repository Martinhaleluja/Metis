using System.IO;
using System.Windows;
using System.Windows.Input;
using Metis.App.Runtime;

namespace Metis.App.Windows;

public partial class SpawnAgentDialog : Window
{
    private readonly MetisRuntime _runtime;

    public SpawnAgentDialog(MetisRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        // Default working directory to Downloads or User Profile
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        WorkingDirBox.Text = Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Loaded += (_, _) =>
        {
            GoalBox.Focus();
            Keyboard.Focus(GoalBox);
        };
    }

    private void Template_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string prompt)
        {
            GoalBox.Text = prompt;
            GoalBox.CaretIndex = GoalBox.Text.Length;
        }
    }

    private void Spawn_OnClick(object sender, MouseButtonEventArgs e)
    {
        var goal = GoalBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(goal))
        {
            return;
        }

        var workingDir = WorkingDirBox.Text?.Trim();
        _runtime.AgentTasks?.SpawnTask(goal, workingDir: workingDir);

        Close();
    }

    private void Cancel_OnClick(object sender, MouseButtonEventArgs e)
    {
        Close();
    }
}
