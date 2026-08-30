using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Metis.App.Runtime;
using Metis.Core.Agents;
using Metis.Core.Models;
using Metis.Core.Services;

// WPF and Windows Forms are both enabled, so these names are ambiguous under
// implicit usings. Everything in this window is WPF.
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Metis.App.Windows;

public sealed record AgentDotItem(string TaskId, Brush DotBrush, string Tooltip);

public static class AgentColors
{
    private static readonly string[] Palette =
    [
        "#0A7CFF", // Electric Blue
        "#30D158", // Vibrant Green
        "#BF5AF2", // Purple
        "#FF9F0A", // Amber Orange
        "#FF375F", // Vivid Coral
        "#5E5CE6", // Indigo
        "#64D2FF", // Cyan
        "#FFD60A"  // Yellow
    ];

    public static Brush GetBrush(int index) =>
        (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[Math.Abs(index) % Palette.Length]));
}

/// <summary>
/// The whole of Metis's chat, laid out to live inside the notch. There is no
/// separate chat window: the notch is where the conversation happens, which is
/// what keeps Metis to one place on screen instead of a floating window the
/// user has to find, move and dismiss.
///
/// This control owns only the conversation. Where it sits, how tall it is and
/// how it opens belong to <see cref="NotchWindow"/>, which asks this control how
/// much room it needs and grows the notch to match.
/// </summary>
public partial class NotchChat : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<ChatBubble> _messages = [];
    private MetisRuntime? _runtime;
    private UpdateCheck? _availableUpdate;
    private bool _sending;

    /// <summary>The reply currently being written into, if one is arriving.</summary>
    private ChatBubble? _streamingBubble;

    /// <summary>
    /// Grows the notch to fit a reply as it arrives. Ticking a few times a
    /// second is enough to look continuous and costs one measure per tick,
    /// rather than one per fragment of text.
    /// </summary>
    private readonly DispatcherTimer _streamGrowth = new()
    {
        Interval = TimeSpan.FromMilliseconds(140)
    };

    /// <summary>Raised when the user asks for the chat to be put away.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the user asks for the setup window.</summary>
    public event EventHandler? SetupRequested;

    /// <summary>Raised when the user clicks Spawn Agent.</summary>
    public event EventHandler? SpawnAgentRequested;

    /// <summary>Raised when the user clicks the active agent dots.</summary>
    public event EventHandler? AgentDrawerRequested;

    /// <summary>
    /// Raised whenever the content changes size — a message arriving, the
    /// composer growing under a longer prompt, a conversation being switched.
    /// The notch listens to this and animates its own height to match, which is
    /// what makes the notch expand as you type into it.
    /// </summary>
    public event EventHandler? ContentSizeChanged;

    public NotchChat()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
        _streamGrowth.Tick += (_, _) =>
        {
            RaiseSizeChanged();
            ScrollToLatest();
        };
    }

    /// <summary>
    /// Wires the panel to the running Metis. Done after construction rather than
    /// through the constructor so the notch can be built before the runtime is
    /// ready, and so this control still opens in the XAML designer.
    /// </summary>
    public void Attach(MetisRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;

        runtime.MessageAdded += Runtime_OnMessageAdded;
        runtime.ResponseStreamStarted += Runtime_OnResponseStreamStarted;
        runtime.ResponseTextDelta += Runtime_OnResponseTextDelta;
        runtime.StatusChanged += Runtime_OnStatusChanged;
        runtime.AudioLevelChanged += Runtime_OnAudioLevelChanged;
        runtime.State.Changed += Runtime_OnStateChanged;
        runtime.ChatsChanged += (_, _) => Dispatcher.InvokeAsync(RefreshModelChip);

        // The model list depends on which provider is selected, so it is rebuilt
        // whenever settings change rather than only once. Usage is re-read at
        // the same time, which keeps the counts from going stale.
        runtime.SettingsChanged += (_, _) => Dispatcher.InvokeAsync(RefreshModelChip);

        if (runtime.AgentTasks is not null)
        {
            runtime.AgentTasks.TaskCreated += (_, task) => Dispatcher.InvokeAsync(() =>
            {
                RefreshAgentDots();
                _messages.Add(new ChatBubble("Metis", $"⚡ Agent [{task.Id}] started: \"{task.Goal}\" in the background."));
                RaiseSizeChanged();
            });

            runtime.AgentTasks.TaskCompleted += (_, task) => Dispatcher.InvokeAsync(() =>
            {
                RefreshAgentDots();
                var summary = string.IsNullOrWhiteSpace(task.ResultSummary) ? "Task completed successfully." : task.ResultSummary;
                _messages.Add(new ChatBubble("Metis", $"✅ Agent [{task.Id}] finished!\n\nGoal: \"{task.Goal}\"\n\nResult: {summary}"));
                RaiseSizeChanged();
            });

            runtime.AgentTasks.TaskUpdated += (_, _) => Dispatcher.InvokeAsync(RefreshAgentDots);

            runtime.AgentTasks.TaskFailed += (_, task) => Dispatcher.InvokeAsync(() =>
            {
                RefreshAgentDots();
                _messages.Add(new ChatBubble("Problem", $"❌ Agent [{task.Id}] failed: {task.ErrorMessage ?? "Unknown error"}\n\nClick 'Spawn Agent' or type /spawn {task.Goal} to retry."));
                RaiseSizeChanged();
            });

            runtime.AgentTasks.TaskCancelled += (_, task) => Dispatcher.InvokeAsync(() =>
            {
                RefreshAgentDots();
                _messages.Add(new ChatBubble("Metis", $"⏹ Agent [{task.Id}] was cancelled."));
                RaiseSizeChanged();
            });

            RefreshAgentDots();
        }

        StatusText.Text = runtime.CurrentStatus;
        Greet("I'm ready. Ask me about your screen, or tell me what to do.");
        RefreshModelChip();
    }

    /// <summary>Puts the caret in the composer, ready to type.</summary>
    public void FocusComposer()
    {
        PromptBox.Focus();
        Keyboard.Focus(PromptBox);
        PromptBox.CaretIndex = PromptBox.Text.Length;
    }

    /// <summary>Closes both menus, so nothing is left hanging when the notch folds away.</summary>
    public void CloseMenus()
    {
        ModelMenu.IsOpen = false;
        HistoryMenu.IsOpen = false;
    }

    /// <summary>
    /// How tall the panel wants to be at the given width. The notch asks for
    /// this rather than measuring the control itself, so the measurement is made
    /// with the same constraint the layout will actually run under.
    /// </summary>
    public double MeasureDesiredHeight(double width)
    {
        Measure(new System.Windows.Size(Math.Max(width, 1), double.PositiveInfinity));
        return DesiredSize.Height;
    }

    private void Greet(string text)
    {
        _messages.Clear();
        _messages.Add(new ChatBubble("Metis", text));
        RaiseSizeChanged();
    }

    /// <summary>
    /// Displays the update banner telling the user to click update, after which
    /// Metis will download the installer and automatically restart.
    /// </summary>
    public void ShowUpdate(UpdateCheck update)
    {
        if (!update.UpdateAvailable)
        {
            return;
        }

        _availableUpdate = update;
        UpdateTitle.Text = $"Update to Metis {update.Version ?? "new version"} is available";
        UpdateSubtitle.Text = "Click update and Metis will automatically restart.";
        UpdateActionLabel.Text = "Update";
        UpdateButton.IsEnabled = true;
        UpdateButton.Opacity = 1.0;
        UpdateBanner.Visibility = Visibility.Visible;
        RaiseSizeChanged();
    }

    public void HideUpdate()
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        RaiseSizeChanged();
    }

    /// <summary>
    /// Raised when the user asks to see their plan, so the shell can open
    /// Preferences on the account page. The panel does not open windows itself
    /// — it is a control inside the notch and knows nothing about the rest of
    /// the application.
    /// </summary>
    public event EventHandler? PlanRequested;

    /// <summary>
    /// Shows the plan banner, or hides it.
    ///
    /// Called when a turn is refused for a reason that is about the account
    /// rather than about the request: the month's included AI is spent, or the
    /// plan does not cover what was asked for. Both are ordinary states rather
    /// than faults, which is why they get their own quiet banner instead of the
    /// error surface.
    /// </summary>
    public void ShowPlanNotice(string title, string subtitle, string action = "See plans")
    {
        PlanBannerTitle.Text = title;
        PlanBannerSubtitle.Text = subtitle;
        PlanBannerActionLabel.Text = action;
        PlanBanner.Visibility = Visibility.Visible;
    }

    public void HidePlanNotice() => PlanBanner.Visibility = Visibility.Collapsed;

    private void PlanBannerButton_OnClick(object sender, MouseButtonEventArgs e)
    {
        HidePlanNotice();
        PlanRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void UpdateButton_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_availableUpdate is null || !_availableUpdate.UpdateAvailable)
        {
            return;
        }

        if (_runtime is null)
        {
            return;
        }

        UpdateActionLabel.Text = "Updating…";
        UpdateSubtitle.Text = "Downloading update and restarting Metis…";
        UpdateButton.IsEnabled = false;
        UpdateButton.Opacity = 0.6;
        RaiseSizeChanged();

        var updater = new UpdateService(_runtime.Log);
        var started = await updater.DownloadAndRunAsync(_availableUpdate);
        if (!started)
        {
            UpdateActionLabel.Text = "Retry";
            UpdateSubtitle.Text = "Update download failed. Check your internet connection or retry.";
            UpdateButton.IsEnabled = true;
            UpdateButton.Opacity = 1.0;
            RaiseSizeChanged();
        }
    }

    // ============================ Sending ============================

    private async void Send_OnClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            e.Handled = true;
            await SendAsync();
        }
        catch (Exception ex)
        {
            _runtime?.Log.Error("Unhandled exception in Send_OnClick", ex);
        }
    }

    private async void PromptBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        try
        {
            // Enter sends and Shift+Enter breaks the line, which is the convention
            // every chat box follows. PreviewKeyDown rather than KeyDown so the
            // TextBox never gets the chance to insert the newline first.
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await SendAsync();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _runtime?.Log.Error("Unhandled exception in PromptBox_OnPreviewKeyDown", ex);
        }
    }

    private async Task SendAsync()
    {
        if (_runtime is null || _sending)
        {
            return;
        }

        var prompt = PromptBox.Text.Trim();
        if (prompt.Length == 0)
        {
            return;
        }

        PromptBox.Clear();
        UpdatePlaceholder();

        try
        {
            // The spawn intercept that used to sit here is gone. It caught the
            // prompt before the runtime ever saw it, which meant the typed path
            // and the voice path disagreed about what counted as a request for
            // an agent, it only ever started one however many were asked for,
            // and it could never follow "spawn an agent" with "to do what?".
            // The runtime handles all of it now, in one place.

            _sending = true;
            SendButton.Opacity = 0.45;
            await _runtime.AskTextAsync(prompt);
        }
        catch (Exception exception)
        {
            _runtime.Log.Error("Failed to send message", exception);
            _messages.Add(new ChatBubble("Problem", $"Failed to send message: {exception.Message}"));
            RaiseSizeChanged();
        }
        finally
        {
            _sending = false;
            SendButton.Opacity = 1;
            FocusComposer();
        }
    }

    private void SpawnAgentChip_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SpawnAgentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AgentDots_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AgentDrawerRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshAgentDots()
    {
        if (_runtime?.AgentTasks is null) return;

        var active = _runtime.AgentTasks.GetActiveTasks();
        var dots = new List<AgentDotItem>();
        for (var i = 0; i < active.Count; i++)
        {
            var task = active[i];
            dots.Add(new AgentDotItem(task.Id, AgentColors.GetBrush(i), $"{task.Id}: {task.Goal} ({task.Status})"));
        }

        AgentDotsControl.ItemsSource = dots;
        AgentDotsControl.Visibility = dots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Grows the notch as the prompt grows. The composer is capped, so a very
    /// long prompt scrolls inside the field rather than pushing the notch down
    /// the whole screen.
    /// </summary>
    private void PromptBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholder();
        RaiseSizeChanged();
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = PromptBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ========================= Runtime events =========================

    private void Runtime_OnMessageAdded(object? sender, AssistantMessage message) => Dispatcher.InvokeAsync(() =>
    {
        var author = message.Role switch
        {
            AssistantRole.User => "You",
            AssistantRole.Error => "Problem",
            _ => "Metis"
        };

        // The reply may already be on screen, written a fragment at a time. If
        // so this is the same answer arriving complete — the parsed text, which
        // can differ slightly from the raw stream — so it replaces what was
        // shown rather than being added underneath it.
        _streamGrowth.Stop();
        if (_streamingBubble is { } streaming && message.Role == AssistantRole.Metis)
        {
            streaming.Text = message.Text;
            _streamingBubble = null;
            RaiseSizeChanged();
            ScrollToLatest();
            RefreshModelChip();
            return;
        }

        _streamingBubble = null;
        _messages.Add(new ChatBubble(author, message.Text));
        RaiseSizeChanged();
        ScrollToLatest();
        RefreshModelChip();
    });

    private void Runtime_OnResponseStreamStarted(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        _streamingBubble = new ChatBubble("Metis", string.Empty);
        _messages.Add(_streamingBubble);
        RaiseSizeChanged();
        ScrollToLatest();
        _streamGrowth.Start();
    });

    /// <summary>
    /// Appends the next piece of a reply that is still being written.
    ///
    /// Deliberately does not resize or scroll here. Fragments arrive many times
    /// a second and <see cref="MeasureDesiredHeight"/> measures the whole
    /// transcript, so doing that per fragment would lay the conversation out
    /// again for every few characters of it. The binding redraws the text on
    /// its own; a timer keeps the notch's height following along.
    /// </summary>
    private void Runtime_OnResponseTextDelta(object? sender, string delta) =>
        Dispatcher.InvokeAsync(() => _streamingBubble?.Append(delta));

    private void Runtime_OnStatusChanged(object? sender, string status) =>
        Dispatcher.InvokeAsync(() => StatusText.Text = status);

    private void Runtime_OnAudioLevelChanged(object? sender, float level) =>
        Dispatcher.BeginInvoke(() => VoiceLevel.Value = Math.Clamp(level, 0, 1));

    private void Runtime_OnStateChanged(object? sender, AssistantState state) => Dispatcher.InvokeAsync(() =>
    {
        VoiceLevel.Visibility = state == AssistantState.Listening ? Visibility.Visible : Visibility.Hidden;

        // Stop is only offered while there is something to stop. A control that
        // does nothing is worse than one that is not there.
        var busy = state is AssistantState.Listening or AssistantState.Thinking or AssistantState.Speaking;
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        StateDot.Fill = new SolidColorBrush(state switch
        {
            AssistantState.Listening => Color.FromRgb(0x30, 0xD1, 0x58),
            AssistantState.Thinking => Color.FromRgb(0x0A, 0x7C, 0xFF),
            AssistantState.Speaking => Color.FromRgb(0x64, 0xD2, 0xFF),
            AssistantState.Success => Color.FromRgb(0x30, 0xD1, 0x58),
            AssistantState.Paused => Color.FromRgb(0xFF, 0x9F, 0x0A),
            AssistantState.Error or AssistantState.NetworkError or AssistantState.AuthenticationError
                or AssistantState.QuotaError or AssistantState.AutomationError =>
                Color.FromRgb(0xFF, 0x62, 0x57),
            _ => Color.FromRgb(0x8E, 0x8E, 0x93)
        });
    });

    private void ScrollToLatest() => Dispatcher.BeginInvoke(
        () => MessagesScroll.ScrollToEnd(),
        System.Windows.Threading.DispatcherPriority.Loaded);

    private void RaiseSizeChanged() => Dispatcher.BeginInvoke(
        () => ContentSizeChanged?.Invoke(this, EventArgs.Empty),
        System.Windows.Threading.DispatcherPriority.Render);

    // ========================== Header actions ==========================

    private void Stop_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _runtime?.CancelCurrentTurn();
    }

    private void Setup_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseMenus();
        SetupRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Collapse_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NewChat_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseMenus();
        if (_runtime is null)
        {
            return;
        }

        _runtime.StartNewChat();
        Greet("New chat. What are we working on?");
        FocusComposer();
    }

    // ============================== Menus ==============================

    private void ModelChip_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HistoryMenu.IsOpen = false;
        if (ModelMenu.IsOpen)
        {
            ModelMenu.IsOpen = false;
            return;
        }

        BuildModelMenu();
        ModelMenu.IsOpen = true;
    }

    private void History_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModelMenu.IsOpen = false;
        if (HistoryMenu.IsOpen)
        {
            HistoryMenu.IsOpen = false;
            return;
        }

        BuildHistoryMenu();
        HistoryMenu.IsOpen = true;
    }

    private void ModelRow_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModelMenu.IsOpen = false;
        if (_runtime is null || sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        _ = _runtime.SetModelAsync(id);
    }

    private void HistoryRow_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HistoryMenu.IsOpen = false;
        if (_runtime is null || sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        if (id == _runtime.CurrentChat.Id)
        {
            return;
        }

        // Cleared before resuming, because ResumeChat replays the stored turns
        // through MessageAdded and they would otherwise land underneath the
        // conversation the user is leaving.
        _messages.Clear();
        _runtime.ResumeChat(id);
        RaiseSizeChanged();
    }

    /// <summary>
    /// Fills the model menu from the catalogue for whichever provider is
    /// selected, annotated with what this machine has actually used.
    ///
    /// Every model is listed, including the ones that run locally and the free
    /// tiers of the online providers, because the point of the picker is to let
    /// someone choose what a request will cost them.
    /// </summary>
    private void BuildModelMenu()
    {
        if (_runtime is null)
        {
            return;
        }

        var provider = _runtime.Settings.AiProvider;
        var current = CurrentModelId(provider);
        var now = DateTimeOffset.Now;

        var rows = ModelCatalog.For(provider).Select(model =>
        {
            var usage = _runtime.ModelUsage.For(model, now);
            return new ModelRow(
                model.Id,
                model.DisplayName,
                TierLabelFor(model.Tier),
                TierBrushFor(model.Tier),
                model.Note is null ? model.Summary : $"{model.Summary} — {model.Note}",
                usage.Describe(),
                string.Equals(model.Id, current, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }).ToArray();

        ModelMenuList.ItemsSource = rows;
        ModelMenuHeading.Text = rows.Length == 0
            ? $"{provider} has no models listed. Open setup to choose a provider."
            : $"{provider} · {rows.Length} models · usage is counted on this PC, so it may differ "
              + "from the provider's own total. Change provider in setup.";
    }

    private void BuildHistoryMenu()
    {
        if (_runtime is null)
        {
            return;
        }

        var rows = _runtime.Chats
            .Take(30)
            .Select(session => new HistoryRow(
                session.Id,
                session.Title,
                $"{session.Application ?? "Metis"} · {session.UpdatedAt:g}",
                session.Id == _runtime.CurrentChat.Id ? Visibility.Visible : Visibility.Collapsed))
            .ToList();

        if (rows.All(row => row.Id != _runtime.CurrentChat.Id))
        {
            rows.Insert(0, new HistoryRow(
                _runtime.CurrentChat.Id, "This chat", "Not saved yet", Visibility.Visible));
        }

        HistoryMenuList.ItemsSource = rows;
    }

    /// <summary>
    /// Keeps the chip showing the model that will actually answer. It is the
    /// only place the choice is visible once the menu is closed, so it has to
    /// follow every route that can change it — the menu, setup, or a provider
    /// swap.
    /// </summary>
    private void RefreshModelChip()
    {
        if (_runtime is null)
        {
            return;
        }

        var provider = _runtime.Settings.AiProvider;
        var current = CurrentModelId(provider);
        var match = ModelCatalog.For(provider)
            .FirstOrDefault(model => string.Equals(model.Id, current, StringComparison.OrdinalIgnoreCase));

        ModelName.Text = match?.DisplayName ?? (string.IsNullOrWhiteSpace(current) ? provider : current);
        TierLabel.Text = TierLabelFor(match?.Tier ?? ModelTier.Paid);
        TierBadge.Background = TierBrushFor(match?.Tier ?? ModelTier.Paid);
        ModelChip.ToolTip = match is null
            ? $"{provider} · {current}"
            : $"{provider} · {(match.Note is null ? match.Summary : $"{match.Summary} — {match.Note}")}";
    }

    private static string TierLabelFor(ModelTier tier) => tier switch
    {
        ModelTier.Free => "FREE",
        ModelTier.Local => "LOCAL",
        _ => "PAID"
    };

    /// <summary>
    /// Green for free, blue for local, amber for billed. The colour is what is
    /// read at a glance; the word is there because colour alone is not readable
    /// by everyone.
    /// </summary>
    private static System.Windows.Media.Brush TierBrushFor(ModelTier tier) => new SolidColorBrush(tier switch
    {
        ModelTier.Free => Color.FromRgb(0x30, 0xB1, 0x58),
        ModelTier.Local => Color.FromRgb(0x0A, 0x7C, 0xFF),
        _ => Color.FromRgb(0xC8, 0x7A, 0x0A)
    });

    private string CurrentModelId(string provider) => _runtime is null ? string.Empty : provider switch
    {
        "OpenAI" => _runtime.Settings.OpenAiReasoningModel,
        "Claude" => _runtime.Settings.ClaudeReasoningModel,
        "OpenRouter" => _runtime.Settings.OpenRouterModel,
        "Ollama" => _runtime.Settings.OllamaModel,
        _ => _runtime.Settings.ReasoningModel
    };

    /// <summary>One row of the model menu, already formatted for display.</summary>
    private sealed record ModelRow(
        string Id,
        string DisplayName,
        string TierLabel,
        System.Windows.Media.Brush TierBrush,
        string Detail,
        string Usage,
        Visibility TickVisibility);

    /// <summary>One earlier conversation, as the history menu shows it.</summary>
    private sealed record HistoryRow(string Id, string Title, string Subtitle, Visibility TickVisibility);
}

/// <summary>
/// One line of the conversation as it is drawn.
///
/// A class rather than the record this used to be, because a reply is now put
/// on screen while it is still being written: the bubble is created empty and
/// filled in as the words arrive, which needs a property the binding can watch
/// change rather than a new immutable value each time.
/// </summary>
public sealed class ChatBubble : INotifyPropertyChanged
{
    private string _text;

    public ChatBubble(string author, string text)
    {
        Author = author;
        _text = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Author { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public void Append(string fragment) => Text = _text + fragment;
}
