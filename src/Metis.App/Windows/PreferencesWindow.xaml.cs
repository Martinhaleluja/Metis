using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Metis.App.Runtime;
using Metis.App.Theme;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

// WPF and Windows Forms are both enabled, so these are ambiguous under implicit
// usings. Everything here is WPF.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace Metis.App.Windows;

/// <summary>
/// The dashboard and paged preferences. Replaces the single 810-line scrolling
/// Setup page: one page is in the visual tree at a time, and the dashboard
/// answers "what is Metis doing right now" before offering anywhere to change
/// it.
/// </summary>
public partial class PreferencesWindow : System.Windows.Window
{
    private readonly MetisRuntime _runtime;
    private readonly ThemeService? _theme;
    private readonly Action _showAssistant;
    private readonly Dictionary<string, StackPanel> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _companionSaveTimer;

    private bool _loading;
    private bool _allowClose;
    private string _page = "Dashboard";
    private string _companionColour = CompanionPalette.DefaultName;
    private string _companionShape = CompanionShapes.DefaultName;

    /// <summary>Page name, then the words that should find it from search.</summary>
    private static readonly (string Page, string Label, string Keywords)[] Sections =
    [
        ("Dashboard", "Dashboard", "overview status home"),
        ("General", "General", "startup theme dark light appearance sound cue motion windows login"),
        ("Intelligence", "Intelligence", "provider model api key gemini openai claude ollama openclaw openrouter free token endpoint context reasoning"),
        ("Voice", "Voice & input", "microphone speech whisper piper elevenlabs assemblyai chatterbox transcribe voice speak"),
        ("Companion", "Companion", "colour color size cursor distance sprite"),
        ("Privacy", "Memory & privacy", "screen capture memory chat history clear erase privacy recall"),
        ("Skills", "Skills", "skills markdown folder notes"),
        ("Diagnostics", "Diagnostics", "diagnostics logs status troubleshoot")
    ];

    public PreferencesWindow(MetisRuntime runtime, ThemeService? theme, Action showAssistant)
    {
        _runtime = runtime;
        _theme = theme;
        _showAssistant = showAssistant;
        InitializeComponent();

        _pages["Dashboard"] = PageDashboard;
        _pages["General"] = PageGeneral;
        _pages["Intelligence"] = PageIntelligence;
        _pages["Voice"] = PageVoice;
        _pages["Companion"] = PageCompanion;
        _pages["Privacy"] = PagePrivacy;
        _pages["Skills"] = PageSkills;
        _pages["Diagnostics"] = PageDiagnostics;

        BuildNav();

        // Companion changes save on their own after a short pause so the user
        // can judge a colour or size by looking at the real sprite.
        _companionSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _companionSaveTimer.Tick += async (_, _) =>
        {
            _companionSaveTimer.Stop();
            await SaveAsync(quiet: true);
        };

        CompanionSizeSlider.ValueChanged += Companion_OnChanged;
        CursorDistanceSlider.ValueChanged += Companion_OnChanged;

        Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
                Hide();
            }
        };

        RefreshFromRuntime();
        ShowPage("Dashboard");
    }

    public void AllowClose() => _allowClose = true;

    public void ShowAt()
    {
        RefreshFromRuntime();
        Show();
        Activate();
    }

    private void BuildNav()
    {
        foreach (var (page, label, _) in Sections)
        {
            var button = new Button
            {
                Content = label,
                Tag = page,
                Style = (Style)FindResource("NavButton")
            };
            button.Click += Jump_OnClick;
            NavPanel.Children.Add(button);
            _navButtons[page] = button;
        }
    }

    private void ShowPage(string page)
    {
        if (!_pages.TryGetValue(page, out var target))
        {
            return;
        }

        _page = page;

        foreach (var entry in _pages)
        {
            entry.Value.Visibility = entry.Key == page ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var entry in _navButtons)
        {
            entry.Value.Style = (Style)FindResource(entry.Key == page ? "ActiveNavButton" : "NavButton");
        }

        PageScroll.ScrollToTop();
        _ = target;

        if (page == "Dashboard")
        {
            RefreshDashboard();
        }
        else if (page == "Diagnostics")
        {
            RefreshDiagnostics();
        }
    }

    private void Jump_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page })
        {
            ShowPage(page);
        }
    }

    // ----------------------------------------------------------------- load

    private void RefreshFromRuntime()
    {
        _loading = true;
        try
        {
            var s = _runtime.Settings;

            _companionColour = s.CompanionColor;
            _companionShape = s.CompanionShape;

            StartWithWindowsCheck.IsChecked = s.StartWithWindows;
            ReduceMotionCheck.IsChecked = s.ReduceMotion;
            ActivationSoundsCheck.IsChecked = s.ActivationSoundsEnabled;
            SoundPackPathBox.Text = s.SoundPackPath;
            ThemeLight.IsChecked = s.ThemePreference == "Light";
            ThemeDark.IsChecked = s.ThemePreference == "Dark";
            ThemeSystem.IsChecked = s.ThemePreference is not ("Light" or "Dark");

            SelectCombo(ProviderBox, s.AiProvider);
            GeminiModelBox.Text = s.ReasoningModel;
            OpenAiModelBox.Text = s.OpenAiReasoningModel;
            ClaudeModelBox.Text = s.ClaudeReasoningModel;
            OpenClawEndpointBox.Text = s.OpenClawEndpoint;
            OpenClawModelBox.Text = s.OpenClawModel;
            OpenRouterEndpointBox.Text = s.OpenRouterEndpoint;
            OpenRouterModelBox.Text = s.OpenRouterModel;
            OllamaEndpointBox.Text = s.OllamaEndpoint;
            OllamaModelBox.Text = s.OllamaModel;
            LocalContextTokensBox.Text = s.LocalContextTokens.ToString(CultureInfo.InvariantCulture);

            ContextShortcutsCheck.IsChecked = s.ContextShortcutsEnabled;
            VisualGuidanceCheck.IsChecked = s.VisualGuidanceEnabled;

            SelectCombo(SpeechToTextProviderBox, s.SpeechToTextProvider);
            WhisperCppExecutablePathBox.Text = s.WhisperCppExecutablePath;
            WhisperCppModelPathBox.Text = s.WhisperCppModelPath;
            AssemblyAiModelBox.Text = s.AssemblyAiModel;
            OpenAiTranscriptionModelBox.Text = s.OpenAiTranscriptionModel;

            SelectCombo(TextToSpeechProviderBox, s.TextToSpeechProvider);
            LoadWindowsVoices(s.WindowsVoiceName);
            SpeechModelBox.Text = s.SpeechModel;
            VoiceNameBox.Text = s.VoiceName;
            OpenAiSpeechModelBox.Text = s.OpenAiSpeechModel;
            OpenAiVoiceNameBox.Text = s.OpenAiVoiceName;
            PiperExecutablePathBox.Text = s.PiperExecutablePath;
            PiperVoiceModelPathBox.Text = s.PiperVoiceModelPath;
            ChatterboxEndpointBox.Text = s.ChatterboxEndpoint;
            ChatterboxModelBox.Text = s.ChatterboxModel;
            ChatterboxVoiceBox.Text = s.ChatterboxVoice;
            ElevenLabsModelBox.Text = s.ElevenLabsModel;
            ElevenLabsVoiceIdBox.Text = s.ElevenLabsVoiceId;
            SpeechEnabledCheck.IsChecked = s.SpeechEnabled;
            SpeakErrorsCheck.IsChecked = s.SpeakErrorsAloud;

            CompanionSizeSlider.Value = s.CompanionSize;
            CursorDistanceSlider.Value = s.CursorDistance;
            CompanionSizeValue.Text = s.CompanionSize.ToString(CultureInfo.InvariantCulture);
            CursorDistanceValue.Text = s.CursorDistance.ToString(CultureInfo.InvariantCulture);

            CaptureScreenCheck.IsChecked = s.CaptureActiveWindow;
            MemoryEnabledCheck.IsChecked = s.MemoryEnabled;
            ChatMemoryCheck.IsChecked = s.ChatMemoryEnabled;

            UserSkillsCheck.IsChecked = s.UserSkillsEnabled;
            SkillsFolderBox.Text = s.SkillsFolder;

            BuildColourSwatches();
            BuildShapeChoices();
            LoadMicrophones(s.PreferredMicrophoneId);
            UpdateProviderPanels();
            UpdateSpeechPanels();
            UpdateCaptureDisclosure();
            RefreshDashboard();
        }
        finally
        {
            _loading = false;
        }
    }

    private static void SelectCombo(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            item.IsSelected = string.Equals((string?)item.Content, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string SelectedText(ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Content as string ?? fallback;

    private void LoadMicrophones(string? preferredId)
    {
        try
        {
            var devices = _runtime.GetInputDevices();
            MicrophoneBox.ItemsSource = devices;
            MicrophoneBox.DisplayMemberPath = nameof(AudioDeviceInfo.Name);
            MicrophoneBox.SelectedValuePath = nameof(AudioDeviceInfo.Id);
            MicrophoneBox.SelectedValue = preferredId ?? devices.FirstOrDefault()?.Id;
            MicrophoneStatus.Text = devices.Count == 0
                ? "No microphone is available. Check Windows microphone privacy settings."
                : $"{devices.Count} input device(s) found.";
        }
        catch (Exception exception)
        {
            MicrophoneBox.ItemsSource = null;
            MicrophoneStatus.Text = exception.Message;
        }
    }

    private void BuildColourSwatches()
    {
        var swatches = new List<Border>();

        foreach (var option in CompanionPalette.All)
        {
            var ring = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Margin = new Thickness(0, 0, 10, 10),
                BorderThickness = new Thickness(2),
                BorderBrush = option.Name == _companionColour
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = option.Name,
                Tag = option.Name,
                Child = new Ellipse
                {
                    Width = 28,
                    Height = 28,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(option.Fill))
                }
            };

            ring.MouseLeftButtonUp += (sender, _) =>
            {
                if (sender is not Border { Tag: string name })
                {
                    return;
                }

                _companionColour = name;
                foreach (var other in swatches)
                {
                    other.BorderBrush = (string?)other.Tag == name
                        ? (Brush)FindResource("AccentBrush")
                        : Brushes.Transparent;
                }

                // The shape tiles are drawn in the chosen colour, so they have
                // to be redrawn or the picker stops showing the companion the
                // user is actually going to get.
                BuildShapeChoices();
                Companion_OnChanged(this, new RoutedEventArgs());
            };

            swatches.Add(ring);
        }

        ColourSwatches.ItemsSource = swatches;
    }

    /// <summary>
    /// Draws each form at the size it will actually be, in the colour the user
    /// has chosen, so the choice is made by looking at the companion rather
    /// than by reading a list of names.
    /// </summary>
    private void BuildShapeChoices()
    {
        var tiles = new List<Border>();
        var colour = CompanionPalette.Resolve(_companionColour);
        var fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour.Fill));
        var outline = new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x18));

        foreach (var option in CompanionShapes.All)
        {
            var tile = new Border
            {
                Width = 60,
                Height = 60,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 10, 10),
                BorderThickness = new Thickness(2),
                BorderBrush = string.Equals(option.Name, _companionShape, StringComparison.OrdinalIgnoreCase)
                    ? (Brush)FindResource("AccentBrush")
                    : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = $"{option.Name} — {option.Description}",
                Tag = option.Name,
                Child = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(option.Geometry),
                    Fill = fill,
                    Stroke = option.Outlined ? outline : null,
                    StrokeThickness = option.Outlined ? 3.2 : 0,
                    StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(11)
                }
            };

            tile.MouseLeftButtonUp += (sender, _) =>
            {
                if (sender is not Border { Tag: string name })
                {
                    return;
                }

                _companionShape = name;
                foreach (var other in tiles)
                {
                    other.BorderBrush = (string?)other.Tag == name
                        ? (Brush)FindResource("AccentBrush")
                        : Brushes.Transparent;
                }

                Companion_OnChanged(this, new RoutedEventArgs());
            };

            tiles.Add(tile);
        }

        ShapeChoices.ItemsSource = tiles;
    }

    // ------------------------------------------------------------ dashboard

    private void RefreshDashboard()
    {
        var s = _runtime.Settings;

        DashModeText.Text = "Learning";
        DashModeSummary.Text = "Metis shows you how — it guides, draws, and explains, and never operates the computer itself.";

        DashProviderText.Text = s.AiProvider;
        DashProviderDetail.Text = s.AiProvider switch
        {
            "Ollama" => $"{s.OllamaModel} on this PC",
            "OpenAI" => s.OpenAiReasoningModel,
            "Claude" => s.ClaudeReasoningModel,
            "OpenClaw" => $"{s.OpenClawModel} via gateway",
            "OpenRouter" => $"{s.OpenRouterModel} via OpenRouter",
            "Automatic" => "Gemini, then OpenAI, then Claude",
            _ => s.ReasoningModel
        };

        DashCaptureText.Text = s.CaptureActiveWindow ? "On" : "Off";
        DashCaptureDetail.Text = s.CaptureActiveWindow
            ? (s.AiProvider == "Ollama"
                ? "Whole desktop, processed on this PC."
                : $"Whole desktop, sent to {s.AiProvider} when you ask.")
            : "Metis cannot see or point at anything.";

        DashVoiceText.Text = s.SpeechEnabled ? s.TextToSpeechProvider : "Muted";
        DashVoiceDetail.Text = s.SpeechEnabled
            ? $"Listening with {s.SpeechToTextProvider}."
            : "Answers are shown, not spoken.";

        _ = RefreshLearningAsync();
    }

    /// <summary>
    /// Shows the user what they have actually practised. Metis has always kept
    /// this record in order to fade its own guidance out as a skill is
    /// repeated; surfacing it is what lets someone see progress rather than
    /// just receive help.
    /// </summary>
    private async Task RefreshLearningAsync()
    {
        try
        {
            if (!_runtime.Settings.MemoryEnabled)
            {
                LearningSummary.Text = "Progress tracking is off. Turn on \"Remember which steps I have already learned\" under Memory & privacy to see skills build up here.";
                LearningList.ItemsSource = null;
                return;
            }

            var memory = await _runtime.LoadMemoryAsync();
            var skills = memory.Skills
                .OrderByDescending(skill => skill.SuccessfulUses)
                .ThenByDescending(skill => skill.LastUsed)
                .Take(6)
                .ToList();

            if (skills.Count == 0)
            {
                LearningSummary.Text = "Nothing yet. Ask Metis to walk you through something and the skills you practise will appear here.";
                LearningList.ItemsSource = null;
                return;
            }

            var unaided = memory.Skills.Count(skill =>
                skill.Level is SkillLevel.Advanced or SkillLevel.Mastered);
            LearningSummary.Text = unaided > 0
                ? $"{memory.Skills.Count} skill(s) practised, {unaided} you can now do unaided."
                : $"{memory.Skills.Count} skill(s) practised so far.";

            LearningList.ItemsSource = skills.Select(BuildSkillRow).ToList();
        }
        catch (Exception exception)
        {
            LearningSummary.Text = exception.Message;
            LearningList.ItemsSource = null;
        }
    }

    private Border BuildSkillRow(SkillRecord skill)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(skill.Application)
                ? skill.Skill
                : $"{skill.Skill}  ·  {skill.Application}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextBrush")
        };

        // The level is the pedagogically meaningful part: it is what decides
        // how much Metis still says next time.
        var badge = new Border
        {
            Style = (Style)FindResource("Badge"),
            Margin = new Thickness(12, 0, 0, 0),
            Child = new TextBlock
            {
                Style = (Style)FindResource("BadgeText"),
                Text = skill.Level switch
                {
                    SkillLevel.Mastered => "MASTERED",
                    SkillLevel.Advanced => "UNAIDED",
                    SkillLevel.Intermediate => "PRACTISING",
                    SkillLevel.Beginner or SkillLevel.Learning => "LEARNING",
                    _ => "NEW"
                }
            }
        };

        Grid.SetColumn(badge, 1);
        grid.Children.Add(label);
        grid.Children.Add(badge);

        return new Border { Padding = new Thickness(0, 5, 0, 5), Child = grid };
    }

    private void RefreshDiagnostics()
    {
        var s = _runtime.Settings;
        var rows = new List<Border>
        {
            DiagnosticRow("Reasoning provider", s.AiProvider),
            DiagnosticRow("Cloud key stored",
                _runtime.HasAnyApiKey ? "Yes" : "No — local providers do not need one"),
            DiagnosticRow("Screen capture", s.CaptureActiveWindow ? "Enabled" : "Disabled"),
            DiagnosticRow("Speech to text", s.SpeechToTextProvider),
            DiagnosticRow("Text to speech", s.SpeechEnabled ? s.TextToSpeechProvider : "Muted"),
            DiagnosticRow("Microphone", DescribeMicrophone()),
            DiagnosticRow("Runtime status", _runtime.CurrentStatus),
            DiagnosticRow("Log file", _runtime.LogPath)
        };

        DiagnosticsList.ItemsSource = rows;
    }

    private string DescribeMicrophone()
    {
        try
        {
            var devices = _runtime.GetInputDevices();
            if (devices.Count == 0)
            {
                return "None available";
            }

            var preferred = _runtime.Settings.PreferredMicrophoneId;
            var chosen = devices.FirstOrDefault(device => device.Id == preferred) ?? devices[0];
            return chosen.Name;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private Border DiagnosticRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock { Text = label, Style = (Style)FindResource("CaptionText") };
        var content = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextBrush")
        };
        Grid.SetColumn(content, 1);
        grid.Children.Add(name);
        grid.Children.Add(content);

        return new Border { Padding = new Thickness(0, 6, 0, 6), Child = grid };
    }

    // ------------------------------------------------------------- reacting

    private void UpdateProviderPanels()
    {
        var provider = SelectedText(ProviderBox, "Gemini");
        var automatic = provider == "Automatic";

        GeminiCard.Visibility = Show(provider is "Gemini" || automatic);
        OpenAiCard.Visibility = Show(provider is "OpenAI" || automatic);
        ClaudeCard.Visibility = Show(provider is "Claude" || automatic);
        OpenRouterCard.Visibility = Show(provider is "OpenRouter");
        OpenClawCard.Visibility = Show(provider is "OpenClaw");
        OllamaCard.Visibility = Show(provider is "Ollama");

        ProviderHint.Text = provider switch
        {
            "Automatic" => "Tries Gemini, then OpenAI, then Claude, using whichever key is stored. Not available for OpenRouter, OpenClaw or Ollama.",
            "Ollama" => "Runs on this PC. Nothing is sent to a provider.",
            "OpenRouter" => "One key, many hosted models, including free ones. Metis needs a vision-capable model.",
            "OpenClaw" => "Routes through a local agent gateway.",
            _ => "Your screen and questions are sent to this provider when you ask."
        };
    }

    private void UpdateSpeechPanels()
    {
        var stt = SelectedText(SpeechToTextProviderBox, "Native");
        NativeSttPanel.Visibility = Show(stt == "Native");
        WhisperPanel.Visibility = Show(stt == "Whisper.cpp");
        AssemblyAiPanel.Visibility = Show(stt == "AssemblyAI");

        var tts = SelectedText(TextToSpeechProviderBox, "Native");
        NativeTtsPanel.Visibility = Show(tts == "Native");
        WindowsVoicePanel.Visibility = Show(tts == "Windows");
        PiperPanel.Visibility = Show(tts == "Piper");
        ChatterboxPanel.Visibility = Show(tts == "Chatterbox-Nano");
        ElevenLabsPanel.Visibility = Show(tts == "ElevenLabs");
    }

    /// <summary>
    /// Fills the voice list from Windows itself. Read locally rather than
    /// fetched, so it works with no key and no network — and it names what is
    /// actually installed, instead of offering voices this machine lacks.
    /// </summary>
    private void LoadWindowsVoices(string selected)
    {
        WindowsVoiceBox.Items.Clear();
        try
        {
            var voices = _runtime.GetWindowsVoices();
            foreach (var voice in voices)
            {
                WindowsVoiceBox.Items.Add(voice.Name);
            }

            WindowsVoiceBox.Text = selected;
            WindowsVoiceStatusText.Text = voices.Count == 0
                ? "Windows has no speech voices installed. Add one under Settings > Time & language > Speech."
                : $"{voices.Count} voice(s) already on this PC. No download, and it works offline.";
        }
        catch (Exception exception)
        {
            WindowsVoiceStatusText.Text = $"Windows voices could not be listed: {exception.Message}";
        }
    }

    private void UpdateCaptureDisclosure()
    {
        var provider = SelectedText(ProviderBox, "Gemini");
        CaptureDisclosure.Text = provider == "Ollama"
            ? "Metis captures your whole desktop — every monitor — and processes it on this PC. It does not watch continuously and does not record."
            : $"Metis captures your whole desktop — every monitor — and sends it to {provider}. It does not watch continuously and does not record.";
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void ProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        UpdateProviderPanels();
        UpdateCaptureDisclosure();
    }

    private void SpeechProvider_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        UpdateSpeechPanels();
    }

    private void Theme_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        _theme?.Apply(CurrentThemePreference());
    }

    private string CurrentThemePreference() =>
        ThemeLight.IsChecked == true ? "Light" : ThemeDark.IsChecked == true ? "Dark" : "System";

    private void Companion_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        CompanionSizeValue.Text = ((int)CompanionSizeSlider.Value).ToString(CultureInfo.InvariantCulture);
        CursorDistanceValue.Text = ((int)CursorDistanceSlider.Value).ToString(CultureInfo.InvariantCulture);
        _companionSaveTimer.Stop();
        _companionSaveTimer.Start();
    }

    private void Companion_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Companion_OnChanged(sender, new RoutedEventArgs());

    // --------------------------------------------------------------- saving

    private AppSettings BuildSettings()
    {
        var tokens = int.TryParse(LocalContextTokensBox.Text.Trim(), out var parsed)
            ? parsed
            : _runtime.Settings.LocalContextTokens;

        return _runtime.Settings with
        {
            StartWithWindows = StartWithWindowsCheck.IsChecked == true,
            ReduceMotion = ReduceMotionCheck.IsChecked == true,
            ActivationSoundsEnabled = ActivationSoundsCheck.IsChecked == true,
            SoundPackPath = SoundPackPathBox.Text,
            ThemePreference = CurrentThemePreference(),

            AiProvider = SelectedText(ProviderBox, "Gemini"),
            ReasoningModel = GeminiModelBox.Text,
            OpenAiReasoningModel = OpenAiModelBox.Text,
            ClaudeReasoningModel = ClaudeModelBox.Text,
            OpenClawEndpoint = OpenClawEndpointBox.Text,
            OpenClawModel = OpenClawModelBox.Text,
            OpenRouterEndpoint = OpenRouterEndpointBox.Text,
            OpenRouterModel = OpenRouterModelBox.Text,
            OllamaEndpoint = OllamaEndpointBox.Text,
            OllamaModel = OllamaModelBox.Text,
            LocalContextTokens = tokens,

            ContextShortcutsEnabled = ContextShortcutsCheck.IsChecked == true,
            VisualGuidanceEnabled = VisualGuidanceCheck.IsChecked == true,

            SpeechToTextProvider = SelectedText(SpeechToTextProviderBox, "Native"),
            WhisperCppExecutablePath = WhisperCppExecutablePathBox.Text,
            WhisperCppModelPath = WhisperCppModelPathBox.Text,
            AssemblyAiModel = AssemblyAiModelBox.Text,
            OpenAiTranscriptionModel = OpenAiTranscriptionModelBox.Text,

            TextToSpeechProvider = SelectedText(TextToSpeechProviderBox, "Native"),
            WindowsVoiceName = WindowsVoiceBox.Text?.Trim() ?? string.Empty,
            SpeechModel = SpeechModelBox.Text,
            VoiceName = VoiceNameBox.Text,
            OpenAiSpeechModel = OpenAiSpeechModelBox.Text,
            OpenAiVoiceName = OpenAiVoiceNameBox.Text,
            PiperExecutablePath = PiperExecutablePathBox.Text,
            PiperVoiceModelPath = PiperVoiceModelPathBox.Text,
            ChatterboxEndpoint = ChatterboxEndpointBox.Text,
            ChatterboxModel = ChatterboxModelBox.Text,
            ChatterboxVoice = ChatterboxVoiceBox.Text,
            ElevenLabsModel = ElevenLabsModelBox.Text,
            ElevenLabsVoiceId = ElevenLabsVoiceIdBox.Text,
            SpeechEnabled = SpeechEnabledCheck.IsChecked == true,
            SpeakErrorsAloud = SpeakErrorsCheck.IsChecked == true,
            PreferredMicrophoneId = MicrophoneBox.SelectedValue?.ToString(),

            CompanionColor = _companionColour,
            CompanionShape = _companionShape,
            CompanionSize = (int)CompanionSizeSlider.Value,
            CursorDistance = (int)CursorDistanceSlider.Value,

            CaptureActiveWindow = CaptureScreenCheck.IsChecked == true,
            MemoryEnabled = MemoryEnabledCheck.IsChecked == true,
            ChatMemoryEnabled = ChatMemoryCheck.IsChecked == true,

            UserSkillsEnabled = UserSkillsCheck.IsChecked == true,
            SkillsFolder = SkillsFolderBox.Text
        };
    }

    private async Task SaveAsync(bool quiet = false)
    {
        _runtime.SaveAdditionalProviderSecrets(
            NullIfBlank(ClaudeApiKeyBox.Password),
            NullIfBlank(OpenClawTokenBox.Password),
            NullIfBlank(AssemblyAiApiKeyBox.Password),
            NullIfBlank(ElevenLabsApiKeyBox.Password),
            NullIfBlank(OpenRouterApiKeyBox.Password));

        await _runtime.SaveSettingsAsync(
            BuildSettings(),
            NullIfBlank(GeminiApiKeyBox.Password),
            NullIfBlank(OpenAiApiKeyBox.Password));

        // Clearing the boxes after a save keeps a secret from sitting in a
        // control for the rest of the session.
        GeminiApiKeyBox.Password = string.Empty;
        OpenAiApiKeyBox.Password = string.Empty;
        ClaudeApiKeyBox.Password = string.Empty;
        OpenClawTokenBox.Password = string.Empty;
        OpenRouterApiKeyBox.Password = string.Empty;
        AssemblyAiApiKeyBox.Password = string.Empty;
        ElevenLabsApiKeyBox.Password = string.Empty;

        RefreshDashboard();

        if (!quiet)
        {
            SaveStatus.Text = $"Saved at {DateTime.Now:HH:mm}.";
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async void Save_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;
            await SaveAsync();
            _runtime.ReloadUserSkills();
        }
        catch (Exception exception)
        {
            SaveStatus.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    // -------------------------------------------------------------- actions

    private async void TestProvider_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ProviderStatus.Text = "Testing…";
            await SaveAsync(quiet: true);

            var provider = SelectedText(ProviderBox, "Gemini");
            var result = provider switch
            {
                "OpenAI" => await _runtime.TestOpenAiModelAsync(_runtime.Settings.OpenAiReasoningModel),
                "Gemini" or "Automatic" => await _runtime.TestModelAsync(_runtime.Settings.ReasoningModel),
                "Claude" => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.ClaudeReasoningModel),
                "OpenClaw" => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.OpenClawModel),
                "OpenRouter" => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.OpenRouterModel),
                _ => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.OllamaModel)
            };

            ProviderStatus.Text = result.Message;
        }
        catch (Exception exception)
        {
            ProviderStatus.Text = exception.Message;
        }
    }

    private void LocalPreset_OnClick(object sender, RoutedEventArgs e)
    {
        SelectCombo(ProviderBox, "Ollama");
        OllamaEndpointBox.Text = "http://127.0.0.1:11434";
        OllamaModelBox.Text = "qwen3-vl:2b-instruct-q4_K_M";
        LocalContextTokensBox.Text = "2048";
        SelectCombo(SpeechToTextProviderBox, "Whisper.cpp");
        WhisperCppExecutablePathBox.Text = @"tools\whisper.cpp\Release\whisper-cli.exe";
        WhisperCppModelPathBox.Text = @"models\whisper\ggml-tiny.bin";
        SelectCombo(TextToSpeechProviderBox, "Windows");
        PiperExecutablePathBox.Text = @"tools\piper-standalone\piper\piper.exe";
        PiperVoiceModelPathBox.Text = @"models\piper\en_US-lessac-medium.onnx";
        UpdateProviderPanels();
        UpdateSpeechPanels();
        UpdateCaptureDisclosure();
        ProviderStatus.Text = "Fully local preset selected. Install the three runtimes, then Test before saving.";
    }

    private void RemoveKey_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string provider })
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Remove the stored {provider} key from Windows Credential Manager?",
            "Metis",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _runtime.DeleteProviderKey(provider);
            ProviderStatus.Text = $"{provider} key removed.";
        }
        catch (Exception exception)
        {
            ProviderStatus.Text = exception.Message;
        }
    }

    private async void ClearMemory_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Erase everything Metis has learned about which steps you already know?",
            "Metis",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await _runtime.ClearMemoryAsync();
            PrivacyStatus.Text = "Memory cleared.";
        }
        catch (Exception exception)
        {
            PrivacyStatus.Text = exception.Message;
        }
    }

    private void ClearChats_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Delete every saved conversation? This cannot be undone.",
            "Metis",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _runtime.ClearAllChats();
            PrivacyStatus.Text = "Chat history deleted.";
        }
        catch (Exception exception)
        {
            PrivacyStatus.Text = exception.Message;
        }
    }

    private void OpenSkills_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _runtime.ReloadUserSkills();
            var folder = SkillsFolderBox.Text.Trim();
            if (!Path.IsPathRooted(folder))
            {
                folder = Path.Combine(AppContext.BaseDirectory, folder);
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            SkillsStatus.Text = folder;
        }
        catch (Exception exception)
        {
            SkillsStatus.Text = exception.Message;
        }
    }

    private void OpenLogs_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(_runtime.LogPath) ?? AppContext.BaseDirectory;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer is a convenience, never worth surfacing.
        }
    }

    private void RefreshDiagnostics_OnClick(object sender, RoutedEventArgs e) => RefreshDiagnostics();

    private void OpenAssistant_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _showAssistant();
    }

    // --------------------------------------------------------------- search

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (query.Length < 2)
        {
            return;
        }

        var match = Sections.FirstOrDefault(section =>
            section.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || section.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match.Page is not null && match.Page != _page)
        {
            ShowPage(match.Page);
        }
    }

    private void Find_OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.SelectAll();
        SearchBox.Focus();
    }

    private void Close_OnExecuted(object sender, ExecutedRoutedEventArgs e) => Hide();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Hide();

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
