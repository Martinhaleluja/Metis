using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Metis.Core.Models;
using MediaColor = System.Windows.Media.Color;

namespace Metis.App.Windows;

/// <summary>
/// The root of Metis's interface: a notch pinned to the top edge of the primary
/// screen. It is present for as long as Metis runs, tucked to a sliver when
/// idle and expanded to narrate what Metis is doing otherwise. Pulling it down
/// or clicking it opens the chat, and every Metis window grows out of it and
/// collapses back into it, so the notch is the one fixed thing on screen.
/// </summary>
public partial class NotchWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;

    private const double ExpandedHeight = 34;
    private const double TuckedWidth = 104;
    private const double HorizontalPadding = 30;

    /// <summary>
    /// How far the tucked notch hides above the screen edge. A sliver stays
    /// visible so the user always knows Metis is there and has something to
    /// pull down.
    /// </summary>
    private const double TuckedHiddenAmount = 22;

    private static readonly Duration Shape = new(TimeSpan.FromMilliseconds(420));
    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(200));

    private readonly DispatcherTimer _retractTimer;
    private bool _allowClose;
    private bool _isShown;
    private bool _hovered;
    private System.Windows.Point _pressPoint;
    private bool _pressed;

    /// <summary>Raised when the user pulls the notch down or clicks it.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the user clicks the gear on the notch.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>The screen-space bottom edge the attached windows hang from.</summary>
    public double AnchorBottom => Top + ExpandedHeight;

    public NotchWindow()
    {
        InitializeComponent();

        _retractTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _retractTimer.Tick += (_, _) =>
        {
            _retractTimer.Stop();
            Tuck();
        };

        SourceInitialized += (_, _) =>
        {
            MakeToolWindow();
            PositionOverTopEdge();
        };

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
        _retractTimer.Stop();
        Close();
    }

    /// <summary>
    /// Applies one activity. Idle retracts the notch; everything else expands it
    /// to fit its text, and the terminal states retract themselves shortly after
    /// so the user is not left with a stale "Done" hanging over their screen.
    /// </summary>
    public void Show(MetisActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _retractTimer.Stop();

        if (activity.Kind == MetisActivityKind.Idle || string.IsNullOrWhiteSpace(activity.Text))
        {
            Tuck();
            return;
        }

        PositionOverTopEdge();
        ActivityText.Text = activity.Text;
        ApplyAccent(activity.Kind);
        BuildStepPips(activity);
        SetThinking(activity.Kind is MetisActivityKind.Thinking or MetisActivityKind.Capturing);
        Expand();

        if (activity.Kind is MetisActivityKind.Complete or MetisActivityKind.Stopped or MetisActivityKind.Error)
        {
            _retractTimer.Start();
        }
    }

    private void Expand()
    {
        Visibility = Visibility.Visible;

        // Measuring the content is what lets the notch grow to exactly fit its
        // text, the way the shape follows the message rather than the message
        // being squeezed into a fixed pill.
        NotchContent.Measure(new System.Windows.Size(double.PositiveInfinity, ExpandedHeight));
        var target = Math.Clamp(NotchContent.DesiredSize.Width + HorizontalPadding, 132, Width - 24);

        Animate(NotchBody, WidthProperty, target, Shape, new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut });
        Animate(NotchDrop, TranslateTransform.YProperty, 0, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(NotchContent, OpacityProperty, 1, Fade, null, beginAfter: _isShown ? 0 : 120);
        Animate(Grabber, OpacityProperty, 0, Fade, null);
        _isShown = true;
    }

    /// <summary>
    /// Returns the notch to its resting sliver. It never leaves the screen: a
    /// visible handle is what makes the notch something the user can reach for
    /// at any time, rather than a thing that only exists while Metis is busy.
    /// </summary>
    public void Tuck()
    {
        _retractTimer.Stop();
        _isShown = false;
        SetThinking(false);
        Visibility = Visibility.Visible;

        Animate(NotchContent, OpacityProperty, 0, Fade, null);
        Animate(Grabber, OpacityProperty, 1, Fade, null, beginAfter: 120);
        Animate(NotchBody, WidthProperty, TuckedWidth, Shape,
            new CubicEase { EasingMode = EasingMode.EaseInOut }, beginAfter: 100);
        Animate(NotchDrop, TranslateTransform.YProperty, _hovered ? -8 : -TuckedHiddenAmount, Shape,
            new CubicEase { EasingMode = EasingMode.EaseInOut }, beginAfter: 100);
    }

    private void Notch_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovered = true;
        Animate(HoverControls, OpacityProperty, 1, Fade, null);
        if (!_isShown)
        {
            // Peek further out on hover so the notch invites the pull.
            Animate(NotchBody, WidthProperty, 152, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });
            Animate(NotchDrop, TranslateTransform.YProperty, -8, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });
        }
    }

    private void Notch_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Animate(HoverControls, OpacityProperty, 0, Fade, null);
        if (!_isShown)
        {
            Tuck();
        }
    }

    private void Notch_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressed = true;
        _pressPoint = e.GetPosition(this);
    }

    /// <summary>
    /// A downward drag opens Metis, matching the pull-down gesture the shape
    /// suggests. The threshold keeps an ordinary click from being read as a
    /// drag and vice versa.
    /// </summary>
    private void Notch_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        if (e.GetPosition(this).Y - _pressPoint.Y > 14)
        {
            _pressed = false;
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Notch_OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressed = false;
        e.Handled = true;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void Animate(
        System.Windows.Media.Animation.IAnimatable target,
        DependencyProperty property,
        double to,
        Duration duration,
        IEasingFunction? easing,
        double beginAfter = 0)
    {
        var animation = new DoubleAnimation(to, duration)
        {
            EasingFunction = easing,
            BeginTime = TimeSpan.FromMilliseconds(beginAfter),
            FillBehavior = FillBehavior.HoldEnd
        };
        target.BeginAnimation(property, animation);
    }

    private void ApplyAccent(MetisActivityKind kind)
    {
        var colour = kind switch
        {
            MetisActivityKind.Listening => MediaColor.FromRgb(0x30, 0xD1, 0x58),
            MetisActivityKind.Capturing => MediaColor.FromRgb(0x5E, 0x5C, 0xE6),
            MetisActivityKind.Thinking => MediaColor.FromRgb(0x0A, 0x7C, 0xFF),
            MetisActivityKind.Acting => MediaColor.FromRgb(0x0A, 0x7C, 0xFF),
            MetisActivityKind.Verifying => MediaColor.FromRgb(0x5E, 0x5C, 0xE6),
            MetisActivityKind.Speaking => MediaColor.FromRgb(0x64, 0xD2, 0xFF),
            MetisActivityKind.Complete => MediaColor.FromRgb(0x30, 0xD1, 0x58),
            MetisActivityKind.Error => MediaColor.FromRgb(0xFF, 0x45, 0x3A),
            MetisActivityKind.Stopped => MediaColor.FromRgb(0xFF, 0x9F, 0x0A),
            _ => MediaColor.FromRgb(0x8E, 0x8E, 0x93)
        };

        StatusLight.Fill = new SolidColorBrush(colour);
        PulseHalo.Fill = new SolidColorBrush(colour);

        // A breathing halo while work is in flight, still when it is not, so
        // "busy" is legible without reading the words.
        var busy = kind is MetisActivityKind.Listening or MetisActivityKind.Capturing
            or MetisActivityKind.Thinking or MetisActivityKind.Acting
            or MetisActivityKind.Verifying or MetisActivityKind.Speaking;
        if (busy)
        {
            var pulse = new DoubleAnimation(0.12, 0.42, TimeSpan.FromMilliseconds(880))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            PulseHalo.BeginAnimation(OpacityProperty, pulse);
        }
        else
        {
            PulseHalo.BeginAnimation(OpacityProperty, null);
            PulseHalo.Opacity = 0.28;
        }
    }

    /// <summary>
    /// Renders one pip per action in the running batch, filling the ones that
    /// are done. Four small marks say "step 2 of 4" faster than the words do.
    /// </summary>
    private void BuildStepPips(MetisActivity activity)
    {
        StepPips.Items.Clear();
        if (!activity.HasSteps || activity.StepCount < 2)
        {
            StepPips.Visibility = Visibility.Collapsed;
            return;
        }

        for (var step = 1; step <= Math.Min(activity.StepCount, 8); step++)
        {
            var complete = step <= activity.StepNumber;
            StepPips.Items.Add(new Ellipse
            {
                Width = 5,
                Height = 5,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = new SolidColorBrush(complete
                    ? MediaColor.FromRgb(0xF2, 0xF2, 0xF7)
                    : MediaColor.FromRgb(0x48, 0x48, 0x4A))
            });
        }

        StepPips.Visibility = Visibility.Visible;
    }

    private void SetThinking(bool thinking)
    {
        ThinkingDots.Visibility = thinking ? Visibility.Visible : Visibility.Collapsed;
        Ellipse[] dots = [Dot1, Dot2, Dot3];
        for (var index = 0; index < dots.Length; index++)
        {
            if (!thinking)
            {
                dots[index].BeginAnimation(OpacityProperty, null);
                continue;
            }

            var pulse = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(index * 160),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            dots[index].BeginAnimation(OpacityProperty, pulse);
        }
    }

    /// <summary>
    /// Centres the window on the primary monitor's top edge. The notch belongs
    /// to the screen the user is looking at, so it deliberately does not span
    /// the whole virtual desktop the way the guidance overlay does.
    /// </summary>
    private void PositionOverTopEdge()
    {
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = fromDevice.Transform(new System.Windows.Point(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen)));

        var primary = SystemParameters.PrimaryScreenWidth;
        Width = Math.Min(560, primary);
        Left = origin.X + ((primary - Width) / 2);
        Top = origin.Y;
    }

    private void MakeToolWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
