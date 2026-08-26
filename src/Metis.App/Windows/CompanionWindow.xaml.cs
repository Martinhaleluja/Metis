using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Metis.App.Runtime;
using Metis.Core.Models;
using Metis.Core.Services;
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

    /// <summary>
    /// Clears the bubble once the user has had time to read it, and returns the
    /// companion to its own colour after an error. Both are transient signals:
    /// leaving either up turns a moment's news into permanent clutter sitting
    /// over the user's work.
    /// </summary>
    private static readonly TimeSpan TransientDisplay = TimeSpan.FromSeconds(6.5);

    /// <summary>
    /// The blur the companion's glow rests at. The flight scales up from here
    /// and returns to it on landing.
    /// </summary>
    private const double RestingGlowBlurRadius = 12d;
    private const double GlowGrowthDuringFlight = 40d;

    /// <summary>
    /// How far the user has to move the pointer during the return flight to
    /// take the companion back. Set well above the drift of a resting hand, so
    /// only a deliberate movement counts.
    /// </summary>
    private const double CursorReclaimDistance = 100d;

    private readonly DispatcherTimer _bubbleHideTimer;
    private readonly DispatcherTimer _errorResetTimer;
    private readonly DoubleAnimation _spinnerAnimation;
    private string _pendingSpeech = string.Empty;
    private List<int> _wordEnds = [];
    private int _revealedWords;
    private double _smoothLeft;
    private double _smoothTop;
    private double _displayedAudioLevel;
    private int _guidanceScreenX;
    private int _guidanceScreenY;

    /// <summary>
    /// Whether a guidance point has ever been given this session.
    ///
    /// The two coordinates above start life as zero, and zero is a real place —
    /// the top-left corner of the primary screen. Holding beside a target that
    /// was never set therefore parked the companion in that corner and kept it
    /// there, which looked like it wandering off mid-explanation.
    /// </summary>
    private bool _hasGuidancePoint;

    /// <summary>
    /// What the companion is doing with its position right now. Every rule
    /// about where it should be reads from this, so the cases cannot silently
    /// overlap the way a set of independent booleans eventually does.
    /// </summary>
    private CompanionMotion _motion = CompanionMotion.FollowingCursor;

    private CompanionFlight? _flight;
    private TimeSpan _flightElapsed;

    /// <summary>
    /// How long to stay beside the control once the companion has actually
    /// arrived. The countdown deliberately does not start when the guidance is
    /// requested: a long flight would otherwise eat most of a short hold and
    /// the companion would turn round almost as soon as it landed.
    /// </summary>
    private TimeSpan _guidanceHold = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The cue to show on arrival. The bubble is held back until the companion
    /// lands, because a label travelling across the screen is noise — it only
    /// means anything once it is sitting beside the thing it names.
    /// </summary>
    private string? _pendingGuidanceCue;

    /// <summary>
    /// Where the user's pointer was when the return flight began, so a real
    /// move can be told from the jitter of a hand resting on a mouse.
    /// </summary>
    private System.Windows.Point _cursorAtReturnStart;

    /// <summary>
    /// Set while a lesson runs. It keeps the companion off the cursor for the
    /// whole lesson rather than only for the few seconds each pointing cue
    /// lasts, so it stays beside the control the learner is working on.
    /// </summary>
    private bool _teachingDetached;

    /// <summary>
    /// Set while the ghost cursor is performing a movement. It outranks every
    /// other positioning rule, so nothing can pull the companion off its path
    /// mid-demonstration.
    /// </summary>
    private bool _demoActive;
    private bool _positionInitialized;
    private bool _allowClose;
    private CompanionColorOption _colour = CompanionPalette.Resolve(null);

    public CompanionWindow(MetisRuntime runtime)
    {
        InitializeComponent();
        _runtime = runtime;
        ApplySettings(runtime.Settings);
        runtime.SettingsChanged += (_, settings) => Dispatcher.InvokeAsync(() => ApplySettings(settings));
        runtime.State.Changed += RuntimeState_OnChanged;
        runtime.AudioLevelChanged += Runtime_OnAudioLevelChanged;
        runtime.CompanionResponseStarted += Runtime_OnCompanionResponseStarted;
        runtime.CompanionGuidanceRequested += Runtime_OnCompanionGuidanceRequested;
        runtime.CompanionDemoRequested += (_, demo) => Dispatcher.InvokeAsync(() => _ = PlayDemoAsync(demo));

        _spinnerAnimation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.75))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        _followTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _followTimer.Tick += (_, _) =>
        {
            try
            {
                Tick();
            }
            catch
            {
                // Protect render/motion tick loop from unhandled crashes
            }
        };
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

        _bubbleHideTimer = new DispatcherTimer { Interval = TransientDisplay };
        _bubbleHideTimer.Tick += (_, _) =>
        {
            try
            {
                _bubbleHideTimer.Stop();
                HideSpeech();
            }
            catch
            {
                // Protect speech hide timer
            }
        };

        _errorResetTimer = new DispatcherTimer { Interval = TransientDisplay };
        _errorResetTimer.Tick += (_, _) =>
        {
            try
            {
                _errorResetTimer.Stop();
                RestoreRestingAppearance();
            }
            catch
            {
                // Protect error reset timer
            }
        };

        // Subscribed after the timers exist, because ending a lesson stops the
        // guidance hold and sends the companion home — both of which reach for
        // state this constructor has only just finished building.
        runtime.CompanionDetachRequested += (_, detached) => Dispatcher.InvokeAsync(() =>
        {
            _teachingDetached = detached;
            if (!detached)
            {
                // The lesson is over, so the companion flies back to the user's
                // cursor rather than being left stranded at the last control.
                _guidanceTimer.Stop();
                HideSpeech();
                if (_motion != CompanionMotion.FollowingCursor)
                {
                    BeginReturnFlight();
                }
            }
        });

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
        _bubbleHideTimer.Stop();
        _errorResetTimer.Stop();
        _guidanceTimer.Stop();
        _runtime.State.Changed -= RuntimeState_OnChanged;
        _runtime.AudioLevelChanged -= Runtime_OnAudioLevelChanged;
        _runtime.CompanionResponseStarted -= Runtime_OnCompanionResponseStarted;
        _runtime.CompanionGuidanceRequested -= Runtime_OnCompanionGuidanceRequested;
        Close();
    }

    private void ApplySettings(AppSettings settings)
    {
        _colour = CompanionPalette.Resolve(settings.CompanionColor);
        ApplyCompanionColour();
        ApplyCompanionShape(CompanionShapes.Resolve(settings.CompanionShape));
        CompanionHost.Width = settings.CompanionSize;
        CompanionHost.Height = settings.CompanionSize;
    }

    /// <summary>
    /// Swaps the silhouette. Only the geometry, its outline and where the
    /// status indicators sit change here: the fill brush, glow, transforms and
    /// every animation are bound to the same element whichever form is chosen,
    /// so a new form arrives already able to speak, think, flash an error and
    /// fly across the screen.
    /// </summary>
    private void ApplyCompanionShape(CompanionShapeOption shape)
    {
        CompanionShape.Data = Geometry.Parse(shape.Geometry);

        // The outline stays a fixed dark ink while the fill animates with
        // state, so the form keeps its edge on a pale window without the
        // outline itself becoming another thing that means something.
        CompanionShape.Stroke = shape.Outlined ? OutlineBrush : null;
        CompanionShape.StrokeThickness = shape.Outlined ? 3.2 : 0;
        CompanionShape.StrokeLineJoin = PenLineJoin.Round;

        IndicatorShift.X = shape.IndicatorX;
        IndicatorShift.Y = shape.IndicatorY;
        IndicatorScale.ScaleX = shape.IndicatorScale;
        IndicatorScale.ScaleY = shape.IndicatorScale;
    }

    /// <summary>
    /// Frozen because it never changes and is shared by every outlined form.
    /// </summary>
    private static readonly SolidColorBrush OutlineBrush = CreateOutlineBrush();

    private static SolidColorBrush CreateOutlineBrush()
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(0x0B, 0x11, 0x18));
        brush.Freeze();
        return brush;
    }

    private void RuntimeState_OnChanged(object? sender, AssistantState state) => Dispatcher.InvokeAsync(() =>
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
        Dispatcher.InvokeAsync(() =>
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
        Dispatcher.InvokeAsync(() =>
        {
            _guidanceScreenX = guidance.ScreenX;
            _guidanceScreenY = guidance.ScreenY;
            _hasGuidancePoint = true;
            _guidanceHold = guidance.HoldDuration > TimeSpan.Zero
                ? guidance.HoldDuration
                : TimeSpan.FromSeconds(5);
            _pendingGuidanceCue = string.IsNullOrWhiteSpace(guidance.Cue) ? null : guidance.Cue;

            // The hold is started on arrival instead, so it measures time spent
            // beside the control rather than time spent getting there.
            _guidanceTimer.Stop();
            HideSpeech();

            var (targetLeft, targetTop) = ResolveGuidanceOrigin();
            _motion = CompanionMotion.FlyingToTarget;
            BeginFlightTo(targetLeft, targetTop);
        });

    /// <summary>
    /// The hold beside the control has run out. Unless a lesson is in progress,
    /// the companion flies home rather than being left stranded at the last
    /// thing it pointed at.
    /// </summary>
    private void GuidanceTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            _guidanceTimer.Stop();
            HideSpeech();

            if (_teachingDetached)
            {
                // Mid-lesson the companion stays beside the control the learner is
                // working on. Flying home between steps would pull their eye back
                // to the pointer and away from the thing they are meant to be
                // looking at.
                return;
            }

            BeginReturnFlight();
        }
        catch
        {
            // Protect timer handler
        }
    }

    private void BeginReturnFlight()
    {
        _cursorAtReturnStart = CurrentCursorPoint();
        var (cursorLeft, cursorTop) = ResolveCursorOrigin();
        _motion = CompanionMotion.ReturningToCursor;
        BeginFlightTo(cursorLeft, cursorTop);
    }

    private void BeginFlightTo(double targetLeft, double targetTop)
    {
        if (!_positionInitialized)
        {
            // Nothing has placed the companion yet, so there is no journey to
            // make — arriving instantly beats swooping in from the origin.
            _smoothLeft = targetLeft;
            _smoothTop = targetTop;
            _positionInitialized = true;
            Left = targetLeft;
            Top = targetTop;
            ArriveFromFlight();
            return;
        }

        _flightElapsed = TimeSpan.Zero;
        _flight = CompanionFlight.Create(_smoothLeft, _smoothTop, targetLeft, targetTop);
    }

    /// <summary>
    /// Moves the companion one frame along its flight, and decides whether the
    /// journey is over.
    /// </summary>
    private void AdvanceFlight()
    {
        if (_flight is null)
        {
            EndFlight();
            return;
        }

        // Only the trip home may be abandoned. A forward flight is the answer
        // to a question the user asked, so a nudge of the mouse must not strand
        // the companion halfway to the control it was sent to find.
        if (_motion == CompanionMotion.ReturningToCursor && HasUserTakenBackTheCursor())
        {
            EndFlight();
            return;
        }

        _flightElapsed += _followTimer.Interval;
        var frame = _flight.FrameAt(_flightElapsed);

        _smoothLeft = frame.X;
        _smoothTop = frame.Y;
        Left = frame.X;
        Top = frame.Y;

        FlightScale.ScaleX = frame.Scale;
        FlightScale.ScaleY = frame.Scale;
        FlightLean.Angle = frame.LeanDegrees;

        // The glow swells with the companion, so the whole shape reads as one
        // thing gathering speed rather than a sprite being scaled.
        CompanionGlow.BlurRadius = RestingGlowBlurRadius +
                                   ((frame.Scale - 1d) * GlowGrowthDuringFlight);

        if (_flight.IsComplete(_flightElapsed))
        {
            ArriveFromFlight();
        }
    }

    /// <summary>
    /// True when the user has moved the pointer far enough during the return
    /// flight to mean they want it back. The threshold is well above the drift
    /// of a hand resting on a mouse.
    /// </summary>
    private bool HasUserTakenBackTheCursor() =>
        (CurrentCursorPoint() - _cursorAtReturnStart).Length > CursorReclaimDistance;

    private void ArriveFromFlight()
    {
        var wasOutbound = _motion == CompanionMotion.FlyingToTarget;
        _flight = null;
        _flightElapsed = TimeSpan.Zero;
        RestoreFlightPosture();

        if (!wasOutbound)
        {
            EndFlight();
            return;
        }

        _motion = CompanionMotion.PointingAtTarget;

        // The label lands with the companion rather than travelling with it.
        if (_pendingGuidanceCue is { } cue)
        {
            BeginSpeech(cue, null);
            _pendingGuidanceCue = null;
        }

        _guidanceTimer.Stop();
        _guidanceTimer.Interval = _guidanceHold;
        _guidanceTimer.Start();
    }

    private void EndFlight()
    {
        _flight = null;
        _flightElapsed = TimeSpan.Zero;
        _pendingGuidanceCue = null;
        _motion = CompanionMotion.FollowingCursor;
        RestoreFlightPosture();
        RootPanel.FlowDirection = System.Windows.FlowDirection.LeftToRight;
    }

    private void RestoreFlightPosture()
    {
        FlightScale.ScaleX = 1;
        FlightScale.ScaleY = 1;
        FlightLean.Angle = 0;
        CompanionGlow.BlurRadius = RestingGlowBlurRadius;
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

            // An error colour says "that just failed", which stops being true
            // shortly afterwards. Metis keeps the failure in the log and the
            // chat; the companion goes back to being itself.
            _errorResetTimer.Stop();
            _errorResetTimer.Start();
        }
        else if (state == AssistantState.Success)
        {
            StartSuccessAnimation();
        }

        if (state is not (AssistantState.Error
            or AssistantState.NetworkError
            or AssistantState.AuthenticationError
            or AssistantState.QuotaError
            or AssistantState.AutomationError))
        {
            _errorResetTimer.Stop();
        }
    }

    /// <summary>
    /// Returns the companion to the colour the user chose, whatever state the
    /// runtime is still reporting. This is a display decision: the error itself
    /// is not forgotten, it just stops being shouted about.
    /// </summary>
    private void RestoreRestingAppearance()
    {
        StopSpeakingAnimation();
        CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, null);

        var fill = ParseColour(_colour.Fill);
        var glow = ParseColour(_colour.Glow);
        CompanionFill.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(fill, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            });
        CompanionGlow.Color = glow;
        CompanionShape.Opacity = 1;
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

    /// <summary>
    /// Paints the resting companion and its bubble in the user's chosen colour.
    /// The bubble uses the glow, which is the saturated member of the pair, and
    /// its text flips to dark on light colours so every option stays readable.
    /// </summary>
    private void ApplyCompanionColour()
    {
        var fill = ParseColour(_colour.Fill);
        var glow = ParseColour(_colour.Glow);

        if (_runtime.State.Current is AssistantState.Idle or AssistantState.Success)
        {
            CompanionFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
            CompanionFill.Color = fill;
        }

        CompanionGlow.Color = glow;
    }

    private static MediaColor ParseColour(string hex) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    /// <summary>
    /// The caller decides how much belongs in the bar; this is the runaway
    /// guard for anything reaching the companion by another route, such as a
    /// lesson line. A written answer may be a paragraph, so the guard sits well
    /// above the shorter limit used for spoken remarks.
    /// </summary>
    private const int InlineAnswerLimit = CompanionSpeech.MaxWrittenLength;

    private void BeginSpeech(string text, TimeSpan? speechDuration)
    {
        var shownText = text;
        var truncated = false;
        if (text.Length > InlineAnswerLimit)
        {
            shownText = Summarise(text, InlineAnswerLimit);
            truncated = true;
        }

        // A new reply cancels the previous one's countdown, so the fresh text
        // is never cleared by a timer belonging to the answer before it.
        _bubbleHideTimer.Stop();
        MoreInChatHint.Visibility = truncated ? Visibility.Visible : Visibility.Collapsed;
        _pendingSpeech = shownText;
        _revealedWords = 0;
        _wordEnds = FindWordEnds(shownText);
        SpeechText.Text = string.Empty;
        SpeechBubble.Visibility = Visibility.Visible;
        var grow = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);

        // Paced per word rather than per character: words arriving together is
        // how speech lands, and it stays readable while it fills in.
        var perWord = speechDuration is { TotalMilliseconds: > 0 } duration && _wordEnds.Count > 0
            ? duration.TotalMilliseconds / _wordEnds.Count
            : 240d;
        _bubbleTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(perWord, 90d, 520d));
        _bubbleTimer.Start();
    }

    /// <summary>
    /// The end offset of each word, so the reveal can advance a whole word at a
    /// time without re-splitting the string on every tick.
    /// </summary>
    private static List<int> FindWordEnds(string text)
    {
        var ends = new List<int>();
        var inWord = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                if (inWord)
                {
                    ends.Add(index);
                    inWord = false;
                }
            }
            else
            {
                inWord = true;
            }
        }

        if (inWord)
        {
            ends.Add(text.Length);
        }

        return ends;
    }

    private void BubbleTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_revealedWords >= _wordEnds.Count)
            {
                _bubbleTimer.Stop();

                // The countdown starts when the last word lands, not when writing
                // began, so a long answer still gets its full reading time.
                _bubbleHideTimer.Stop();
                _bubbleHideTimer.Start();
                return;
            }

            if (_revealedWords < _wordEnds.Count && _wordEnds[_revealedWords] <= _pendingSpeech.Length)
            {
                SpeechText.Text = _pendingSpeech[.._wordEnds[_revealedWords]];
                _revealedWords++;
            }
            else
            {
                _bubbleTimer.Stop();
            }
        }
        catch
        {
            _bubbleTimer.Stop();
        }
    }

    /// <summary>
    /// Cuts a long answer at a sentence end where possible, and at a word
    /// otherwise, so the companion never shows half a word.
    /// </summary>
    private static string Summarise(string text, int limit)
    {
        var window = text[..Math.Min(text.Length, limit)];
        var lastStop = window.LastIndexOfAny(['.', '!', '?']);
        if (lastStop > limit / 2)
        {
            return window[..(lastStop + 1)];
        }

        var lastSpace = window.LastIndexOf(' ');
        return (lastSpace > limit / 2 ? window[..lastSpace] : window).TrimEnd(' ', ',', ';', ':') + "…";
    }

    private void HideSpeech()
    {
        _bubbleTimer.Stop();
        _bubbleHideTimer.Stop();
        SpeechBubble.Visibility = Visibility.Collapsed;
        MoreInChatHint.Visibility = Visibility.Collapsed;
        SpeechText.Text = string.Empty;
    }

    /// <summary>
    /// Walks the companion along a path as though a second person were driving
    /// the mouse, then returns it to the user's cursor. This is how a gesture
    /// gets shown: a drag or a menu traversal cannot be explained by pointing
    /// at one place, it has to be performed.
    ///
    /// The companion detaches for the whole demonstration and reattaches only
    /// once it has finished, which is what stops it snapping back to the
    /// pointer halfway through and losing the learner.
    /// </summary>
    public async Task PlayDemoAsync(CompanionDemo demo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(demo);
        if (!demo.IsUsable)
        {
            return;
        }

        _demoActive = true;
        _guidanceTimer.Stop();
        try
        {
            if (!string.IsNullOrWhiteSpace(demo.Label))
            {
                BeginSpeech(demo.Label!, TimeSpan.FromMilliseconds(900));
            }

            // Start at the first point rather than gliding in from wherever the
            // cursor happens to be, so the gesture reads from its true origin.
            MoveToScreenPoint(demo.Path[0], instant: true);
            await Task.Delay(TimeSpan.FromMilliseconds(320), cancellationToken);

            for (var index = 1; index < demo.Path.Count; index++)
            {
                await GlideToAsync(demo.Path[index], cancellationToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(demo.HoldAtEnd ? 900 : 380), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A cancelled demonstration still has to hand the companion back.
        }
        finally
        {
            _demoActive = false;
            if (!_teachingDetached)
            {
                _motion = CompanionMotion.FollowingCursor;
            }
        }
    }

    /// <summary>
    /// Walks the companion along one leg of a demonstration.
    ///
    /// Unlike a flight, this follows the literal straight line between the two
    /// points and never arcs. The learner is being shown a gesture they are
    /// about to copy, so an invented curve would teach a movement their own
    /// hand should not make. What it does borrow from a flight is the pacing:
    /// the time taken scales with the distance covered, instead of a fixed
    /// frame count that raced through a long drag and crawled across a short
    /// one.
    /// </summary>
    private async Task GlideToAsync(GuidancePoint target, CancellationToken cancellationToken)
    {
        const int frameMilliseconds = 16;
        var startLeft = _smoothLeft;
        var startTop = _smoothTop;
        var (endLeft, endTop) = ScreenPointToWindowOrigin(target);

        var distance = Math.Sqrt(
            Math.Pow(endLeft - startLeft, 2) + Math.Pow(endTop - startTop, 2));
        var duration = CompanionFlight.DurationForTracedMovement(distance);
        var steps = Math.Max(1, (int)Math.Round(duration.TotalMilliseconds / frameMilliseconds));

        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ease in and out so the movement looks deliberate, the way a hand
            // moves, rather than mechanical.
            var eased = CompanionFlight.Ease(step / (double)steps);
            _smoothLeft = startLeft + ((endLeft - startLeft) * eased);
            _smoothTop = startTop + ((endTop - startTop) * eased);
            Left = _smoothLeft;
            Top = _smoothTop;
            await Task.Delay(TimeSpan.FromMilliseconds(frameMilliseconds), cancellationToken);
        }
    }

    private void MoveToScreenPoint(GuidancePoint point, bool instant)
    {
        var (left, top) = ScreenPointToWindowOrigin(point);
        _smoothLeft = left;
        _smoothTop = top;
        if (instant)
        {
            Left = left;
            Top = top;
        }
    }

    /// <summary>
    /// Where the companion's window has to sit for its body to land on a point.
    /// Deliberately delegates to the same calculation the resting position
    /// uses: these were separate, and because only the resting one accounted
    /// for which side the speech bubble hangs off, the companion aimed at one
    /// place during the flight and settled at another.
    /// </summary>
    private (double Left, double Top) ScreenPointToWindowOrigin(GuidancePoint point)
    {
        var previousX = _guidanceScreenX;
        var previousY = _guidanceScreenY;

        _guidanceScreenX = point.ScreenX;
        _guidanceScreenY = point.ScreenY;
        try
        {
            return ResolveGuidanceOrigin();
        }
        finally
        {
            // This is a query, not a move: the guidance target belongs to
            // whatever set it, so it is restored rather than overwritten.
            _guidanceScreenX = previousX;
            _guidanceScreenY = previousY;
        }
    }

    /// <summary>
    /// One frame of companion movement. Every positioning rule hangs off the
    /// current motion, so the cases stay mutually exclusive instead of racing
    /// each other for the same two coordinates.
    /// </summary>
    private void Tick()
    {
        if (_demoActive)
        {
            // A demonstration owns the companion's position outright.
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        switch (_motion)
        {
            case CompanionMotion.FlyingToTarget:
            case CompanionMotion.ReturningToCursor:
                AdvanceFlight();
                break;

            case CompanionMotion.PointingAtTarget:
                HoldBesideTarget();
                break;

            default:
                if (_teachingDetached)
                {
                    // Teaching, but between marks: hold position rather than
                    // snapping back to the pointer, which would drag the
                    // learner's eye away from their work.
                    return;
                }

                FollowCursor();
                break;
        }
    }

    private void FollowCursor()
    {
        var (targetLeft, targetTop) = ResolveCursorOrigin();
        MoveTowards(targetLeft, targetTop, smoothing: 0.28);
    }

    /// <summary>
    /// Keeps the companion pinned beside the control while it is pointing.
    /// This is re-resolved every frame rather than fixed on arrival, so the
    /// companion stays correctly placed when the bubble grows into its label
    /// and changes the window's width underneath it.
    /// </summary>
    private void HoldBesideTarget()
    {
        if (!_hasGuidancePoint)
        {
            // Nothing has said where to point, so follow the cursor rather than
            // hold beside coordinates that only mean the top-left corner.
            FollowCursor();
            return;
        }

        var (targetLeft, targetTop) = ResolveGuidanceOrigin();
        MoveTowards(targetLeft, targetTop, smoothing: 0.46);
    }

    private void MoveTowards(double targetLeft, double targetTop, double smoothing)
    {
        if (!_positionInitialized)
        {
            _smoothLeft = targetLeft;
            _smoothTop = targetTop;
            _positionInitialized = true;
        }
        else
        {
            _smoothLeft += (targetLeft - _smoothLeft) * smoothing;
            _smoothTop += (targetTop - _smoothTop) * smoothing;
        }

        Left = _smoothLeft;
        Top = _smoothTop;
    }

    private System.Windows.Point CurrentCursorPoint()
    {
        var (pixelX, pixelY) = _runtime.Cursor.GetPosition();
        return DeviceToWindowUnits().Transform(new System.Windows.Point(pixelX, pixelY));
    }

    private Matrix DeviceToWindowUnits() =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

    /// <summary>
    /// Where the companion sits when it is trailing the pointer: offset from
    /// the cursor, flipped away from the right edge, and held inside the
    /// monitor.
    /// </summary>
    private (double Left, double Top) ResolveCursorOrigin()
    {
        var (cursorPixelX, cursorPixelY) = _runtime.Cursor.GetPosition();

        // The taskbar is deliberately excluded from a monitor's working area,
        // so every companion mode uses the full monitor bounds. The companion
        // is click-through and may safely occupy shell UI such as the taskbar.
        var area = ResolveMonitorArea(cursorPixelX, cursorPixelY);
        var cursor = DeviceToWindowUnits().Transform(
            new System.Windows.Point(cursorPixelX, cursorPixelY));

        var distance = _runtime.Settings.CursorDistance;
        var windowWidth = Math.Max(ActualWidth, _runtime.Settings.CompanionSize + 20);
        var windowHeight = Math.Max(ActualHeight, _runtime.Settings.CompanionSize + 20);

        RootPanel.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        var targetLeft = cursor.X + distance;
        var targetTop = cursor.Y + distance;

        if (targetLeft + windowWidth > area.Right)
        {
            targetLeft = cursor.X - distance - windowWidth;
        }

        if (targetTop + windowHeight > area.Bottom)
        {
            // Keep Metis inside the bottom edge instead of flipping above the
            // taskbar, which previously looked like an invisible barrier.
            targetTop = area.Bottom - windowHeight;
        }

        // Clamped with the same care as the guidance origin: a bubble wider
        // than the monitor used to collapse this range and pin the companion to
        // the top-left corner while it was merely following the cursor.
        return (
            ClampToRange(targetLeft, area.Left, area.Right - windowWidth),
            ClampToRange(targetTop, area.Top, area.Bottom - windowHeight));
    }

    /// <summary>
    /// Where the companion sits when it is pointing at something. It also
    /// decides which side of the companion the bubble hangs off, so a label
    /// near the right edge of a monitor opens inwards instead of off-screen.
    /// </summary>
    /// <summary>
    /// Clamps a value into a range, tolerating a range narrower than nothing by
    /// returning its middle. A collapsed range means the thing being placed is
    /// bigger than the space, and the honest answer then is the centre rather
    /// than whichever edge the comparison happened to reach first.
    /// </summary>
    private static double ClampToRange(double value, double min, double max) =>
        min >= max ? (min + max) / 2 : Math.Clamp(value, min, max);

    private (double Left, double Top) ResolveGuidanceOrigin()
    {
        var area = ResolveMonitorArea(_guidanceScreenX, _guidanceScreenY);
        var target = DeviceToWindowUnits().Transform(
            new System.Windows.Point(_guidanceScreenX, _guidanceScreenY));

        var shapeCenterOffset = 10 + (_runtime.Settings.CompanionSize / 2d);

        var placeBubbleOnLeft = target.X > area.Left + ((area.Right - area.Left) / 2d);
        RootPanel.FlowDirection = placeBubbleOnLeft
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;

        // The width has to be measured after the flow flip and after any bubble
        // text change, not before. Reading a stale ActualWidth here offsets the
        // companion by the difference between the old and the new bubble width,
        // which is what made it land wide of what it was pointing at.
        UpdateLayout();
        var windowWidth = Math.Max(ActualWidth, _runtime.Settings.CompanionSize + 20);

        var horizontalShapeCenter = placeBubbleOnLeft
            ? windowWidth - shapeCenterOffset
            : shapeCenterOffset;

        var targetLeft = target.X - horizontalShapeCenter;
        var targetTop = target.Y - shapeCenterOffset;

        // Keep the companion's own shape on screen — not the whole window.
        //
        // Clamping the window meant clamping the bubble with it, and a bubble is
        // as wide as the sentence in it. Once it was wider than the monitor the
        // permitted range collapsed to a single point and every guidance landed
        // in the top-left corner, whatever it was meant to be pointing at. The
        // bubble may hang past the edge; it already flips side to avoid that.
        // What must stay visible is the pointer itself.
        var reach = shapeCenterOffset;
        var shapeCentreX = ClampToRange(target.X, area.Left + reach, area.Right - reach);
        var shapeCentreY = ClampToRange(target.Y, area.Top + reach, area.Bottom - reach);

        return (shapeCentreX - horizontalShapeCenter, shapeCentreY - shapeCenterOffset);
    }

    private (double Left, double Top, double Right, double Bottom) ResolveMonitorArea(
        int pixelX,
        int pixelY)
    {
        var pixelArea = _runtime.Cursor.GetMonitorArea(pixelX, pixelY);
        var fromDevice = DeviceToWindowUnits();
        var topLeft = fromDevice.Transform(
            new System.Windows.Point(pixelArea.Left, pixelArea.Top));
        var bottomRight = fromDevice.Transform(
            new System.Windows.Point(pixelArea.Right, pixelArea.Bottom));
        return (topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
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
