using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Metis.App.Runtime;
using Metis.Core.Models;
using MediaColor = System.Windows.Media.Color;

namespace Metis.App.Windows;

public partial class CompanionWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private readonly MetisRuntime _runtime;
    private readonly DispatcherTimer _followTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _guidanceTimer;
    private readonly DoubleAnimation _spinnerAnimation;
    private string _pendingSpeech = string.Empty;
    private int _revealedCharacters;
    private double _smoothLeft;
    private double _smoothTop;
    private double _displayedAudioLevel;
    private int _guidanceScreenX;
    private int _guidanceScreenY;
    private bool _guidanceActive;
    private bool _positionInitialized;
    private bool _allowClose;

    public CompanionWindow(MetisRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        ApplySettings(runtime.Settings);
        runtime.SettingsChanged += (_, settings) => Dispatcher.Invoke(() => ApplySettings(settings));
        runtime.State.Changed += RuntimeState_OnChanged;
        runtime.AudioLevelChanged += Runtime_OnAudioLevelChanged;
        runtime.CompanionResponseStarted += Runtime_OnCompanionResponseStarted;
        runtime.CompanionGuidanceRequested += Runtime_OnCompanionGuidanceRequested;

        _spinnerAnimation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.75))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        _followTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _followTimer.Tick += (_, _) => FollowCursor();
        _followTimer.Start();

        _bubbleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(36)
        };
        _bubbleTimer.Tick += BubbleTimer_OnTick;

        _guidanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _guidanceTimer.Tick += GuidanceTimer_OnTick;

        SourceInitialized += (_, _) => MakeClickThrough();
        Closing += (_, args) =>
        {
            if (!_allowClose)
            {
                args.Cancel = true;
            }
        };
    }

    public void AllowClose()
    {
        _allowClose = true;
        _followTimer.Stop();
        _bubbleTimer.Stop();
        _guidanceTimer.Stop();
        _runtime.State.Changed -= RuntimeState_OnChanged;
        _runtime.AudioLevelChanged -= Runtime_OnAudioLevelChanged;
        _runtime.CompanionResponseStarted -= Runtime_OnCompanionResponseStarted;
        _runtime.CompanionGuidanceRequested -= Runtime_OnCompanionGuidanceRequested;
        Close();
    }

    private void ApplySettings(AppSettings settings)
    {
        CompanionHost.Width = settings.CompanionSize;
        CompanionHost.Height = settings.CompanionSize;
    }

    private void RuntimeState_OnChanged(object? sender, AssistantState state) => Dispatcher.Invoke(() =>
    {
        WavePanel.Visibility = state == AssistantState.Listening ? Visibility.Visible : Visibility.Collapsed;
        ThinkingRing.Visibility = state == AssistantState.Thinking ? Visibility.Visible : Visibility.Collapsed;

        if (state == AssistantState.Thinking)
        {
            ThinkingRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _spinnerAnimation);
            HideSpeech();
        }
        else
        {
            ThinkingRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }

        ApplyStateAppearance(state);
    });

    private void Runtime_OnAudioLevelChanged(object? sender, float level) => Dispatcher.BeginInvoke(() =>
    {
        var target = Math.Clamp(level, 0, 1);
        _displayedAudioLevel += (target - _displayedAudioLevel) * 0.38;
        var normalized = _displayedAudioLevel;
        Wave1.Height = 7 + normalized * 12;
        Wave2.Height = 9 + normalized * 20;
        Wave3.Height = 7 + normalized * 16;
        Wave4.Height = 10 + normalized * 22;
        Wave5.Height = 6 + normalized * 13;
    });

    private void Runtime_OnCompanionResponseStarted(object? sender, CompanionResponse response) =>
        Dispatcher.Invoke(() =>
        {
            if (response.ShowBubble && !string.IsNullOrWhiteSpace(response.Text))
            {
                BeginSpeech(response.Text, response.SpeechDuration);
            }
            else
            {
                HideSpeech();
            }
        });

    private void Runtime_OnCompanionGuidanceRequested(object? sender, CompanionGuidance guidance) =>
        Dispatcher.Invoke(() =>
        {
            _guidanceScreenX = guidance.ScreenX;
            _guidanceScreenY = guidance.ScreenY;
            _guidanceActive = true;
            _guidanceTimer.Stop();
            _guidanceTimer.Interval = guidance.HoldDuration > TimeSpan.Zero
                ? guidance.HoldDuration
                : TimeSpan.FromSeconds(5);
            _guidanceTimer.Start();

            if (!string.IsNullOrWhiteSpace(guidance.Cue))
            {
                BeginSpeech(guidance.Cue, null);
            }
            else
            {
                HideSpeech();
            }
        });

    private void GuidanceTimer_OnTick(object? sender, EventArgs e)
    {
        _guidanceTimer.Stop();
        _guidanceActive = false;
        RootPanel.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        HideSpeech();
    }

    private void ApplyStateAppearance(AssistantState state)
    {
        StopSpeakingAnimation();

        var (fill, glow, opacity) = state switch
        {
            AssistantState.Listening => (MediaColor.FromRgb(0x4F, 0xE1, 0xE8), MediaColor.FromRgb(0x39, 0xE5, 0xFF), 1d),
            AssistantState.Thinking => (MediaColor.FromRgb(0x7B, 0x73, 0xFF), MediaColor.FromRgb(0x70, 0x91, 0xFF), 1d),
            AssistantState.Speaking => (MediaColor.FromRgb(0x8E, 0xD8, 0xFF), Colors.White, 1d),
            AssistantState.Success => (MediaColor.FromRgb(0x55, 0xDC, 0x9A), MediaColor.FromRgb(0x5A, 0xF0, 0xAD), 1d),
            AssistantState.Error => (MediaColor.FromRgb(0xFF, 0x5F, 0x57), MediaColor.FromRgb(0xFF, 0x98, 0x4A), 1d),
            AssistantState.NetworkError => (MediaColor.FromRgb(0xFF, 0x8A, 0x3D), MediaColor.FromRgb(0xFF, 0xC1, 0x5A), 1d),
            AssistantState.AuthenticationError => (MediaColor.FromRgb(0xF0, 0x3E, 0x52), MediaColor.FromRgb(0xFF, 0x68, 0x72), 1d),
            AssistantState.QuotaError => (MediaColor.FromRgb(0xE3, 0x57, 0xD6), MediaColor.FromRgb(0xFF, 0x83, 0xE9), 1d),
            AssistantState.AutomationError => (MediaColor.FromRgb(0xFF, 0xB5, 0x2E), MediaColor.FromRgb(0xFF, 0xD7, 0x62), 1d),
            AssistantState.Paused => (MediaColor.FromRgb(0x7A, 0x87, 0x93), MediaColor.FromRgb(0x5E, 0x68, 0x72), 0.62d),
            _ => (MediaColor.FromRgb(0x8E, 0xD8, 0xFF), MediaColor.FromRgb(0x56, 0xCC, 0xFF), 1d)
        };

        AnimateCompanionColor(fill);
        CompanionGlow.Color = glow;
        CompanionShape.Opacity = opacity;

        if (state == AssistantState.Speaking)
        {
            StartSpeakingAnimation();
        }
        else if (state is AssistantState.Error
                 or AssistantState.NetworkError
                 or AssistantState.AuthenticationError
                 or AssistantState.QuotaError
                 or AssistantState.AutomationError)
        {
            StartErrorAnimation(fill, glow);
        }
        else if (state == AssistantState.Success)
        {
            StartSuccessAnimation();
        }
    }

    private void AnimateCompanionColor(MediaColor color)
    {
        var transition = new ColorAnimation(color, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, transition);
    }

    private void StartSpeakingAnimation()
    {
        var pulse = new DoubleAnimation(0.97, 1.055, TimeSpan.FromMilliseconds(210))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        CompanionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        CompanionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse.Clone());

        var shake = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(430),
            RepeatBehavior = RepeatBehavior.Forever
        };
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-0.8, KeyTime.FromPercent(0.15)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0.7, KeyTime.FromPercent(0.36)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-0.35, KeyTime.FromPercent(0.63)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, KeyTime.FromPercent(0.82)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        CompanionShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, shake);

        var tilt = new DoubleAnimation(-1.2, 1.2, TimeSpan.FromMilliseconds(320))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        CompanionTilt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, tilt);

        var breathe = new ColorAnimation(MediaColor.FromRgb(0xF4, 0xFB, 0xFF), TimeSpan.FromMilliseconds(280))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, breathe);
    }

    private void StartErrorAnimation(MediaColor fill, MediaColor glow)
    {
        CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
        CompanionFill.Color = fill;
        var errorPulse = new ColorAnimation(glow, TimeSpan.FromMilliseconds(190))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop
        };
        CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, errorPulse);

        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromPercent(0.15)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromPercent(0.32)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-1.3, KeyTime.FromPercent(0.52)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.72)));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        CompanionShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, shake);
    }

    private void StartSuccessAnimation()
    {
        var pop = new DoubleAnimation(1, 1.13, TimeSpan.FromMilliseconds(150))
        {
            AutoReverse = true,
            EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
        };
        CompanionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
        CompanionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop.Clone());
    }

    private void StopSpeakingAnimation()
    {
        CompanionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        CompanionScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        CompanionTilt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        CompanionShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        CompanionShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
        CompanionScale.ScaleX = 1;
        CompanionScale.ScaleY = 1;
        CompanionShift.X = 0;
        CompanionShift.Y = 0;
    }

    private void BeginSpeech(string text, TimeSpan? speechDuration)
    {
        _pendingSpeech = text;
        _revealedCharacters = 0;
        SpeechText.Text = string.Empty;
        SpeechBubble.Visibility = Visibility.Visible;
        var grow = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
        var pacedMilliseconds = speechDuration is { } duration && text.Length > 0
            ? duration.TotalMilliseconds / text.Length
            : 36d;
        _bubbleTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(pacedMilliseconds, 18d, 95d));
        _bubbleTimer.Start();
    }

    private void BubbleTimer_OnTick(object? sender, EventArgs e)
    {
        if (_revealedCharacters >= _pendingSpeech.Length)
        {
            _bubbleTimer.Stop();
            return;
        }

        _revealedCharacters = Math.Min(_pendingSpeech.Length, _revealedCharacters + 1);
        SpeechText.Text = _pendingSpeech[.._revealedCharacters];
    }

    private void HideSpeech()
    {
        _bubbleTimer.Stop();
        SpeechBubble.Visibility = Visibility.Collapsed;
        SpeechText.Text = string.Empty;
    }

    private void FollowCursor()
    {
        if (!IsVisible)
        {
            return;
        }

        var (cursorPixelX, cursorPixelY) = _runtime.Cursor.GetPosition();
        var anchorPixelX = _guidanceActive ? _guidanceScreenX : cursorPixelX;
        var anchorPixelY = _guidanceActive ? _guidanceScreenY : cursorPixelY;
        // The taskbar is deliberately excluded from a monitor's working area,
        // so every companion mode uses the full monitor bounds. The companion
        // is click-through and may safely occupy shell UI such as the taskbar.
        var pixelArea = _runtime.Cursor.GetMonitorArea(anchorPixelX, anchorPixelY);
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var cursor = fromDevice.Transform(new System.Windows.Point(cursorPixelX, cursorPixelY));
        var guidanceTarget = fromDevice.Transform(new System.Windows.Point(anchorPixelX, anchorPixelY));
        var areaTopLeft = fromDevice.Transform(new System.Windows.Point(pixelArea.Left, pixelArea.Top));
        var areaBottomRight = fromDevice.Transform(new System.Windows.Point(pixelArea.Right, pixelArea.Bottom));
        var cursorX = cursor.X;
        var cursorY = cursor.Y;
        var area = (
            Left: areaTopLeft.X,
            Top: areaTopLeft.Y,
            Right: areaBottomRight.X,
            Bottom: areaBottomRight.Y);
        var distance = _runtime.Settings.CursorDistance;
        var windowWidth = Math.Max(ActualWidth, _runtime.Settings.CompanionSize + 20);
        var windowHeight = Math.Max(ActualHeight, _runtime.Settings.CompanionSize + 20);
        double targetLeft;
        double targetTop;

        if (_guidanceActive)
        {
            var shapeCenterOffset = 10 + (_runtime.Settings.CompanionSize / 2d);
            var placeBubbleOnLeft = guidanceTarget.X > area.Left + ((area.Right - area.Left) / 2d);
            RootPanel.FlowDirection = placeBubbleOnLeft
                ? System.Windows.FlowDirection.RightToLeft
                : System.Windows.FlowDirection.LeftToRight;
            var horizontalShapeCenter = placeBubbleOnLeft
                ? windowWidth - shapeCenterOffset
                : shapeCenterOffset;
            targetLeft = guidanceTarget.X - horizontalShapeCenter;
            targetTop = guidanceTarget.Y - shapeCenterOffset;
        }
        else
        {
            RootPanel.FlowDirection = System.Windows.FlowDirection.LeftToRight;
            targetLeft = cursorX + distance;
            targetTop = cursorY + distance;
        }

        if (!_guidanceActive && targetLeft + windowWidth > area.Right)
        {
            targetLeft = cursorX - distance - windowWidth;
        }

        if (!_guidanceActive && targetTop + windowHeight > area.Bottom)
        {
            // Keep Metis inside the bottom edge instead of flipping above the
            // taskbar, which previously looked like an invisible barrier.
            targetTop = area.Bottom - windowHeight;
        }

        if (!_guidanceActive)
        {
            targetLeft = Math.Clamp(targetLeft, area.Left, Math.Max(area.Left, area.Right - windowWidth));
            targetTop = Math.Clamp(targetTop, area.Top, Math.Max(area.Top, area.Bottom - windowHeight));
        }

        if (!_positionInitialized)
        {
            _smoothLeft = targetLeft;
            _smoothTop = targetTop;
            _positionInitialized = true;
        }
        else
        {
            var smoothing = _guidanceActive ? 0.46 : 0.28;
            _smoothLeft += (targetLeft - _smoothLeft) * smoothing;
            _smoothTop += (targetTop - _smoothTop) * smoothing;
        }

        Left = _smoothLeft;
        Top = _smoothTop;
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

}
