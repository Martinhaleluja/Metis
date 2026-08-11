using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Metis.App.Runtime;
using Metis.Core.Models;

namespace Metis.App.Windows;

public partial class AssistantWindow : Window
{
    private readonly MetisRuntime _runtime;
    private readonly Action _showSetup;
    private readonly ObservableCollection<MessageItem> _messages = [];
    private bool _allowClose;
    private bool _typingAnimationRunning;

    public AssistantWindow(MetisRuntime runtime, Action showSetup)
    {
        InitializeComponent();
        _runtime = runtime;
        _showSetup = showSetup;
        MessagesList.ItemsSource = _messages;
        _runtime.MessageAdded += Runtime_OnMessageAdded;
        _runtime.StatusChanged += Runtime_OnStatusChanged;
        _runtime.AudioLevelChanged += Runtime_OnAudioLevelChanged;
        _runtime.State.Changed += State_OnChanged;
        StatusText.Text = _runtime.CurrentStatus;
        Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
                Hide();
            }
        };
        _messages.Add(new MessageItem(
            "Metis",
            "I'm ready. Ask me about your screen, or tell me what to do."));
    }

    public void AllowClose() => _allowClose = true;

    private async void Send_OnClick(object sender, RoutedEventArgs e) => await SendPromptAsync();

    private async Task SendPromptAsync()
    {
        var prompt = PromptBox.Text.Trim();
        if (prompt.Length == 0)
        {
            return;
        }

        PromptBox.Clear();
        SendButton.IsEnabled = false;
        try
        {
            await _runtime.AskTextAsync(prompt);
        }
        finally
        {
            SendButton.IsEnabled = true;
            PromptBox.Focus();
        }
    }

    private async void PromptBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendPromptAsync();
        }
    }

    private void Runtime_OnMessageAdded(object? sender, AssistantMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            var author = message.Role switch
            {
                AssistantRole.User => "You",
                AssistantRole.Error => "Problem",
                _ => "Metis"
            };
            _messages.Add(new MessageItem(author, message.Text));
            ScrollToLatest();
        });
    }

    private void Runtime_OnStatusChanged(object? sender, string status) =>
        Dispatcher.Invoke(() => StatusText.Text = status);

    private void Runtime_OnAudioLevelChanged(object? sender, float level) =>
        Dispatcher.BeginInvoke(() => VoiceLevel.Value = Math.Clamp(level, 0, 1));

    private void State_OnChanged(object? sender, AssistantState state) => Dispatcher.Invoke(() =>
    {
        VoiceLevel.Visibility = state == AssistantState.Listening ? Visibility.Visible : Visibility.Hidden;
        StateDot.Fill = state switch
        {
            AssistantState.Listening => (System.Windows.Media.Brush)FindResource("AccentBrush"),
            AssistantState.Thinking => (System.Windows.Media.Brush)FindResource("AccentBrush"),
            AssistantState.Success => (System.Windows.Media.Brush)FindResource("MintBrush"),
            AssistantState.Error or AssistantState.NetworkError or AssistantState.AuthenticationError
                or AssistantState.QuotaError or AssistantState.AutomationError =>
                (System.Windows.Media.Brush)FindResource("DangerBrush"),
            _ => (System.Windows.Media.Brush)FindResource("MintBrush")
        };

        SetTyping(state == AssistantState.Thinking);
    });

    /// <summary>
    /// Shows the three-dot bubble while Metis is composing, with the dots
    /// staggered so the motion travels left to right the way a phone's typing
    /// indicator does.
    /// </summary>
    private void SetTyping(bool typing)
    {
        if (typing == _typingAnimationRunning)
        {
            return;
        }

        _typingAnimationRunning = typing;
        TypingBubble.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;

        Ellipse[] dots = [TypingDot1, TypingDot2, TypingDot3];
        for (var index = 0; index < dots.Length; index++)
        {
            if (!typing)
            {
                dots[index].BeginAnimation(OpacityProperty, null);
                dots[index].BeginAnimation(HeightProperty, null);
                continue;
            }

            var pulse = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(560))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(index * 180),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            dots[index].BeginAnimation(OpacityProperty, pulse);
        }

        if (typing)
        {
            ScrollToLatest();
        }
    }

    private void ScrollToLatest() => Dispatcher.BeginInvoke(
        () => MessagesScroll.ScrollToEnd(),
        System.Windows.Threading.DispatcherPriority.Loaded);

    private void Stop_OnClick(object sender, RoutedEventArgs e) => _runtime.CancelCurrentTurn();
    private void Setup_OnClick(object sender, RoutedEventArgs e) => _showSetup();
    private void Close_OnClick(object sender, RoutedEventArgs e) => FoldIntoNotch();
    private void TrafficLightClose_OnMouseUp(object sender, MouseButtonEventArgs e) => FoldIntoNotch();

    /// <summary>
    /// Opens the window out of the notch at the given anchor. The window is
    /// repositioned every time because the notch owns where it lives.
    /// </summary>
    public void OpenFromNotch(double anchorBottom)
    {
        NotchAnchor.Position(this, anchorBottom);
        Show();
        Activate();
        NotchAnchor.AnimateOpen(RootShell);
    }

    /// <summary>
    /// Folds back into the notch instead of vanishing, so dismissing the window
    /// shows the user where it went and where to find it again.
    /// </summary>
    public void FoldIntoNotch()
    {
        if (!IsVisible)
        {
            return;
        }

        NotchAnchor.AnimateClose(RootShell, Hide);
        Folded?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Folded;

}

public sealed record MessageItem(string Author, string Text);
