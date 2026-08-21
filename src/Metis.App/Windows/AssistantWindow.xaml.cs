using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Metis.App.Runtime;
using Metis.Core.Models;

namespace Metis.App.Windows;

public partial class AssistantWindow : Window
{
    private readonly MetisRuntime _runtime;
    private readonly Action _showSetup;
    private readonly ObservableCollection<MessageItem> _messages = [];
    private bool _allowClose;
    private bool _suppressChatSelection;

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

        _runtime.ChatsChanged += (_, _) => Dispatcher.Invoke(RefreshChats);
        RefreshChats();

        // The list depends on the provider, so it is rebuilt whenever settings
        // change rather than only at startup. Usage is re-read at the same
        // time, which is what keeps the counts from going stale while the
        // window sits open.
        _runtime.SettingsChanged += (_, _) => Dispatcher.Invoke(RefreshModels);
        _runtime.MessageAdded += (_, _) => Dispatcher.Invoke(RefreshModels);
        RefreshModels();
    }

    public void AllowClose() => _allowClose = true;

    /// <summary>
    /// Rebuilds the chat list. The current conversation stays selected so that
    /// saving a turn does not look like the user was moved to another chat.
    /// </summary>
    private void RefreshChats()
    {
        _suppressChatSelection = true;
        try
        {
            ChatSelector.Items.Clear();
            foreach (var session in _runtime.Chats.Take(30))
            {
                ChatSelector.Items.Add(new ComboBoxItem
                {
                    Content = session.Title,
                    Tag = session.Id,
                    ToolTip = $"{session.Application ?? "Metis"} · {session.UpdatedAt:g}"
                });
            }

            if (ChatSelector.Items.Count == 0)
            {
                ChatSelector.Items.Add(new ComboBoxItem { Content = "This chat", Tag = _runtime.CurrentChat.Id });
            }

            foreach (ComboBoxItem item in ChatSelector.Items)
            {
                if ((item.Tag as string) == _runtime.CurrentChat.Id)
                {
                    ChatSelector.SelectedItem = item;
                    break;
                }
            }

            ChatSelector.SelectedItem ??= ChatSelector.Items[0];
        }
        finally
        {
            _suppressChatSelection = false;
        }
    }

    private void ChatSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChatSelection || ChatSelector.SelectedItem is not ComboBoxItem { Tag: string id })
        {
            return;
        }

        _messages.Clear();
        _runtime.ResumeChat(id);
    }

    private void NewChat_OnClick(object sender, RoutedEventArgs e)
    {
        _runtime.StartNewChat();
        _messages.Clear();
        _messages.Add(new MessageItem("Metis", "New chat. What are we working on?"));
    }

    /// <summary>One row of the model picker, already formatted for display.</summary>
    private sealed record ModelRow(
        string Id,
        string DisplayName,
        string TierLabel,
        System.Windows.Media.Brush TierBrush,
        string Detail,
        string Usage);

    private bool _loadingModels;

    /// <summary>
    /// Fills the picker from the catalogue for whichever provider is selected,
    /// annotated with what this machine has actually used.
    ///
    /// Every model is listed, including the ones that run locally and the free
    /// tiers of the online providers, because the point of the picker is to let
    /// someone choose what a request will cost them.
    /// </summary>
    private void RefreshModels()
    {
        _loadingModels = true;
        try
        {
            var provider = _runtime.Settings.AiProvider;
            var current = CurrentModelId(provider);
            var now = DateTimeOffset.Now;

            var rows = ModelCatalog.For(provider).Select(model =>
            {
                var usage = _runtime.ModelUsage.For(model, now);
                return new ModelRow(
                    model.Id,
                    model.DisplayName,
                    model.Tier switch
                    {
                        ModelTier.Free => "FREE",
                        ModelTier.Local => "LOCAL",
                        _ => "PAID"
                    },
                    new System.Windows.Media.SolidColorBrush(model.Tier switch
                    {
                        // Green for free, blue for local, amber for billed. The
                        // colour is the thing read at a glance; the word is
                        // there because colour alone is not readable by
                        // everyone.
                        ModelTier.Free => System.Windows.Media.Color.FromRgb(0x30, 0xB1, 0x58),
                        ModelTier.Local => System.Windows.Media.Color.FromRgb(0x0A, 0x7C, 0xFF),
                        _ => System.Windows.Media.Color.FromRgb(0xC8, 0x7A, 0x0A)
                    }),
                    model.Note is null ? model.Summary : $"{model.Summary} — {model.Note}",
                    usage.Describe());
            }).ToArray();

            ModelSelector.ItemsSource = rows;
            ModelSelector.SelectedItem =
                rows.FirstOrDefault(row => string.Equals(row.Id, current, StringComparison.OrdinalIgnoreCase))
                ?? rows.FirstOrDefault();

            ModelUsageText.Text = rows.Length == 0
                ? string.Empty
                : $"{provider} · {rows.Length} models · usage counted on this PC, so it may differ from the provider's own total.";
        }
        finally
        {
            _loadingModels = false;
        }
    }

    private void ModelSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModels || ModelSelector.SelectedItem is not ModelRow row)
        {
            return;
        }

        _ = _runtime.SetModelAsync(row.Id);
    }

    private string CurrentModelId(string provider) => provider switch
    {
        "OpenAI" => _runtime.Settings.OpenAiReasoningModel,
        "Claude" => _runtime.Settings.ClaudeReasoningModel,
        "OpenRouter" => _runtime.Settings.OpenRouterModel,
        "Ollama" => _runtime.Settings.OllamaModel,
        _ => _runtime.Settings.ReasoningModel
    };

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

    });

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
