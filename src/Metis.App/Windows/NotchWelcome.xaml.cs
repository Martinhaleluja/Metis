using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Metis.App.Runtime;
using Metis.Core.Models;
using Metis.Core.Services;

using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;

namespace Metis.App.Windows;

/// <summary>
/// First run, inside the notch.
///
/// What this replaces was four surfaces and eight steps. A new user signed in
/// through the notch, was handed a 980x700 settings window, then a notch
/// welcome page, then an 880x640 wizard — and was asked for their AI provider
/// and API key twice, in two different places, before Metis had answered a
/// single question. The wizard also asked for a companion colour, a companion
/// size, a microphone, keyboard shortcuts and a theme, every one of which has a
/// working default and none of which belongs between somebody and the first
/// thing they came for.
///
/// So: four steps, nothing asked twice, and nothing asked at all that has a
/// sensible answer already. It ends on a question the user can tap rather than
/// on a congratulation, because "You're set" is the exact moment a first-time
/// user closes everything and never comes back.
///
/// The steps are deliberately in this order. Where Metis is, then what it will
/// and will not do, then how it answers, then a real answer. Consent to read
/// the screen is explained on the second step and asked nowhere else.
/// </summary>
public partial class NotchWelcome : UserControl
{
    private MetisRuntime? _runtime;
    private Action<string>? _askQuestion;
    private Action? _openSettings;
    private int _index;
    private string _provider = ManagedProvider;

    /// <summary>Metis's own AI, reached through the gateway.</summary>
    private const string ManagedProvider = "Metis";

    /// <summary>A model running on this computer, through Ollama.</summary>
    private const string LocalProvider = "Ollama";

    /// <summary>The last step's index. Four steps, zero-based.</summary>
    private const int LastStep = 3;

    public NotchWelcome()
    {
        InitializeComponent();
        StarterChips.ItemsSource = Starters;
        ShowStep(0);
    }

    /// <summary>Raised when the welcome is over and the chat should take the notch.</summary>
    public event EventHandler? Finished;

    /// <summary>Raised whenever the panel's height changes, so the notch can fit it.</summary>
    public event EventHandler? ContentSizeChanged;

    /// <summary>
    /// The questions offered on the last step.
    ///
    /// All three are answerable immediately, need nothing installed, and
    /// demonstrate a different thing Metis does: reading the screen, explaining
    /// a task, and being asked about itself. Long enough to read as real
    /// questions rather than as buttons.
    /// </summary>
    private static readonly string[] Starters =
    [
        "What is on my screen right now?",
        "Show me how to crop a picture.",
        "What can you help me with?"
    ];

    public void Attach(MetisRuntime runtime, Action<string> askQuestion, Action openSettings)
    {
        _runtime = runtime;
        _askQuestion = askQuestion;
        _openSettings = openSettings;
        // Metis's own AI, preselected, unless this machine is already pointed
        // at a local model — which nobody sets by accident.
        //
        // The card used to start on whatever AiProvider happened to say, which
        // on a fresh install is "Gemini": an own-key provider with no key
        // behind it. So a new user arrived at the one decision first run asks
        // them to make with nothing ticked, no indication of what the default
        // was, and a preselected answer that would have refused their first
        // question. Preselecting the choice that needs nothing configured is
        // the whole reason this step can be skipped past.
        _provider = runtime.Settings.AiProvider.Equals(
            LocalProvider, StringComparison.OrdinalIgnoreCase)
            ? LocalProvider
            : ManagedProvider;

        RefreshChoices();
    }

    public double MeasureDesiredHeight(double width)
    {
        // Invalidated first for the same reason the settings panel does it: WPF
        // caches a measure against its constraint, and these four steps differ
        // in height by a couple of hundred pixels at the same width.
        InvalidateMeasure();
        Measure(new System.Windows.Size(Math.Max(width, 1), double.PositiveInfinity));
        return DesiredSize.Height;
    }

    // ============================== Steps ==============================

    private void ShowStep(int index)
    {
        _index = Math.Clamp(index, 0, LastStep);

        ArrivalStep.Visibility = Show(_index == 0);
        PromiseStep.Visibility = Show(_index == 1);
        AnswerStep.Visibility = Show(_index == 2);
        ReadyStep.Visibility = Show(_index == 3);

        BackButton.Visibility = Show(_index > 0);
        NextButton.Content = _index == LastStep ? "Start using Metis" : "Continue";

        RefreshDots();

        if (_index == 0)
        {
            PulseArrivalHalo();
        }

        UpdateLayout();
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Visibility Show(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private void Back_OnClick(object sender, RoutedEventArgs e) => ShowStep(_index - 1);

    private async void Next_OnClick(object sender, RoutedEventArgs e)
    {
        if (_index < LastStep)
        {
            ShowStep(_index + 1);
            return;
        }

        await FinishAsync();
    }

    /// <summary>
    /// Records that the welcome has been seen, and hands the notch to the chat.
    ///
    /// The save is allowed to fail without stopping anything. Being shown the
    /// welcome a second time is a small annoyance; being unable to leave it
    /// because a settings file would not write is not.
    /// </summary>
    private async System.Threading.Tasks.Task FinishAsync()
    {
        if (_runtime is not null)
        {
            try
            {
                await _runtime.SaveSettingsAsync(
                    _runtime.Settings with
                    {
                        AiProvider = _provider,
                        OnboardingCompleted = true,
                        OnboardingVersion = OnboardingVersions.Current
                    },
                    newGeminiApiKey: null,
                    newOpenAiApiKey: null);
            }
            catch (Exception exception)
            {
                _runtime.Log.Error("The welcome could not be saved.", exception);
            }
        }

        Finished?.Invoke(this, EventArgs.Empty);
    }

    // ============================== Choices ==============================

    private void Managed_OnClick(object sender, RoutedEventArgs e) => Choose(ManagedProvider);

    private void OwnKey_OnClick(object sender, RoutedEventArgs e) => ChooseOwnKey();

    private void Local_OnClick(object sender, RoutedEventArgs e) => Choose(LocalProvider);

    /// <summary>
    /// Own keys are Pro's. Somebody on Free or Plus who taps this card is told
    /// so and left on their current choice, rather than being allowed to pick
    /// something that will refuse them the moment they ask a question.
    /// </summary>
    private void ChooseOwnKey()
    {
        if (_runtime is not null && !_runtime.Can(MetisFeature.CustomAiProvider))
        {
            _openSettings?.Invoke();
            return;
        }

        Choose("Gemini");
        _openSettings?.Invoke();
    }

    private void Choose(string provider)
    {
        _provider = provider;
        RefreshChoices();
    }

    /// <summary>
    /// Draws which card is chosen, and whether the Pro card is available.
    ///
    /// The tick is the only thing that moves between cards. An earlier version
    /// changed the border, the background and the tick together, which read as
    /// three things happening for one decision.
    /// </summary>
    private void RefreshChoices()
    {
        var managed = _provider.Equals(ManagedProvider, StringComparison.OrdinalIgnoreCase);
        var local = _provider.Equals(LocalProvider, StringComparison.OrdinalIgnoreCase);

        ManagedTick.Visibility = Show(managed);
        LocalTick.Visibility = Show(local);

        ManagedChoice.BorderBrush = Chrome(managed);
        LocalChoice.BorderBrush = Chrome(local);
        OwnKeyChoice.BorderBrush = Chrome(!managed && !local);

        var entitled = _runtime?.Can(MetisFeature.CustomAiProvider) ?? false;
        OwnKeyBadge.Visibility = Show(!entitled);
        OwnKeyChoice.Opacity = entitled ? 1 : 0.72;

        // Which plan, asked rather than typed here. This card said "Part of
        // Metis Pro" long after bringing your own key had moved to Max, on the
        // first screen a new user ever sees.
        OwnKeyBody.Text = entitled
            ? "Bring a key from Google, OpenAI, Anthropic or OpenRouter and pay them directly."
            : $"Part of Metis {PlanCatalogue.NameOfPlanWith(MetisFeature.CustomAiProvider)}. "
              + "Bring a key from Google, OpenAI, Anthropic or OpenRouter and pay them directly.";
    }

    private Brush Chrome(bool selected) =>
        (TryFindResource(selected ? "AccentBrush" : "NotchScrollThumbBrush") as Brush)
        ?? System.Windows.Media.Brushes.Gray;

    // ============================== Starters ==============================

    private void Starter_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string question })
        {
            Ask(question);
        }
    }

    private async void Ask(string question)
    {
        await FinishAsync();
        _askQuestion?.Invoke(question);
    }

    // ============================== Chrome ==============================

    private void RefreshDots()
    {
        var dots = new[] { Dot0, Dot1, Dot2, Dot3 };
        for (var step = 0; step < dots.Length; step++)
        {
            dots[step].Background = Chrome(step == _index);
        }

        // The one thing here worth saying out loud. The dots themselves are
        // decoration and are not in the automation tree at all.
        System.Windows.Automation.AutomationProperties.SetName(
            StepDots, $"Step {_index + 1} of {LastStep + 1}");
    }

    /// <summary>
    /// The halo behind the notch shape, once.
    ///
    /// One element moving, one time. The installed motion guidance is explicit
    /// that a repeating animation is for loading and nothing else, and a
    /// permanently throbbing shape on the first screen somebody ever sees would
    /// be the thing they remember instead of the sentence beneath it. Skipped
    /// entirely when motion is reduced, where the shape simply sits there.
    /// </summary>
    private void PulseArrivalHalo()
    {
        ArrivalHalo.BeginAnimation(OpacityProperty, null);
        ArrivalHalo.Opacity = 0;

        if (MotionTuning.Reduced)
        {
            return;
        }

        var swell = new DoubleAnimation(0, 0.34, TimeSpan.FromMilliseconds(620))
        {
            AutoReverse = true,
            BeginTime = TimeSpan.FromMilliseconds(260),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        ArrivalHalo.BeginAnimation(OpacityProperty, swell);
    }
}
