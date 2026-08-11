using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Metis.Core.Models;
using MediaColor = System.Windows.Media.Color;

namespace Metis.App.Windows;

/// <summary>
/// A click-through overlay spanning the whole virtual desktop. It draws
/// temporary marks — focus rings, boxes, arrows, labels, and numbered steps —
/// over whatever application the user is in, and never touches that
/// application. Every frame replaces the previous one and expires on its own,
/// so guidance cannot accumulate or outlive the step it belongs to.
/// </summary>
public partial class GuidanceOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly MediaColor AccentColor = MediaColor.FromRgb(0x8E, 0xD8, 0xFF);
    private static readonly MediaColor AccentGlow = MediaColor.FromRgb(0x56, 0xCC, 0xFF);

    private readonly DispatcherTimer _expiryTimer;
    private bool _allowClose;

    public GuidanceOverlayWindow()
    {
        InitializeComponent();
        _expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _expiryTimer.Tick += (_, _) => Clear();

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            StretchOverVirtualDesktop();
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
        _expiryTimer.Stop();
        Close();
    }

    /// <summary>
    /// Replaces the visible guidance. An empty request clears the overlay.
    /// </summary>
    public void Show(GuidanceOverlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _expiryTimer.Stop();
        MarkCanvas.Children.Clear();

        if (request.Marks.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        StretchOverVirtualDesktop();
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;

        if (request.DimBackground)
        {
            AddDimming(request.Marks, fromDevice);
        }

        foreach (var mark in request.Marks)
        {
            AddMark(mark, fromDevice);
        }

        Visibility = Visibility.Visible;
        _expiryTimer.Interval = request.HoldDuration > TimeSpan.Zero
            ? request.HoldDuration
            : TimeSpan.FromSeconds(5);
        _expiryTimer.Start();
    }

    public void Clear()
    {
        _expiryTimer.Stop();
        MarkCanvas.Children.Clear();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Darkens everything except the marked regions, so the user's eye lands on
    /// the control being described. Built as one even-odd geometry rather than
    /// four rectangles so it stays correct for several marks at once.
    /// </summary>
    private void AddDimming(IReadOnlyList<GuidanceMark> marks, Matrix fromDevice)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Width, Height));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(full);

        foreach (var mark in marks)
        {
            var bounds = ToWindowRect(mark, fromDevice, minimumSize: 44);
            group.Children.Add(new RectangleGeometry(bounds, 6, 6));
        }

        var dim = new System.Windows.Shapes.Path
        {
            Data = group,
            Fill = new SolidColorBrush(MediaColor.FromArgb(0x8A, 0x05, 0x09, 0x0D))
        };
        MarkCanvas.Children.Add(dim);
    }

    private void AddMark(GuidanceMark mark, Matrix fromDevice)
    {
        switch (mark.Kind)
        {
            case GuidanceMarkKind.Arrow:
                AddArrow(mark, fromDevice);
                break;
            case GuidanceMarkKind.Label:
                AddLabel(mark, fromDevice, ToWindowRect(mark, fromDevice, minimumSize: 0));
                break;
            default:
                AddRegion(mark, fromDevice);
                break;
        }
    }

    private void AddRegion(GuidanceMark mark, Matrix fromDevice)
    {
        var bounds = ToWindowRect(mark, fromDevice, minimumSize: 44);
        var outline = new System.Windows.Shapes.Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            RadiusX = 6,
            RadiusY = 6,
            Stroke = new SolidColorBrush(AccentColor),
            StrokeThickness = mark.Kind == GuidanceMarkKind.FocusRing ? 3 : 2,
            Fill = new SolidColorBrush(MediaColor.FromArgb(0x1F, AccentColor.R, AccentColor.G, AccentColor.B)),
            Effect = new DropShadowEffect
            {
                Color = AccentGlow,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.85
            }
        };

        Canvas.SetLeft(outline, bounds.X);
        Canvas.SetTop(outline, bounds.Y);
        MarkCanvas.Children.Add(outline);

        if (mark.Kind == GuidanceMarkKind.FocusRing)
        {
            var pulse = new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            outline.BeginAnimation(OpacityProperty, pulse);
        }

        if (mark.StepNumber > 0)
        {
            AddStepBadge(mark.StepNumber, bounds);
        }

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, bounds);
        }
    }

    private void AddStepBadge(int stepNumber, Rect bounds)
    {
        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(AccentColor),
            Child = new TextBlock
            {
                Text = stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Foreground = new SolidColorBrush(MediaColor.FromRgb(0x09, 0x0D, 0x12)),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(badge, bounds.X - 13);
        Canvas.SetTop(badge, bounds.Y - 13);
        MarkCanvas.Children.Add(badge);
    }

    private void AddLabel(GuidanceMark mark, Matrix fromDevice, Rect bounds)
    {
        if (string.IsNullOrWhiteSpace(mark.Label))
        {
            return;
        }

        var label = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF2, 0x0D, 0x13, 0x1A)),
            BorderBrush = new SolidColorBrush(AccentColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 5, 10, 5),
            MaxWidth = 340,
            Child = new TextBlock
            {
                Text = mark.Label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            }
        };

        label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = label.DesiredSize;

        // Prefer above the target; drop below when the target sits near the top
        // edge, so a label never leaves the desktop.
        var top = bounds.Y - size.Height - 10;
        if (top < 4)
        {
            top = bounds.Y + bounds.Height + 10;
        }

        var left = Math.Clamp(
            bounds.X + (bounds.Width / 2d) - (size.Width / 2d),
            4,
            Math.Max(4, Width - size.Width - 4));

        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, Math.Clamp(top, 4, Math.Max(4, Height - size.Height - 4)));
        MarkCanvas.Children.Add(label);
        _ = fromDevice;
    }

    /// <summary>
    /// Draws a hand-drawn arrow that sweeps toward the target and then fades.
    /// The stroke is animated rather than simply appearing: a mark that is drawn
    /// carries the eye along it to the thing being pointed at, which a static
    /// arrow does not.
    /// </summary>
    private void AddArrow(GuidanceMark mark, Matrix fromDevice)
    {
        var target = ToWindowPoint(mark.ScreenX, mark.ScreenY, fromDevice);

        // Approach from whichever side has room, so the arrow never runs off
        // the desktop and never covers the control it is pointing at.
        var fromLeft = target.X > Width / 2d;
        var fromAbove = target.Y > Height / 2d;
        var reach = 170d;
        var tail = new System.Windows.Point(
            target.X + (fromLeft ? -reach : reach),
            target.Y + (fromAbove ? -reach * 0.62 : reach * 0.62));

        // The bow is what makes it read as drawn by hand rather than plotted.
        // Both control points sit to one side of the straight line.
        var bow = fromLeft ? 1 : -1;
        var control1 = new System.Windows.Point(
            tail.X + ((target.X - tail.X) * 0.35) + (34 * bow),
            tail.Y + ((target.Y - tail.Y) * 0.55) + 26);
        var control2 = new System.Windows.Point(
            tail.X + ((target.X - tail.X) * 0.74) + (14 * bow),
            tail.Y + ((target.Y - tail.Y) * 0.86) + 10);

        var shaft = new PathFigure { StartPoint = tail, IsClosed = false };
        shaft.Segments.Add(new BezierSegment(control1, control2, target, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(shaft);

        // The head is drawn as two open strokes rather than a filled triangle,
        // matching the marker-pen feel of the shaft.
        var angle = Math.Atan2(target.Y - control2.Y, target.X - control2.X);
        geometry.Figures.Add(HeadStroke(target, angle, 2.5));
        geometry.Figures.Add(HeadStroke(target, angle, -2.5));

        var stroke = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(AccentColor),
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect
            {
                Color = AccentGlow,
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.85
            }
        };

        MarkCanvas.Children.Add(stroke);

        // The dash has to span the whole path for the reveal to work, so the
        // arc length is approximated from the chord: a curve this shallow runs
        // about a quarter longer than the straight line, plus the two head
        // strokes.
        var chord = Math.Sqrt(Math.Pow(target.X - tail.X, 2) + Math.Pow(target.Y - tail.Y, 2));
        AnimateStroke(stroke, (chord * 1.25) + 52);

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, new Rect(tail.X - 60, tail.Y - 26, 120, 1));
        }
    }

    /// <summary>
    /// Reveals the stroke by retracting its dash gap, so the line appears to be
    /// drawn from tail to tip, then holds and fades.
    /// </summary>
    private static void AnimateStroke(System.Windows.Shapes.Path stroke, double pathLength)
    {
        // Dash lengths are multiples of the stroke thickness, so the span is
        // converted before use. One dash and one gap, each as long as the whole
        // path, offset so nothing shows at the start; animating the offset to
        // zero walks the dash into view from the tail.
        var span = Math.Max(1d, pathLength / stroke.StrokeThickness);
        stroke.StrokeDashArray = [span, span];
        stroke.StrokeDashOffset = span;

        var draw = new DoubleAnimation(span, 0, TimeSpan.FromMilliseconds(560))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        stroke.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, draw);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(2200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        stroke.BeginAnimation(OpacityProperty, fade);
    }

    private static PathFigure HeadStroke(System.Windows.Point tip, double angle, double spread)
    {
        var figure = new PathFigure { StartPoint = tip, IsClosed = false };
        figure.Segments.Add(new LineSegment(
            new System.Windows.Point(
                tip.X - (26 * Math.Cos(angle + spread)),
                tip.Y - (26 * Math.Sin(angle + spread))),
            true));
        return figure;
    }

    private Rect ToWindowRect(GuidanceMark mark, Matrix fromDevice, double minimumSize)
    {
        var width = Math.Max(mark.Width, minimumSize);
        var height = Math.Max(mark.Height, minimumSize);
        var topLeft = ToWindowPoint(
            mark.ScreenX - (int)Math.Round(width / 2d),
            mark.ScreenY - (int)Math.Round(height / 2d),
            fromDevice);
        var bottomRight = ToWindowPoint(
            mark.ScreenX + (int)Math.Round(width / 2d),
            mark.ScreenY + (int)Math.Round(height / 2d),
            fromDevice);
        return new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    /// <summary>
    /// Converts a virtual-desktop pixel to a point inside this window, which is
    /// itself positioned at the virtual desktop's origin.
    /// </summary>
    private System.Windows.Point ToWindowPoint(int screenX, int screenY, Matrix fromDevice)
    {
        var transformed = fromDevice.Transform(new System.Windows.Point(screenX, screenY));
        var origin = fromDevice.Transform(new System.Windows.Point(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen)));
        return new System.Windows.Point(transformed.X - origin.X, transformed.Y - origin.Y);
    }

    private void StretchOverVirtualDesktop()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen)));
        var size = fromDevice.Transform(new System.Windows.Point(
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen)));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, size.X);
        Height = Math.Max(1, size.Y);
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
