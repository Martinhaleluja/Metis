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

    // Annotation red rather than a system accent: these marks are meant to
    // read as someone drawing on the screen with a marker, not as chrome.
    private static readonly MediaColor MarkerColor = MediaColor.FromRgb(0xF0, 0x3A, 0x33);
    private static readonly MediaColor MarkerGlow = MediaColor.FromRgb(0xFF, 0x6B, 0x5E);

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

        // A diagram is built up over several steps, so its later marks join the
        // earlier ones instead of replacing them. Everything else replaces, so
        // a stale pointer never outlives the sentence it belonged to.
        if (!request.Accumulate)
        {
            MarkCanvas.Children.Clear();
        }

        if (request.Marks.Count == 0)
        {
            if (!request.Accumulate)
            {
                Visibility = Visibility.Collapsed;
            }

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
            case GuidanceMarkKind.Underline:
                AddUnderline(mark, fromDevice);
                break;
            case GuidanceMarkKind.Stroke:
                AddStroke(mark, fromDevice);
                break;
            case GuidanceMarkKind.Lasso:
                AddStroke(mark, fromDevice, closed: true);
                break;
            case GuidanceMarkKind.Polygon:
                AddStroke(mark, fromDevice, closed: true, smooth: !mark.StraightEdges);
                break;
            case GuidanceMarkKind.Capsule:
                AddCapsule(mark, fromDevice);
                break;
            case GuidanceMarkKind.Bracket:
                AddBracket(mark, fromDevice);
                break;
            case GuidanceMarkKind.Label:
                AddLabel(mark, fromDevice, ToWindowRect(mark, fromDevice, minimumSize: 0));
                break;
            default:
                AddRegion(mark, fromDevice);
                break;
        }
    }

    /// <summary>
    /// A marker stroke swept under a span of text, with the slight bow and
    /// uneven ends of a line drawn by hand rather than a ruled rectangle.
    /// </summary>
    private void AddUnderline(GuidanceMark mark, Matrix fromDevice)
    {
        var bounds = ToWindowRect(mark, fromDevice, minimumSize: 40);
        var y = bounds.Y + bounds.Height;
        var start = new System.Windows.Point(bounds.X - 4, y);
        var end = new System.Windows.Point(bounds.X + bounds.Width + 4, y);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new System.Windows.Point(start.X + (bounds.Width * 0.3), y + 5),
            new System.Windows.Point(start.X + (bounds.Width * 0.7), y - 2),
            end,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var stroke = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Effect = MarkerShadow()
        };

        MarkCanvas.Children.Add(stroke);
        AnimateStroke(stroke, bounds.Width + 16);

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, bounds);
        }
    }

    /// <summary>
    /// Draws a freehand stroke through a list of points — a line, a curve, a
    /// shape, a path traced over a diagram. Two points make a straight line;
    /// more are joined by a smooth curve so a sketched circle or arc reads as
    /// drawn rather than as a chain of segments.
    /// </summary>
    private void AddStroke(GuidanceMark mark, Matrix fromDevice, bool closed = false, bool smooth = true)
    {
        var points = (mark.Points ?? [])
            .Select(point => ToWindowPoint(point.ScreenX, point.ScreenY, fromDevice))
            .ToArray();
        if (points.Length < 2)
        {
            return;
        }

        var figure = new PathFigure { StartPoint = points[0], IsClosed = closed };
        if (closed)
        {
            // A lasso is filled faintly as well as outlined: the tint is what
            // makes it read as "this region" rather than "this squiggle".
            figure.IsFilled = true;
        }

        if (points.Length == 2)
        {
            figure.Segments.Add(new LineSegment(points[1], true));
        }
        else if (smooth)
        {
            // A cardinal spline through every point: it passes through what the
            // model asked for rather than merely near it.
            figure.Segments.Add(new PolyBezierSegment(SmoothThrough(points), true));
        }
        else
        {
            // Corners, not curves. Smoothing a triangle would round off the
            // corners, which on a geometry lesson is the part being taught.
            for (var index = 1; index < points.Length; index++)
            {
                figure.Segments.Add(new LineSegment(points[index], true));
            }
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var stroke = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = closed
                ? new SolidColorBrush(MediaColor.FromArgb(0x1C, MarkerColor.R, MarkerColor.G, MarkerColor.B))
                : null,
            Effect = MarkerShadow()
        };

        MarkCanvas.Children.Add(stroke);

        var length = 0d;
        for (var index = 1; index < points.Length; index++)
        {
            length += (points[index] - points[index - 1]).Length;
        }

        AnimateStroke(stroke, Math.Max(length, 1), fadeAfterHold: !mark.Persistent);

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            var minX = points.Min(point => point.X);
            var minY = points.Min(point => point.Y);
            AddLabel(mark, fromDevice, new Rect(minX, minY, points.Max(point => point.X) - minX, 1));
        }
    }

    /// <summary>
    /// Builds the control points for a smooth curve passing through every
    /// supplied point, three per segment as PolyBezierSegment expects.
    /// </summary>
    private static System.Windows.Point[] SmoothThrough(System.Windows.Point[] points)
    {
        const double tension = 0.25;
        var controls = new List<System.Windows.Point>();

        for (var index = 0; index < points.Length - 1; index++)
        {
            var previous = points[Math.Max(index - 1, 0)];
            var current = points[index];
            var next = points[index + 1];
            var following = points[Math.Min(index + 2, points.Length - 1)];

            controls.Add(new System.Windows.Point(
                current.X + ((next.X - previous.X) * tension),
                current.Y + ((next.Y - previous.Y) * tension)));
            controls.Add(new System.Windows.Point(
                next.X - ((following.X - current.X) * tension),
                next.Y - ((following.Y - current.Y) * tension)));
            controls.Add(next);
        }

        return controls.ToArray();
    }

    /// <summary>
    /// A rounded outline hugging a wide, short target. The radius follows half
    /// the height, so it reads as a capsule around a field rather than a
    /// rectangle that happens to have soft corners.
    /// </summary>
    private void AddCapsule(GuidanceMark mark, Matrix fromDevice)
    {
        const double padding = 5;
        var bounds = ToWindowRect(mark, fromDevice, minimumSize: 24);
        var width = bounds.Width + (padding * 2);
        var height = bounds.Height + (padding * 2);
        var radius = Math.Min(height / 2, 22);

        var outline = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 3,
            StrokeDashArray = [2.4, 1.8],
            StrokeDashCap = PenLineCap.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Effect = MarkerShadow(),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
        };

        var scale = new ScaleTransform(0.85, 0.85);
        outline.RenderTransform = scale;
        Canvas.SetLeft(outline, bounds.X - padding);
        Canvas.SetTop(outline, bounds.Y - padding);
        MarkCanvas.Children.Add(outline);

        var settle = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

        FinishMark(mark, fromDevice, bounds);
    }

    /// <summary>
    /// Four corner marks around a large region. At this size a full outline
    /// reads as a border and visually swallows the content it is meant to draw
    /// attention to; brackets mark the same area and leave it clear.
    /// </summary>
    private void AddBracket(GuidanceMark mark, Matrix fromDevice)
    {
        const double padding = 8;
        var bounds = ToWindowRect(mark, fromDevice, minimumSize: 80);

        // A maximized window fills the display, and its frame extends a few
        // pixels beyond it. Bracketing that without clamping puts all four
        // corners just off the edge of the screen, which draws the mark
        // perfectly and shows the user nothing at all.
        const double inset = 3;
        var surfaceWidth = Width > 0 ? Width : SystemParameters.VirtualScreenWidth;
        var surfaceHeight = Height > 0 ? Height : SystemParameters.VirtualScreenHeight;

        var left = Math.Max(inset, bounds.X - padding);
        var top = Math.Max(inset, bounds.Y - padding);
        var right = Math.Min(surfaceWidth - inset, bounds.X + bounds.Width + padding);
        var bottom = Math.Min(surfaceHeight - inset, bounds.Y + bounds.Height + padding);

        // A rectangle inverted by the clamp has nothing to bracket.
        if (right <= left || bottom <= top)
        {
            return;
        }

        bounds = new Rect(left, top, right - left, bottom - top);

        // Arms are a fraction of the shorter side so they stay proportionate on
        // both a small dialog and a full-screen panel.
        var arm = Math.Clamp(Math.Min(bounds.Width, bounds.Height) * 0.22, 18, 54);

        var geometry = new PathGeometry();
        geometry.Figures.Add(Corner(new System.Windows.Point(left, top + arm), new System.Windows.Point(left, top), new System.Windows.Point(left + arm, top)));
        geometry.Figures.Add(Corner(new System.Windows.Point(right - arm, top), new System.Windows.Point(right, top), new System.Windows.Point(right, top + arm)));
        geometry.Figures.Add(Corner(new System.Windows.Point(right, bottom - arm), new System.Windows.Point(right, bottom), new System.Windows.Point(right - arm, bottom)));
        geometry.Figures.Add(Corner(new System.Windows.Point(left + arm, bottom), new System.Windows.Point(left, bottom), new System.Windows.Point(left, bottom - arm)));
        geometry.Freeze();

        var brackets = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = MarkerShadow(),
            Opacity = 0
        };

        MarkCanvas.Children.Add(brackets);
        brackets.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        FinishMark(mark, fromDevice, bounds);
    }

    private static PathFigure Corner(System.Windows.Point from, System.Windows.Point elbow, System.Windows.Point to)
    {
        var figure = new PathFigure { StartPoint = from, IsClosed = false };
        figure.Segments.Add(new LineSegment(elbow, true));
        figure.Segments.Add(new LineSegment(to, true));
        return figure;
    }

    /// <summary>Adds the step badge and label every shaped mark shares.</summary>
    private void FinishMark(GuidanceMark mark, Matrix fromDevice, Rect bounds)
    {
        if (mark.StepNumber > 0)
        {
            AddStepBadge(mark.StepNumber, bounds);
        }

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, bounds);
        }
    }

    private void AddRegion(GuidanceMark mark, Matrix fromDevice)
    {
        var bounds = ToWindowRect(mark, fromDevice, minimumSize: 52);

        if (mark.Kind == GuidanceMarkKind.FocusRing)
        {
            AddTargetRing(mark, fromDevice, bounds);
            return;
        }

        var outline = new System.Windows.Shapes.Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            RadiusX = 5,
            RadiusY = 5,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 3,
            StrokeDashArray = [3, 2],
            Fill = System.Windows.Media.Brushes.Transparent,
            Effect = MarkerShadow()
        };

        Canvas.SetLeft(outline, bounds.X);
        Canvas.SetTop(outline, bounds.Y);
        MarkCanvas.Children.Add(outline);

        if (mark.StepNumber > 0)
        {
            AddStepBadge(mark.StepNumber, bounds);
        }

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, bounds);
        }
    }

    /// <summary>
    /// A dashed ring around the target, drawn as a circle rather than a
    /// rectangle because a hand circling something on screen is the gesture
    /// these marks are imitating. It rotates slowly so it reads as alive
    /// without flashing.
    /// </summary>
    private void AddTargetRing(GuidanceMark mark, Matrix fromDevice, Rect bounds)
    {
        // A circle only suits a roughly square target. Anything long — a search
        // bar, a menu row, a text field — gets an outline that takes its shape,
        // because a circle around a wide control either misses its ends or
        // swallows everything around it.
        var aspect = bounds.Height <= 0 ? 1 : bounds.Width / bounds.Height;
        if (mark.HasShape && aspect is > 1.45 or < 0.69)
        {
            AddShapedRing(mark, fromDevice, bounds);
            return;
        }

        var diameter = Math.Max(bounds.Width, bounds.Height);
        var centre = new System.Windows.Point(
            bounds.X + (bounds.Width / 2),
            bounds.Y + (bounds.Height / 2));

        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = diameter,
            Height = diameter,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 3,
            StrokeDashArray = [2.4, 1.8],
            StrokeDashCap = PenLineCap.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Effect = MarkerShadow(),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
        };

        var spin = new RotateTransform();
        ring.RenderTransform = spin;
        Canvas.SetLeft(ring, centre.X - (diameter / 2));
        Canvas.SetTop(ring, centre.Y - (diameter / 2));
        MarkCanvas.Children.Add(ring);

        spin.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever });

        // Drawn on, so the ring appears to be circled rather than to pop in.
        var draw = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
        };
        var scale = new ScaleTransform(0.4, 0.4);
        ring.RenderTransform = new TransformGroup { Children = { scale, spin } };
        scale.CenterX = diameter / 2;
        scale.CenterY = diameter / 2;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, draw);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, draw);

        if (mark.StepNumber > 0)
        {
            AddStepBadge(mark.StepNumber, bounds);
        }

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, bounds);
        }
    }

    /// <summary>
    /// A dashed outline hugging a non-square control, padded slightly so the
    /// mark sits just outside what it is marking rather than on top of it.
    /// </summary>
    private void AddShapedRing(GuidanceMark mark, Matrix fromDevice, Rect bounds)
    {
        const double padding = 5;
        var outline = new System.Windows.Shapes.Rectangle
        {
            Width = bounds.Width + (padding * 2),
            Height = bounds.Height + (padding * 2),
            RadiusX = Math.Min(12, (bounds.Height / 2) + padding),
            RadiusY = Math.Min(12, (bounds.Height / 2) + padding),
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 3,
            StrokeDashArray = [2.4, 1.8],
            StrokeDashCap = PenLineCap.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Effect = MarkerShadow(),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
        };

        var scale = new ScaleTransform(0.82, 0.82);
        outline.RenderTransform = scale;
        Canvas.SetLeft(outline, bounds.X - padding);
        Canvas.SetTop(outline, bounds.Y - padding);
        MarkCanvas.Children.Add(outline);

        var settle = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

        if (mark.StepNumber > 0)
        {
            AddStepBadge(mark.StepNumber, bounds);
        }

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, bounds);
        }
    }

    private static DropShadowEffect MarkerShadow() => new()
    {
        Color = MarkerGlow,
        BlurRadius = 10,
        ShadowDepth = 0,
        Opacity = 0.55
    };

    private void AddStepBadge(int stepNumber, Rect bounds)
    {
        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(MarkerColor),
            Child = new TextBlock
            {
                Text = stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(badge, bounds.X - 11);
        Canvas.SetTop(badge, bounds.Y - 11);
        MarkCanvas.Children.Add(badge);
    }

    private void AddLabel(GuidanceMark mark, Matrix fromDevice, Rect bounds)
    {
        if (string.IsNullOrWhiteSpace(mark.Label))
        {
            return;
        }

        // A small filled badge rather than a panel: these sit over the user's
        // work, so they carry a few words and get out of the way. Matching the
        // marker colour ties the label to the stroke it belongs to.
        var label = new Border
        {
            Background = new SolidColorBrush(MarkerColor),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 3, 7, 3),
            MaxWidth = 210,
            Effect = new DropShadowEffect
            {
                Color = MediaColor.FromRgb(0, 0, 0),
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.28
            },
            Child = new TextBlock
            {
                Text = mark.Label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = label.DesiredSize;

        // Prefer sitting just above the mark, and drop below when the mark is
        // near the top edge, so a badge never leaves the desktop or covers the
        // thing it is naming.
        var top = bounds.Y - size.Height - 8;
        if (top < 4)
        {
            top = bounds.Y + bounds.Height + 8;
        }

        var left = bounds.X + (bounds.Width / 2d) - (size.Width / 2d);

        // Width/Height are set explicitly when the overlay is stretched over the
        // desktop; ActualWidth stays zero because this window never runs a
        // normal layout pass, which pinned every label to the top-left corner.
        var surfaceWidth = Width > 0 ? Width : SystemParameters.VirtualScreenWidth;
        var surfaceHeight = Height > 0 ? Height : SystemParameters.VirtualScreenHeight;

        Canvas.SetLeft(label, Math.Clamp(left, 4, Math.Max(4, surfaceWidth - size.Width - 4)));
        Canvas.SetTop(label, Math.Clamp(top, 4, Math.Max(4, surfaceHeight - size.Height - 4)));
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
        // Two points mean the arrow was given both ends — a force or a flow in
        // a diagram, which runs from where it was said to run to where it was
        // said to end. Without them the arrow is pointing at something real, so
        // it picks its own approach instead.
        var drawn = mark.Points is { Count: >= 2 };
        System.Windows.Point target;
        System.Windows.Point tail;
        bool fromLeft;

        if (drawn)
        {
            tail = ToWindowPoint(mark.Points![0].ScreenX, mark.Points[0].ScreenY, fromDevice);
            target = ToWindowPoint(mark.Points[1].ScreenX, mark.Points[1].ScreenY, fromDevice);
            fromLeft = tail.X > target.X;
        }
        else
        {
            target = ToWindowPoint(mark.ScreenX, mark.ScreenY, fromDevice);

            // Approach from whichever side has room, so the arrow never runs off
            // the desktop and never covers the control it is pointing at.
            fromLeft = target.X > Width / 2d;
            var fromAbove = target.Y > Height / 2d;
            var reach = 170d;
            tail = new System.Windows.Point(
                target.X + (fromLeft ? -reach : reach),
                target.Y + (fromAbove ? -reach * 0.62 : reach * 0.62));
        }

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
        if (drawn)
        {
            // A vector is straight. The hand-drawn bow belongs to an arrow that
            // is pointing across the screen at something, not to one whose
            // direction is the quantity being taught.
            shaft.Segments.Add(new LineSegment(target, true));
        }
        else
        {
            shaft.Segments.Add(new BezierSegment(control1, control2, target, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(shaft);

        // The head is drawn as two open strokes rather than a filled triangle,
        // matching the marker-pen feel of the shaft.
        // Barbs sweep back from the tip at 30 degrees either side of the line
        // of travel. Wider angles put the barbs ahead of the tip, which reads
        // as an arrowhead pointing the wrong way.
        const double barbAngle = 0.52;
        var approach = drawn ? tail : control2;
        var angle = Math.Atan2(target.Y - approach.Y, target.X - approach.X);
        geometry.Figures.Add(HeadStroke(target, angle, barbAngle));
        geometry.Figures.Add(HeadStroke(target, angle, -barbAngle));

        var stroke = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(MarkerColor),
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect
            {
                Color = MarkerGlow,
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
        AnimateStroke(
            stroke,
            (chord * (drawn ? 1d : 1.25)) + 52,
            fadeAfterHold: !mark.Persistent);

        if (!string.IsNullOrWhiteSpace(mark.Label))
        {
            AddLabel(mark, fromDevice, new Rect(tail.X - 60, tail.Y - 26, 120, 1));
        }
    }

    /// <summary>
    /// Reveals the stroke by retracting its dash gap, so the line appears to be
    /// drawn from tail to tip, then holds and fades.
    /// </summary>
    private static void AnimateStroke(
        System.Windows.Shapes.Path stroke,
        double pathLength,
        bool fadeAfterHold = true)
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

        if (!fadeAfterHold)
        {
            // A diagram stays until the overlay clears it. The fade below runs
            // on a fixed clock of its own, so on a stage narrated for ten
            // seconds it would rub the shape out mid-sentence.
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(2200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        stroke.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// One barb of the arrowhead, running backwards from the tip. Subtracting
    /// the offset vector is what makes it trail the tip rather than lead it.
    /// </summary>
    private static PathFigure HeadStroke(System.Windows.Point tip, double angle, double spread)
    {
        const double barbLength = 24;
        var figure = new PathFigure { StartPoint = tip, IsClosed = false };
        figure.Segments.Add(new LineSegment(
            new System.Windows.Point(
                tip.X - (barbLength * Math.Cos(angle + spread)),
                tip.Y - (barbLength * Math.Sin(angle + spread))),
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
