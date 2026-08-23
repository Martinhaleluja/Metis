using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Metis.App.Runtime;
using Metis.App.Theme;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

// The project enables both WPF and Windows Forms, so these names are ambiguous
// under implicit usings. Everything here is WPF.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Metis.App.Windows;

/// <summary>
/// The first-run flow. Each step writes its own settings as the user leaves it,
/// so quitting halfway never loses an API key, and
/// <see cref="AppSettings.OnboardingCompleted"/> is only set on the final step.
/// </summary>
public partial class OnboardingWindow : System.Windows.Window
{
    private const int LastStep = 7;

    private readonly MetisRuntime _runtime;
    private readonly ThemeService? _theme;
    private readonly Action _onFinished;
    private readonly List<StackPanel> _steps = [];
    private readonly List<Border> _dots = [];

    private int _index;
    private bool _allowClose;
    private string _companionColour = CompanionPalette.DefaultName;
    private string _themePreference = "System";

    public OnboardingWindow(MetisRuntime runtime, ThemeService? theme, Action onFinished)
    {
        _runtime = runtime;
        _theme = theme;
        _onFinished = onFinished;
        InitializeComponent();

        _steps.AddRange([Step0, Step1, Step2, Step3, Step4, Step5, Step6, Step7]);
        BuildStepDots();
        BuildColourSwatches();
        LoadFromSettings();

        CompanionSizeSlider.ValueChanged += (_, _) =>
            CompanionSizeValue.Text = ((int)CompanionSizeSlider.Value).ToString();

        // Same contract as every other Metis window: closing hides rather than
        // exits, so the tray icon stays the single owner of the app's lifetime.
        Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
                Hide();
            }
        };

        ShowStep(0);
    }

    public void AllowClose() => _allowClose = true;

    private void LoadFromSettings()
    {
        var settings = _runtime.Settings;

        _companionColour = settings.CompanionColor;
        _themePreference = settings.ThemePreference;

        CompanionSizeSlider.Value = settings.CompanionSize;
        CompanionSizeValue.Text = settings.CompanionSize.ToString();
        CaptureScreenCheck.IsChecked = settings.CaptureActiveWindow;
        ContextShortcutsCheck.IsChecked = settings.ContextShortcutsEnabled;
        SpeechEnabledCheck.IsChecked = settings.SpeechEnabled;
        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        OllamaEndpointBox.Text = settings.OllamaEndpoint;
        OllamaModelBox.Text = settings.OllamaModel;

        ThemeLight.IsChecked = settings.ThemePreference == "Light";
        ThemeDark.IsChecked = settings.ThemePreference == "Dark";
        ThemeSystem.IsChecked = settings.ThemePreference is not ("Light" or "Dark");

        SelectProvider(settings.AiProvider);
        if (settings.AiProvider == "Ollama")
        {
            LocalChoice.IsChecked = true;
        }

        UpdateCaptureExplanation();
        LoadMicrophones(settings.PreferredMicrophoneId);
    }

    private void SelectProvider(string provider)
    {
        foreach (var item in ProviderBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals((string?)item.Content, provider, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    private string SelectedProvider() =>
        (ProviderBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Gemini";

    private void LoadMicrophones(string? preferredId)
    {
        try
        {
            var devices = _runtime.GetInputDevices();
            MicrophoneBox.ItemsSource = devices;
            MicrophoneBox.DisplayMemberPath = nameof(AudioDeviceInfo.Name);
            MicrophoneBox.SelectedValuePath = nameof(AudioDeviceInfo.Id);
            MicrophoneBox.SelectedValue = preferredId ?? devices.FirstOrDefault()?.Id;

            if (devices.Count == 0)
            {
                MicrophoneStatus.Text = "No microphone is available. Check Windows microphone privacy settings.";
                OpenMicSettings.Visibility = Visibility.Visible;
            }
            else
            {
                MicrophoneStatus.Text = $"{devices.Count} input device(s) found.";
                OpenMicSettings.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception exception)
        {
            MicrophoneBox.ItemsSource = null;
            MicrophoneStatus.Text = exception.Message;
            OpenMicSettings.Visibility = Visibility.Visible;
        }
    }

    private void BuildStepDots()
    {
        for (var step = 0; step <= LastStep; step++)
        {
            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _dots.Add(dot);
        }

        StepDots.ItemsSource = _dots;
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
            };

            swatches.Add(ring);
        }

        ColourSwatches.ItemsSource = swatches;
    }

    private void ShowStep(int index)
    {
        _index = Math.Clamp(index, 0, LastStep);

        for (var step = 0; step < _steps.Count; step++)
        {
            _steps[step].Visibility = step == _index ? Visibility.Visible : Visibility.Collapsed;
        }

        for (var dot = 0; dot < _dots.Count; dot++)
        {
            _dots[dot].Background = dot <= _index
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderStrongBrush");
        }

        BackButton.Visibility = _index == 0 ? Visibility.Hidden : Visibility.Visible;
        SkipButton.Visibility = _index == LastStep ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _index switch
        {
            0 => "Get started",
            LastStep => "Start using Metis",
            _ => "Continue"
        };

        if (_index == 2)
        {
            UpdateCaptureExplanation();
        }
    }

    /// <summary>
    /// The disclosure has to match what the chosen provider actually does with
    /// the screenshot, so it is rebuilt from the current selection rather than
    /// written once in markup.
    /// </summary>
    private void UpdateCaptureExplanation()
    {
        var local = LocalChoice.IsChecked == true;
        CaptureExplanation.Text = local
            ? "When you ask a question, Metis takes a picture of your whole desktop — every monitor — and processes it on this PC. Nothing is sent anywhere. It doesn't watch continuously, and it doesn't record."
            : $"When you ask a question, Metis takes a picture of your whole desktop — every monitor — and sends it to {SelectedProvider()} to answer. It doesn't watch continuously, and it doesn't record.";
    }

    private void BrainChoice_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var local = LocalChoice.IsChecked == true;
        CloudPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        LocalPanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        UpdateCaptureExplanation();
    }

    private void ProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection is raised during InitializeComponent by the IsSelected in
        // markup, before the named fields this reads have been assigned.
        if (!IsInitialized)
        {
            return;
        }

        ProviderNote.Visibility = SelectedProvider() == "OpenRouter"
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateCaptureExplanation();
    }

    private void Theme_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        _themePreference = ThemeLight.IsChecked == true ? "Light"
            : ThemeDark.IsChecked == true ? "Dark"
            : "System";

        // Applied immediately so the choice is judged by looking at it.
        _theme?.Apply(_themePreference);
    }

    private async void TestProvider_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ProviderStatus.Text = "Testing…";
            await PersistAsync();

            var provider = SelectedProvider();
            var result = provider switch
            {
                "OpenAI" => await _runtime.TestOpenAiModelAsync(_runtime.Settings.OpenAiReasoningModel),
                // TestModelAsync is the Gemini path; the endpoint providers
                // (Claude, OpenClaw, Ollama) go through the unified one.
                "Gemini" => await _runtime.TestModelAsync(_runtime.Settings.ReasoningModel),
                "OpenRouter" => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.OpenRouterModel),
                _ => await _runtime.TestReasoningProviderAsync(provider, _runtime.Settings.ClaudeReasoningModel)
            };

            ProviderStatus.Text = result.Message;
        }
        catch (Exception exception)
        {
            ProviderStatus.Text = exception.Message;
        }
    }

    private void OpenMicSettings_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MicrophoneStatus.Text = exception.Message;
        }
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Back_OnClick(object sender, RoutedEventArgs e) => ShowStep(_index - 1);

    private async void Next_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await PersistAsync();

            if (_index == LastStep)
            {
                await PersistAsync(completed: true);
                _onFinished();
                Hide();
                return;
            }

            ShowStep(_index + 1);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Metis could not save that step. {exception.Message}",
                "Metis setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void Skip_OnClick(object sender, RoutedEventArgs e)
    {
        // Skipping still keeps whatever was filled in, but deliberately leaves
        // OnboardingCompleted false so the wizard offers itself again.
        try
        {
            await PersistAsync();
        }
        catch (Exception)
        {
            // Skipping should never be blocked by a save failure.
        }

        _onFinished();
        Hide();
    }

    private async Task PersistAsync(bool completed = false)
    {
        var settings = BuildSettings(completed);

        var provider = SelectedProvider();
        string? gemini = null;
        string? openAi = null;
        string? claude = null;
        string? openRouter = null;

        var key = ApiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(key) && LocalChoice.IsChecked != true)
        {
            switch (provider)
            {
                case "OpenAI": openAi = key; break;
                case "Claude": claude = key; break;
                case "OpenRouter": openRouter = key; break;
                default: gemini = key; break;
            }
        }

        if (claude is not null || openRouter is not null)
        {
            _runtime.SaveAdditionalProviderSecrets(claude, null, null, null, openRouter);
        }

        await _runtime.SaveSettingsAsync(settings, gemini, openAi);
    }

    private AppSettings BuildSettings(bool completed)
    {
        var local = LocalChoice.IsChecked == true;

        return _runtime.Settings with
        {
            OnboardingCompleted = completed || _runtime.Settings.OnboardingCompleted,

            // Records which welcome was seen, not merely that one was. Raising
            // the current version later brings this user back through it once,
            // which is what stops an existing install being left believing
            // whatever the old wizard told them.
            OnboardingVersion = completed
                ? OnboardingVersions.Current
                : _runtime.Settings.OnboardingVersion,
            ThemePreference = _themePreference,
            CompanionColor = _companionColour,
            CompanionSize = (int)CompanionSizeSlider.Value,
            CaptureActiveWindow = CaptureScreenCheck.IsChecked == true,
            ContextShortcutsEnabled = ContextShortcutsCheck.IsChecked == true,
            SpeechEnabled = SpeechEnabledCheck.IsChecked == true,
            StartWithWindows = StartWithWindowsCheck.IsChecked == true,
            PreferredMicrophoneId = MicrophoneBox.SelectedValue?.ToString(),

            AiProvider = local ? "Ollama" : SelectedProvider(),
            OllamaEndpoint = OllamaEndpointBox.Text.Trim(),
            OllamaModel = OllamaModelBox.Text.Trim(),

            // Choosing the local brain implies the local speech stack; leaving
            // it on a cloud transcriber would quietly defeat the point.
            SpeechToTextProvider = local ? "Whisper.cpp" : _runtime.Settings.SpeechToTextProvider,

            // The Windows voice rather than Piper. Both are offline, but Piper
            // is a separate executable and a voice model the installer does not
            // carry, so steering someone to it on a fresh install handed them a
            // voice that could never speak. Windows ships with the operating
            // system and is there on the first run.
            TextToSpeechProvider = local ? "Windows" : _runtime.Settings.TextToSpeechProvider
        };
    }
}
