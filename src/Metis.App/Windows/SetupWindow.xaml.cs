using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Metis.App.Runtime;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.App.Windows;

public partial class SetupWindow : Window
{
    private readonly MetisRuntime _runtime;
    private readonly Action _showAssistant;
    private readonly DispatcherTimer _companionSaveTimer;
    private bool _allowClose;
    private bool _loading;
    private string _selectedCompanionColor = CompanionPalette.DefaultName;

    public SetupWindow(MetisRuntime runtime, Action showAssistant)
    {
        InitializeComponent();
        _runtime = runtime;
        _showAssistant = showAssistant;
        _companionSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _companionSaveTimer.Tick += CompanionSaveTimer_OnTick;
        _runtime.StatusChanged += Runtime_OnStatusChanged;
        Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
                Hide();
            }
        };
        RefreshFromRuntime();
    }

    public void RefreshFromRuntime()
    {
        if (!IsInitialized)
        {
            return;
        }

        _loading = true;
        var settings = _runtime.Settings;
        GeminiKeyStatusText.Text = _runtime.HasGeminiKey
            ? "A Gemini key is stored securely. Leave this blank to keep it."
            : "No Gemini key is stored yet.";
        OpenAiKeyStatusText.Text = _runtime.HasOpenAiKey
            ? "An OpenAI key is stored securely. Leave this blank to keep it."
            : "No OpenAI key is stored yet.";
        ClaudeKeyStatusText.Text = _runtime.HasClaudeKey
            ? "An Anthropic key is stored securely. Leave this blank to keep it."
            : "No Anthropic key is stored yet.";
        OpenClawStatusText.Text = _runtime.HasOpenClawToken
            ? "A gateway token is stored securely. Leave this blank to keep it."
            : "No gateway token is stored. This is fine when the gateway does not require one.";
        AssemblyAiStatusText.Text = _runtime.HasAssemblyAiKey
            ? "An AssemblyAI key is stored securely. Leave this blank to keep it."
            : "No AssemblyAI key is stored yet.";
        ElevenLabsStatusText.Text = _runtime.HasElevenLabsKey
            ? "An ElevenLabs key is stored securely. Leave this blank to keep it."
            : "No ElevenLabs key is stored yet.";
        OllamaStatusText.Text = "Ollama runs locally and does not require an API key.";
        GeminiApiKeyBox.Password = string.Empty;
        OpenAiApiKeyBox.Password = string.Empty;
        ClaudeApiKeyBox.Password = string.Empty;
        OpenClawTokenBox.Password = string.Empty;
        AssemblyAiApiKeyBox.Password = string.Empty;
        ElevenLabsApiKeyBox.Password = string.Empty;
        SelectComboItem(ProviderBox, settings.AiProvider);
        ReasoningModelBox.Text = settings.ReasoningModel;
        SpeechModelBox.Text = settings.SpeechModel;
        SelectComboItem(VoiceBox, settings.VoiceName);
        OpenAiReasoningModelBox.Text = settings.OpenAiReasoningModel;
        OpenAiTranscriptionModelBox.Text = settings.OpenAiTranscriptionModel;
        OpenAiSpeechModelBox.Text = settings.OpenAiSpeechModel;
        SelectComboItem(OpenAiVoiceBox, settings.OpenAiVoiceName);
        ClaudeReasoningModelBox.Text = settings.ClaudeReasoningModel;
        OpenClawEndpointBox.Text = settings.OpenClawEndpoint;
        OpenClawModelBox.Text = settings.OpenClawModel;
        OllamaEndpointBox.Text = settings.OllamaEndpoint;
        OllamaModelBox.Text = settings.OllamaModel;
        LocalContextTokensBox.Text = settings.LocalContextTokens.ToString();
        SelectComboItem(SpeechToTextProviderBox, settings.SpeechToTextProvider);
        AssemblyAiModelBox.Text = settings.AssemblyAiModel;
        WhisperCppExecutablePathBox.Text = settings.WhisperCppExecutablePath;
        WhisperCppModelPathBox.Text = settings.WhisperCppModelPath;
        WhisperCppStatusText.Text = "Runs locally; use Test to verify the executable and Tiny model.";
        SelectComboItem(TextToSpeechProviderBox, settings.TextToSpeechProvider);
        ElevenLabsModelBox.Text = settings.ElevenLabsModel;
        ElevenLabsVoiceIdBox.Text = settings.ElevenLabsVoiceId;
        PiperExecutablePathBox.Text = settings.PiperExecutablePath;
        PiperVoiceModelPathBox.Text = settings.PiperVoiceModelPath;
        PiperStatusText.Text = "Runs locally; use Test to generate a short WAV.";
        ChatterboxEndpointBox.Text = settings.ChatterboxEndpoint;
        ChatterboxModelBox.Text = settings.ChatterboxModel;
        ChatterboxVoiceBox.Text = settings.ChatterboxVoice;
        ChatterboxStatusText.Text = "The server must be running on this PC before Metis can speak.";
        CompanionSizeSlider.Value = settings.CompanionSize;
        CursorDistanceSlider.Value = settings.CursorDistance;
        CompanionSizeValue.Text = $"{settings.CompanionSize}px";
        CursorDistanceValue.Text = $"{settings.CursorDistance}px";
        CaptureScreenCheck.IsChecked = settings.CaptureActiveWindow;
        FullControlCheck.IsChecked = settings.FullDesktopControl;
        SpeechEnabledCheck.IsChecked = settings.SpeechEnabled;
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        SelectComboItem(OperatingModeBox, AssistanceModes.Name(AssistanceModes.Parse(settings.OperatingMode)));
        ContextShortcutsCheck.IsChecked = settings.ContextShortcutsEnabled;
        WakeWordBox.Text = WakeWordListener.Normalize(settings.WakeWord);
        MoveRealCursorCheck.IsChecked = settings.MoveRealCursor;
        SupabaseUrlBox.Text = settings.SupabaseUrl;
        SupabaseAnonKeyBox.Text = settings.SupabaseAnonKey;
        // Read back through the parser rather than off the raw string, so a
        // hand-edited or empty value lands on Production instead of throwing.
        SelectComboItem(
            MetisEnvironmentBox,
            Entitlements.ParseEnvironment(settings.MetisEnvironment).ToString());
        VisualGuidanceCheck.IsChecked = settings.VisualGuidanceEnabled;
        MemoryEnabledCheck.IsChecked = settings.MemoryEnabled;
        ActivationSoundsCheck.IsChecked = settings.ActivationSoundsEnabled;
        SpeakErrorsCheck.IsChecked = settings.SpeakErrorsAloud;
        SoundPackPathBox.Text = settings.SoundPackPath;
        ChatMemoryCheck.IsChecked = settings.ChatMemoryEnabled;
        UserSkillsCheck.IsChecked = settings.UserSkillsEnabled;
        SkillsFolderBox.Text = settings.SkillsFolder;
        SkillsStatusText.Text = DescribeLoadedSkills();
        BuildCompanionColorSwatches(settings.CompanionColor);
        UpdateOperatingModeDescription();
        MemoryStatusText.Text = $"Stored at {_runtime.MemoryPath}";
        RefreshMicrophones(settings.PreferredMicrophoneId);
        InlineStatusText.Text = _runtime.CurrentStatus;
        DiagnosticsRuntimeText.Text = _runtime.CurrentStatus;
        UpdateCapabilityPanels();
        _loading = false;
    }

    /// <summary>
    /// Renders the palette as swatches rather than a dropdown: the choice is
    /// entirely visual, so showing the colours is the whole point.
    /// </summary>
    private void BuildCompanionColorSwatches(string selectedName)
    {
        _selectedCompanionColor = CompanionPalette.Normalize(selectedName);
        CompanionColorList.Items.Clear();

        foreach (var option in CompanionPalette.All)
        {
            var isSelected = string.Equals(option.Name, _selectedCompanionColor, StringComparison.OrdinalIgnoreCase);
            var swatch = new System.Windows.Controls.Border
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(17),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(option.Fill)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(option.Glow)),
                // The selected swatch is ringed rather than ticked, so the
                // colour itself is never obscured by an indicator.
                BorderThickness = new Thickness(isSelected ? 4 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = option.Name,
                Tag = option.Name
            };

            swatch.MouseLeftButtonUp += CompanionColorSwatch_OnClick;
            CompanionColorList.Items.Add(swatch);
        }
    }

    private void CompanionColorSwatch_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name })
        {
            return;
        }

        BuildCompanionColorSwatches(name);
        if (_loading)
        {
            return;
        }

        // Saved immediately so the companion changes colour as it is picked,
        // which is the only way to judge a colour.
        _companionSaveTimer.Stop();
        _companionSaveTimer.Start();
    }

    public void AllowClose() => _allowClose = true;

    /// <summary>Opens the window out of the notch at the given anchor.</summary>
    public void OpenFromNotch(double anchorBottom)
    {
        RefreshFromRuntime();
        NotchAnchor.Position(this, anchorBottom);
        Show();
        Activate();
        NotchAnchor.AnimateOpen(RootShell);
    }

    /// <summary>Folds back into the notch rather than simply disappearing.</summary>
    public void FoldIntoNotch()
    {
        if (IsVisible)
        {
            NotchAnchor.AnimateClose(RootShell, Hide);
        }
    }

    private async void Save_OnClick(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            if (!TryValidateSelectedConfiguration(out var validationMessage))
            {
                InlineStatusText.Text = validationMessage;
                return;
            }

            await SavePendingSettingsAsync();
            RefreshFromRuntime();
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void GeminiFindModels_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SavePendingSettingsAsync();
            var models = await _runtime.GetModelsAsync();
            var current = ReasoningModelBox.Text;
            ReasoningModelBox.Items.Clear();
            foreach (var model in models)
            {
                ReasoningModelBox.Items.Add(model.Name);
            }
            ReasoningModelBox.Text = current;
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private async void GeminiTestModel_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SavePendingSettingsAsync();
            var result = await _runtime.TestModelAsync(ReasoningModelBox.Text.Trim());
            InlineStatusText.Text = result.Message;
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private async void GeminiTestAllModels_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SavePendingSettingsAsync();
            var results = await _runtime.TestAllModelsAsync();
            var working = results.Where(result => result.Success).Select(result => result.Model).ToArray();
            var failed = results.Where(result => !result.Success).Take(3).ToArray();
            var summary = $"Working ({working.Length}): {string.Join(", ", working)}";
            if (failed.Length > 0)
            {
                summary += $"\nFirst failures: {string.Join(" | ", failed.Select(result => $"{result.Model}: {result.Message}"))}";
            }
            InlineStatusText.Text = summary;
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private async void OpenAiFindModels_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SavePendingSettingsAsync();
            var models = await _runtime.GetOpenAiModelsAsync();
            var current = OpenAiReasoningModelBox.Text;
            OpenAiReasoningModelBox.Items.Clear();
            foreach (var model in models)
            {
                OpenAiReasoningModelBox.Items.Add(model.Name);
            }
            OpenAiReasoningModelBox.Text = current;
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private async void OpenAiTestModel_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SavePendingSettingsAsync();
            var result = await _runtime.TestOpenAiModelAsync(OpenAiReasoningModelBox.Text.Trim());
            InlineStatusText.Text = result.Message;
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private async Task SavePendingSettingsAsync()
    {
        _runtime.SaveAdditionalProviderSecrets(
            NullIfWhiteSpace(ClaudeApiKeyBox.Password),
            NullIfWhiteSpace(OpenClawTokenBox.Password),
            NullIfWhiteSpace(AssemblyAiApiKeyBox.Password),
            NullIfWhiteSpace(ElevenLabsApiKeyBox.Password));
        await _runtime.SaveSettingsAsync(
            BuildSettings(),
            NullIfWhiteSpace(GeminiApiKeyBox.Password),
            NullIfWhiteSpace(OpenAiApiKeyBox.Password));
        GeminiApiKeyBox.Password = string.Empty;
        OpenAiApiKeyBox.Password = string.Empty;
        ClaudeApiKeyBox.Password = string.Empty;
        OpenClawTokenBox.Password = string.Empty;
        AssemblyAiApiKeyBox.Password = string.Empty;
        ElevenLabsApiKeyBox.Password = string.Empty;
    }

    private AppSettings BuildSettings()
    {
        var provider = (ProviderBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Gemini";
        var geminiVoice = (VoiceBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Kore";
        var openAiVoice = (OpenAiVoiceBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "alloy";
        return _runtime.Settings with
        {
            AiProvider = provider,
            ReasoningModel = ReasoningModelBox.Text,
            SpeechModel = SpeechModelBox.Text,
            VoiceName = geminiVoice,
            OpenAiReasoningModel = OpenAiReasoningModelBox.Text,
            OpenAiTranscriptionModel = OpenAiTranscriptionModelBox.Text,
            OpenAiSpeechModel = OpenAiSpeechModelBox.Text,
            OpenAiVoiceName = openAiVoice,
            ClaudeReasoningModel = ClaudeReasoningModelBox.Text,
            OpenClawEndpoint = OpenClawEndpointBox.Text,
            OpenClawModel = OpenClawModelBox.Text,
            OllamaEndpoint = OllamaEndpointBox.Text,
            OllamaModel = OllamaModelBox.Text,
            LocalContextTokens = ParseContextTokens(LocalContextTokensBox.Text),
            SpeechToTextProvider = SelectedContent(SpeechToTextProviderBox, "Native"),
            AssemblyAiModel = AssemblyAiModelBox.Text,
            WhisperCppExecutablePath = WhisperCppExecutablePathBox.Text,
            WhisperCppModelPath = WhisperCppModelPathBox.Text,
            TextToSpeechProvider = SelectedContent(TextToSpeechProviderBox, "Native"),
            ElevenLabsModel = ElevenLabsModelBox.Text,
            ElevenLabsVoiceId = ElevenLabsVoiceIdBox.Text,
            PiperExecutablePath = PiperExecutablePathBox.Text,
            PiperVoiceModelPath = PiperVoiceModelPathBox.Text,
            ChatterboxEndpoint = ChatterboxEndpointBox.Text,
            ChatterboxModel = ChatterboxModelBox.Text,
            ChatterboxVoice = ChatterboxVoiceBox.Text,
            CompanionSize = (int)Math.Round(CompanionSizeSlider.Value),
            CursorDistance = (int)Math.Round(CursorDistanceSlider.Value),
            CaptureActiveWindow = CaptureScreenCheck.IsChecked == true,
            FullDesktopControl = FullControlCheck.IsChecked == true,
            SpeechEnabled = SpeechEnabledCheck.IsChecked == true,
            StartWithWindows = StartWithWindowsCheck.IsChecked == true,
            OperatingMode = SelectedContent(OperatingModeBox, "Learn"),
            ContextShortcutsEnabled = ContextShortcutsCheck.IsChecked == true,
            WakeWord = WakeWordListener.Normalize(WakeWordBox.Text),
            MoveRealCursor = MoveRealCursorCheck.IsChecked == true,
            SupabaseUrl = SupabaseUrlBox.Text.Trim(),
            SupabaseAnonKey = SupabaseAnonKeyBox.Text.Trim(),
            MetisEnvironment = SelectedContent(MetisEnvironmentBox, "Production").ToLowerInvariant(),
            VisualGuidanceEnabled = VisualGuidanceCheck.IsChecked == true,
            MemoryEnabled = MemoryEnabledCheck.IsChecked == true,
            ActivationSoundsEnabled = ActivationSoundsCheck.IsChecked == true,
            SpeakErrorsAloud = SpeakErrorsCheck.IsChecked == true,
            SoundPackPath = SoundPackPathBox.Text,
            ChatMemoryEnabled = ChatMemoryCheck.IsChecked == true,
            UserSkillsEnabled = UserSkillsCheck.IsChecked == true,
            SkillsFolder = SkillsFolderBox.Text,
            CompanionColor = _selectedCompanionColor,
            PreferredMicrophoneId = MicrophoneBox.SelectedValue?.ToString()
        };
    }

    private void OperatingModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateOperatingModeDescription();
        }
    }

    private void UpdateOperatingModeDescription() =>
        OperatingModeDescription.Text =
            AssistanceModes.Describe(AssistanceModes.Parse(SelectedContent(OperatingModeBox, "Learn")));

    /// <summary>
    /// Names the skills that actually loaded, so a file with a typo in its
    /// header is visibly absent rather than silently ignored.
    /// </summary>
    private string DescribeLoadedSkills()
    {
        var skills = _runtime.UserSkills;
        if (skills.Count == 0)
        {
            return "No skills loaded yet. Add a markdown file to the folder and save to reload.";
        }

        return $"{skills.Count} loaded: {string.Join(", ", skills.Select(skill => skill.Name))}";
    }

    private void OpenSkillsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _runtime.ReloadUserSkills();
            var folder = _runtime.SkillsFolder;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }

            SkillsStatusText.Text = DescribeLoadedSkills();
        }
        catch (Exception exception)
        {
            SkillsStatusText.Text = exception.Message;
        }
    }

    private async void ClearMemory_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            "Delete everything Metis has learned about your skills and past tasks? This cannot be undone.",
            "Clear Metis memory",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _runtime.ClearMemoryAsync();
            MemoryStatusText.Text = "Memory cleared.";
        }
        catch (Exception exception)
        {
            MemoryStatusText.Text = $"Memory could not be cleared. {exception.Message}";
        }
    }

    private void CompanionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized)
        {
            return;
        }

        CompanionSizeValue.Text = $"{Math.Round(CompanionSizeSlider.Value)}px";
        CursorDistanceValue.Text = $"{Math.Round(CursorDistanceSlider.Value)}px";
        if (_loading)
        {
            return;
        }

        _companionSaveTimer.Stop();
        _companionSaveTimer.Start();
    }

    private async void CompanionSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _companionSaveTimer.Stop();
        try
        {
            await _runtime.SaveSettingsAsync(BuildSettings(), null, null);
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private void Runtime_OnStatusChanged(object? sender, string status) =>
        Dispatcher.Invoke(() =>
        {
            InlineStatusText.Text = status;
            DiagnosticsRuntimeText.Text = status;
        });

    private void ProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCapabilityPanels();

    private void SpeechToTextProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCapabilityPanels();

    private void TextToSpeechProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCapabilityPanels();

    private void UseFullyLocalPreset_OnClick(object sender, RoutedEventArgs e)
    {
        SelectComboItem(ProviderBox, "Ollama");
        OllamaEndpointBox.Text = "http://127.0.0.1:11434";
        OllamaModelBox.Text = "qwen3-vl:2b-instruct-q4_K_M";
        LocalContextTokensBox.Text = "2048";
        SelectComboItem(SpeechToTextProviderBox, "Whisper.cpp");
        WhisperCppExecutablePathBox.Text = @"tools\whisper.cpp\Release\whisper-cli.exe";
        WhisperCppModelPathBox.Text = @"models\whisper\ggml-tiny.bin";
        SelectComboItem(TextToSpeechProviderBox, "Piper");
        PiperExecutablePathBox.Text = @"tools\piper-standalone\piper\piper.exe";
        PiperVoiceModelPathBox.Text = @"models\piper\en_US-lessac-medium.onnx";
        CaptureScreenCheck.IsChecked = true;
        SpeechEnabledCheck.IsChecked = true;
        UpdateCapabilityPanels();
        InlineStatusText.Text =
            "Fully local preset selected. Install/pull the three local model runtimes, then use each Test button before saving.";
    }

    private void UpdateCapabilityPanels()
    {
        if (!IsInitialized)
        {
            return;
        }

        var provider = SelectedContent(ProviderBox, "Gemini");
        DiagnosticsProviderText.Text = provider;
        var automatic = provider.Equals("Automatic", StringComparison.OrdinalIgnoreCase);
        AutomaticCard.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
        GeminiCard.Visibility = provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) || automatic
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenAiCard.Visibility = provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) || automatic
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClaudeCard.Visibility = provider.Equals("Claude", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenClawCard.Visibility = provider.Equals("OpenClaw", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        OllamaCard.Visibility = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

        var speechToText = SelectedContent(SpeechToTextProviderBox, "Native");
        var usesAssemblyAi = speechToText.Equals("AssemblyAI", StringComparison.OrdinalIgnoreCase);
        var usesWhisperCpp = speechToText.Equals("Whisper.cpp", StringComparison.OrdinalIgnoreCase);
        NativeSpeechToTextCard.Visibility = usesAssemblyAi || usesWhisperCpp ? Visibility.Collapsed : Visibility.Visible;
        WhisperCppCard.Visibility = usesWhisperCpp ? Visibility.Visible : Visibility.Collapsed;
        AssemblyAiCard.Visibility = usesAssemblyAi ? Visibility.Visible : Visibility.Collapsed;
        OpenAiNativeTranscriptionPanel.Visibility = !usesAssemblyAi && !usesWhisperCpp
                                                    && (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                                                        || automatic)
            ? Visibility.Visible
            : Visibility.Collapsed;
        NativeSpeechToTextStatusText.Text = provider switch
        {
            "Gemini" => "Gemini receives Metis's microphone recording directly.",
            "OpenAI" => "OpenAI transcribes Metis's microphone recording with the model below.",
            "Automatic" => "Gemini handles the recording first; OpenAI uses the transcription model below when Metis falls back.",
            _ => $"{provider} does not accept Metis's native microphone recording. Select AssemblyAI for voice input."
        };

        var textToSpeech = SelectedContent(TextToSpeechProviderBox, "Native");
        DiagnosticsVoiceText.Text = $"{speechToText} STT  •  {textToSpeech} TTS";
        var usesElevenLabs = textToSpeech.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase);
        var usesPiper = textToSpeech.Equals("Piper", StringComparison.OrdinalIgnoreCase);
        var usesChatterbox = textToSpeech.Equals("Chatterbox-Nano", StringComparison.OrdinalIgnoreCase);
        var usesLocalVoice = usesPiper || usesChatterbox;
        NativeTextToSpeechCard.Visibility = usesElevenLabs || usesLocalVoice ? Visibility.Collapsed : Visibility.Visible;
        PiperCard.Visibility = usesPiper ? Visibility.Visible : Visibility.Collapsed;
        ChatterboxNanoCard.Visibility = usesChatterbox ? Visibility.Visible : Visibility.Collapsed;
        ElevenLabsCard.Visibility = usesElevenLabs ? Visibility.Visible : Visibility.Collapsed;
        GeminiNativeVoicePanel.Visibility = !usesElevenLabs && !usesLocalVoice
                                             && (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                                                 || automatic)
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenAiNativeVoicePanel.Visibility = !usesElevenLabs && !usesLocalVoice
                                             && (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                                                 || automatic)
            ? Visibility.Visible
            : Visibility.Collapsed;
        NativeTextToSpeechStatusText.Text = provider switch
        {
            "Gemini" => "Gemini generates Metis's voice with the model and voice below.",
            "OpenAI" => "OpenAI generates Metis's voice with the model and voice below.",
            "Automatic" => "Metis uses the voice settings that match the reasoning provider that answered.",
            _ when _runtime.HasGeminiKey || _runtime.HasOpenAiKey =>
                $"{provider} has no built-in Metis voice. Native mode falls back to a stored Gemini or OpenAI voice.",
            _ => $"{provider} has no built-in Metis voice. Select ElevenLabs or save a Gemini/OpenAI key for voice output."
        };
    }

    private void SetupNav_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button selectedButton)
        {
            return;
        }

        var target = selectedButton.Name switch
        {
            nameof(ModeNavButton) => ModeSection,
            nameof(CompanionNavButton) => CompanionSection,
            nameof(VoiceNavButton) => VoiceSection,
            nameof(ScreenNavButton) => ScreenSection,
            nameof(DiagnosticsNavButton) => DiagnosticsSection,
            _ => ReasoningSection
        };

        foreach (var button in new[]
                 {
                     ModeNavButton,
                     ReasoningNavButton,
                     CompanionNavButton,
                     VoiceNavButton,
                     ScreenNavButton,
                     DiagnosticsNavButton
                 })
        {
            button.Style = (Style)FindResource(button == selectedButton ? "ActiveNavButton" : "NavButton");
        }

        ScrollSectionToTop(target);
    }

    /// <summary>
    /// Scrolls a section to the top of the viewport. BringIntoView only scrolls
    /// far enough to make an element visible, so a section already partly on
    /// screen would not move at all and the nav button appeared to do nothing.
    /// </summary>
    private void ScrollSectionToTop(FrameworkElement section)
    {
        // Deferred so the offset is measured after any pending layout pass,
        // which matters the first time a section is reached.
        Dispatcher.BeginInvoke(
            () =>
            {
                if (!section.IsLoaded || SetupScrollViewer.ExtentHeight <= 0)
                {
                    return;
                }

                var offset = section
                    .TransformToAncestor(SetupScrollViewer)
                    .Transform(new System.Windows.Point(0, 0))
                    .Y + SetupScrollViewer.VerticalOffset;

                // A small lead-in keeps the heading clear of the top edge.
                SetupScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset - 18));
            },
            DispatcherPriority.Loaded);
    }

    private async void ValidateReasoningProvider_OnClick(object sender, RoutedEventArgs e)
    {
        var provider = (sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty;
        var (valid, message) = provider switch
        {
            "Claude" => ValidateCloudProvider(
                ClaudeReasoningModelBox.Text,
                _runtime.HasClaudeKey || !string.IsNullOrWhiteSpace(ClaudeApiKeyBox.Password),
                "Claude",
                "an Anthropic API key"),
            "OpenClaw" => ValidateLocalProvider(OpenClawEndpointBox.Text, OpenClawModelBox.Text, "OpenClaw"),
            "Ollama" => ValidateLocalProvider(OllamaEndpointBox.Text, OllamaModelBox.Text, "Ollama"),
            _ => (false, "Choose a provider before testing its configuration.")
        };

        if (provider == "OpenClaw")
        {
            OpenClawStatusText.Text = message;
        }
        else if (provider == "Ollama")
        {
            OllamaStatusText.Text = message;
        }
        else
        {
            ClaudeKeyStatusText.Text = message;
        }

        if (!valid)
        {
            InlineStatusText.Text = message;
            return;
        }

        try
        {
            await SavePendingSettingsAsync();
            var model = provider switch
            {
                "Claude" => ClaudeReasoningModelBox.Text.Trim(),
                "OpenClaw" => OpenClawModelBox.Text.Trim(),
                "Ollama" => OllamaModelBox.Text.Trim(),
                _ => string.Empty
            };
            var result = await _runtime.TestReasoningProviderAsync(provider, model);
            InlineStatusText.Text = result.Message;
            if (provider == "OpenClaw")
            {
                OpenClawStatusText.Text = result.Message;
            }
            else if (provider == "Ollama")
            {
                OllamaStatusText.Text = result.Message;
            }
            else
            {
                ClaudeKeyStatusText.Text = result.Message;
            }
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private async void ValidateSpeechProvider_OnClick(object sender, RoutedEventArgs e)
    {
        var provider = (sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty;
        var (valid, message) = provider switch
        {
            "Whisper.cpp" => ValidateLocalFiles(
                WhisperCppExecutablePathBox.Text,
                WhisperCppModelPathBox.Text,
                "whisper.cpp"),
            "Piper" => ValidateLocalFiles(
                PiperExecutablePathBox.Text,
                PiperVoiceModelPathBox.Text,
                "Piper"),
            "Chatterbox-Nano" => ValidateLoopbackProvider(
                ChatterboxEndpointBox.Text,
                ChatterboxModelBox.Text,
                "Chatterbox-Nano"),
            "AssemblyAI" => ValidateCloudProvider(
                AssemblyAiModelBox.Text,
                _runtime.HasAssemblyAiKey || !string.IsNullOrWhiteSpace(AssemblyAiApiKeyBox.Password),
                "AssemblyAI",
                "an AssemblyAI API key"),
            "ElevenLabs" => ValidateCloudProvider(
                ElevenLabsModelBox.Text,
                _runtime.HasElevenLabsKey || !string.IsNullOrWhiteSpace(ElevenLabsApiKeyBox.Password),
                "ElevenLabs",
                "an ElevenLabs API key"),
            _ => (false, "Choose a speech provider before testing its configuration.")
        };

        SetSpeechProviderStatus(provider, message);

        if (!valid)
        {
            InlineStatusText.Text = message;
            return;
        }

        try
        {
            await SavePendingSettingsAsync();
            if (provider == "AssemblyAI")
            {
                var result = await _runtime.TestAssemblyAiAsync();
                AssemblyAiStatusText.Text = result.Message;
                InlineStatusText.Text = result.Message;
                return;
            }

            if (provider == "Whisper.cpp")
            {
                var result = await _runtime.TestWhisperCppAsync();
                SetSpeechProviderStatus(provider, result.Message);
                InlineStatusText.Text = result.Message;
                return;
            }

            if (provider == "Piper")
            {
                var result = await _runtime.TestPiperAsync();
                SetSpeechProviderStatus(provider, result.Message);
                InlineStatusText.Text = result.Message;
                return;
            }

            if (provider == "Chatterbox-Nano")
            {
                var result = await _runtime.TestChatterboxNanoAsync();
                SetSpeechProviderStatus(provider, result.Message);
                InlineStatusText.Text = result.Message;
                return;
            }

            var elevenLabsResult = await _runtime.TestElevenLabsAsync();
            var voices = await _runtime.GetElevenLabsVoicesAsync();
            if (string.IsNullOrWhiteSpace(ElevenLabsVoiceIdBox.Text) && voices.Count > 0)
            {
                ElevenLabsVoiceIdBox.Text = voices[0].Id;
                await SavePendingSettingsAsync();
            }

            var voiceMessage = voices.Count == 0
                ? elevenLabsResult.Message
                : $"{elevenLabsResult.Message} Found {voices.Count} voices; current ID: {ElevenLabsVoiceIdBox.Text}.";
            ElevenLabsStatusText.Text = voiceMessage;
            InlineStatusText.Text = voiceMessage;
        }
        catch (Exception exception)
        {
            InlineStatusText.Text = exception.Message;
        }
    }

    private void SetSpeechProviderStatus(string provider, string message)
    {
        switch (provider)
        {
            case "AssemblyAI":
                AssemblyAiStatusText.Text = message;
                break;
            case "ElevenLabs":
                ElevenLabsStatusText.Text = message;
                break;
            case "Whisper.cpp":
                WhisperCppStatusText.Text = message;
                break;
            case "Piper":
                PiperStatusText.Text = message;
                break;
            case "Chatterbox-Nano":
                ChatterboxStatusText.Text = message;
                break;
        }
    }

    private bool TryValidateSelectedConfiguration(out string message)
    {
        var provider = SelectedContent(ProviderBox, "Gemini");
        var reasoningValidation = provider switch
        {
            "Gemini" => ValidateCloudProvider(
                ReasoningModelBox.Text,
                _runtime.HasGeminiKey || !string.IsNullOrWhiteSpace(GeminiApiKeyBox.Password),
                "Gemini",
                "a Gemini API key"),
            "OpenAI" => ValidateCloudProvider(
                OpenAiReasoningModelBox.Text,
                _runtime.HasOpenAiKey || !string.IsNullOrWhiteSpace(OpenAiApiKeyBox.Password),
                "OpenAI",
                "an OpenAI Platform API key"),
            "Claude" => ValidateCloudProvider(
                ClaudeReasoningModelBox.Text,
                _runtime.HasClaudeKey || !string.IsNullOrWhiteSpace(ClaudeApiKeyBox.Password),
                "Claude",
                "an Anthropic API key"),
            "OpenClaw" => ValidateLocalProvider(OpenClawEndpointBox.Text, OpenClawModelBox.Text, "OpenClaw"),
            "Ollama" => ValidateLocalProvider(OllamaEndpointBox.Text, OllamaModelBox.Text, "Ollama"),
            "Automatic" when !_runtime.HasGeminiKey
                             && !_runtime.HasOpenAiKey
                             && string.IsNullOrWhiteSpace(GeminiApiKeyBox.Password)
                             && string.IsNullOrWhiteSpace(OpenAiApiKeyBox.Password) =>
                (false, "Automatic mode needs a Gemini or OpenAI API key."),
            _ => (true, string.Empty)
        };
        if (!reasoningValidation.Item1)
        {
            message = reasoningValidation.Item2;
            return false;
        }

        if (SelectedContent(SpeechToTextProviderBox, "Native") == "AssemblyAI")
        {
            var transcriptionValidation = ValidateCloudProvider(
                AssemblyAiModelBox.Text,
                _runtime.HasAssemblyAiKey || !string.IsNullOrWhiteSpace(AssemblyAiApiKeyBox.Password),
                "AssemblyAI",
                "an AssemblyAI API key");
            if (!transcriptionValidation.Valid)
            {
                message = transcriptionValidation.Message;
                return false;
            }
        }

        if (SelectedContent(SpeechToTextProviderBox, "Native") == "Whisper.cpp")
        {
            var transcriptionValidation = ValidateLocalFiles(
                WhisperCppExecutablePathBox.Text,
                WhisperCppModelPathBox.Text,
                "whisper.cpp");
            if (!transcriptionValidation.Valid)
            {
                message = transcriptionValidation.Message;
                return false;
            }
        }

        if (SelectedContent(TextToSpeechProviderBox, "Native") == "ElevenLabs")
        {
            if (string.IsNullOrWhiteSpace(ElevenLabsVoiceIdBox.Text))
            {
                message = "Enter an ElevenLabs voice ID.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ElevenLabsModelBox.Text))
            {
                message = "Enter an ElevenLabs voice model.";
                return false;
            }

            if (!_runtime.HasElevenLabsKey && string.IsNullOrWhiteSpace(ElevenLabsApiKeyBox.Password))
            {
                message = "Enter an ElevenLabs API key.";
                return false;
            }
        }

        if (SelectedContent(TextToSpeechProviderBox, "Native") == "Piper")
        {
            var voiceValidation = ValidateLocalFiles(
                PiperExecutablePathBox.Text,
                PiperVoiceModelPathBox.Text,
                "Piper");
            if (!voiceValidation.Valid)
            {
                message = voiceValidation.Message;
                return false;
            }
        }

        if (SelectedContent(TextToSpeechProviderBox, "Native") == "Chatterbox-Nano")
        {
            var voiceValidation = ValidateLoopbackProvider(
                ChatterboxEndpointBox.Text,
                ChatterboxModelBox.Text,
                "Chatterbox-Nano");
            if (!voiceValidation.Valid)
            {
                message = voiceValidation.Message;
                return false;
            }
        }

        message = "Provider settings are valid.";
        return true;
    }

    private static (bool Valid, string Message) ValidateModel(string value, string provider) =>
        string.IsNullOrWhiteSpace(value)
            ? (false, $"Enter a {provider} model name.")
            : (true, $"{provider} configuration looks valid.");

    private static (bool Valid, string Message) ValidateCloudProvider(
        string model,
        bool hasCredential,
        string provider,
        string credentialLabel)
    {
        var modelValidation = ValidateModel(model, provider);
        if (!modelValidation.Valid)
        {
            return modelValidation;
        }

        return hasCredential
            ? (true, $"{provider} fields are complete.")
            : (false, $"Enter {credentialLabel}.");
    }

    private static (bool Valid, string Message) ValidateLocalProvider(string endpoint, string model, string provider)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, $"Enter a valid http or https address for {provider}.");
        }

        return ValidateModel(model, provider);
    }

    private static (bool Valid, string Message) ValidateLocalFiles(
        string executable,
        string model,
        string provider)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return (false, $"Enter the {provider} executable path.");
        }

        return string.IsNullOrWhiteSpace(model)
            ? (false, $"Enter the {provider} model path.")
            : (true, $"{provider} paths are set. Use Test to verify the files.");
    }

    private static (bool Valid, string Message) ValidateLoopbackProvider(
        string endpoint,
        string model,
        string provider)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !uri.IsLoopback)
        {
            return (false, $"{provider} must use a local address such as http://127.0.0.1:4123/v1.");
        }

        return ValidateModel(model, provider);
    }

    private static int ParseContextTokens(string value) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 2048, 4096) : 2048;

    private void OpenAssistant_OnClick(object sender, RoutedEventArgs e) => _showAssistant();

    private void OpenLogs_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(_runtime.LogPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Hide();


    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SelectedContent(System.Windows.Controls.ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

    private static void SelectComboItem(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void RefreshMicrophones(string? preferredId)
    {
        try
        {
            var devices = _runtime.GetInputDevices();
            MicrophoneBox.ItemsSource = devices;
            MicrophoneBox.SelectedValue = preferredId ?? devices.FirstOrDefault()?.Id;
            MicrophoneStatusText.Text = devices.Count == 0
                ? "No microphone is available. Check Windows microphone privacy settings."
                : $"{devices.Count} microphone input{(devices.Count == 1 ? string.Empty : "s")} available.";
        }
        catch (Exception exception)
        {
            MicrophoneBox.ItemsSource = null;
            MicrophoneStatusText.Text = $"Windows could not list microphones. {exception.Message}";
        }
    }
}
