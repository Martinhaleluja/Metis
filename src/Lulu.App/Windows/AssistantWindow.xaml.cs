using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Lulu.App.Runtime;
using Lulu.Core.Models;

namespace Lulu.App.Windows;

public partial class AssistantWindow : Window
{
    private readonly LuluRuntime _runtime;
    private readonly Action _showSetup;
    private readonly ObservableCollection<MessageItem> _messages = [];
    private bool _allowClose;

    public AssistantWindow(LuluRuntime runtime, Action showSetup)
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
        _messages.Add(new MessageItem("Lulu", "I’m ready. Type here or hold Ctrl+Shift+1 while you speak."));
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
                _ => "Lulu"
            };
            _messages.Add(new MessageItem(author, message.Text));
            MessagesList.ScrollIntoView(_messages[^1]);
        });
    }

    private void Runtime_OnStatusChanged(object? sender, string status) =>
        Dispatcher.Invoke(() => StatusText.Text = status);

    private void Runtime_OnAudioLevelChanged(object? sender, float level) =>
        Dispatcher.BeginInvoke(() => VoiceLevel.Value = Math.Clamp(level, 0, 1));

    private void State_OnChanged(object? sender, AssistantState state) => Dispatcher.Invoke(() =>
    {
        VoiceLevel.Visibility = state == AssistantState.Listening ? Visibility.Visible : Visibility.Hidden;
        StateDot.Fill = state == AssistantState.Error
            ? System.Windows.Media.Brushes.HotPink
            : state == AssistantState.Listening
                ? System.Windows.Media.Brushes.LimeGreen
                : (System.Windows.Media.Brush)FindResource("BabyBlueBrush");
    });

    private void Stop_OnClick(object sender, RoutedEventArgs e) => _runtime.CancelCurrentTurn();
    private void Setup_OnClick(object sender, RoutedEventArgs e) => _showSetup();
    private void Close_OnClick(object sender, RoutedEventArgs e) => Hide();

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

public sealed record MessageItem(string Author, string Text);
