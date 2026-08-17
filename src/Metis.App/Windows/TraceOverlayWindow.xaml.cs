using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Metis.Core.Models;
using Metis.Core.Services;
using MediaColor = System.Windows.Media.Color;

namespace Metis.App.Windows;

/// <summary>
/// The surface the user draws on. Holding the inspect chord arms this window
/// over the whole desktop; dragging paints a stroke in the same ink Metis marks
/// with, and releasing hands the traced region back to the runtime.
///
/// It is a separate window from the guidance overlay on purpose. That one is
/// click-through and purely presentational; this one takes mouse input. Keeping
/// them apart means a fault in tracing can never leave the presentational layer
/// intercepting the user's clicks.
/// </summary>
public partial class TraceOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly MediaColor MarkerColor = MediaColor.FromRgb(0xE0, 0x34, 0x2B);
    private static readonly MediaColor MarkerGlow = MediaColor.FromRgb(0xFF, 0x6B, 0x5E);

    private readonly List<GuidancePoint> _rawPoints = [];
    private System.Windows.Shapes.Polyline? _stroke;
    private System.Windows.Shapes.Rectangle? _box;
    private System.Windows.Point _boxOrigin;
    private bool _allowClose;
    private bool _drawing;

    /// <summary>
    /// While sticky the surface stays up after the keys are released, so the
    /// user can take their time and finish from the notch. This is the mode the
    /// toolbar puts it in; a quick hold-and-drag still commits on release.
    /// </summary>
    public bool IsSticky { get; private set; }

    public TraceTool Tool { get; private set; } = TraceTool.Freehand;

    public TraceOverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            MakeToolWindow();
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

    /// <summary>
    /// Raised when the user finishes a trace. The path is already resampled and
    /// in screen pixels.
    /// </summary>
    public event EventHandler<IReadOnlyList<GuidancePoint>>? TraceCompleted;

    /// <summary>
    /// Raised when the gesture was a tap rather than a trace, so the caller can
    /// fall back to resolving the single control under that point.
    /// </summary>
    public event EventHandler<GuidancePoint>? TapCompleted;

    /// <summary>
    /// Raised whenever the surface leaves the armed state, for any reason. The
    /// notch listens to this rather than to each individual ending, so the two
    /// windows cannot disagree about whether tracing is still in progress.
    /// </summary>
    public event EventHandler? Disarmed;

    public bool IsArmed { get; private set; }

    public void AllowClose()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// Puts the pen out: the desktop takes a light scrim and the cursor becomes
    /// a crosshair, so the mode change is visible before anything is drawn.
    /// </summary>
    /// <summary>
    /// Keeps the surface up after the chord is released. The user finishes from
    /// the notch instead of having to hold three keys while drawing.
    /// </summary>
    public void MakeSticky() => IsSticky = true;

    /// <summary>Switches tool without losing the surface.</summary>
    public void SetTool(TraceTool tool)
    {
        Tool = tool;
        IsSticky = true;
        HintText.Text = tool switch
        {
            TraceTool.Rectangle => "Drag a rectangle",
            TraceTool.FullScreen => "Whole screen selected",
            _ => "Draw around an area"
        };
        Hint.Opacity = 1;
        ClearInk();

        if (tool == TraceTool.FullScreen)
        {
            SelectWholeScreen();
        }
    }

    public void Arm()
    {
        if (IsArmed)
        {
            return;
        }

        IsArmed = true;

        // Sticky from the start: the surface stays up until an area is actually
        // marked. Holding three keys steady while drawing was the thing that
        // made this unusable, and it also meant the stroke vanished before the
        // user could look at what they had drawn.
        IsSticky = true;
        _drawing = false;
        ClearInk();

        StretchOverVirtualDesktop();
        Show();
        PositionHintAtCursor();

        Scrim.Fill = new SolidColorBrush(MediaColor.FromArgb(0x0F, 0x0A, 0x0C, 0x10));
        Animate(Scrim, OpacityProperty, 0, 1, 120, new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(Hint, OpacityProperty, 0, 1, 160, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    /// <summary>
    /// Puts the pen away without committing anything.
    /// </summary>
    public void Disarm()
    {
        if (!IsArmed)
        {
            return;
        }

        IsArmed = false;
        IsSticky = false;
        _drawing = false;
        Tool = TraceTool.Freehand;
        ClearInk();
        Hint.Opacity = 0;
        Hide();

        // Every way out of tracing goes through here — Escape, a stray click, a
        // completed mark, the notch's own cancel. Announcing it from this one
        // place is what stops the notch being told about some of those and left
        // in trace mode for the rest of the session by the others.
        Disarmed?.Invoke(this, EventArgs.Empty);
    }

    private void ClearInk()
    {
        _rawPoints.Clear();
        InkCanvas.Children.Clear();
        _stroke = null;
        _box = null;
    }

    /// <summary>
    /// The whole desktop as a region, for when the question is about everything
    /// on screen and drawing a loop round it would be busywork.
    /// </summary>
    private void SelectWholeScreen()
    {
        _rawPoints.Clear();
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var right = left + GetSystemMetrics(SmCxVirtualScreen);
        var bottom = top + GetSystemMetrics(SmCyVirtualScreen);

        _rawPoints.Add(new GuidancePoint(left + 2, top + 2));
        _rawPoints.Add(new GuidancePoint(right - 2, top + 2));
        _rawPoints.Add(new GuidancePoint(right - 2, bottom - 2));
        _rawPoints.Add(new GuidancePoint(left + 2, bottom - 2));

        DrawBox(new System.Windows.Point(1, 1), new System.Windows.Point(Width - 2, Height - 2));
    }

    /// <summary>
    /// Ends the gesture and reports it. Releasing the chord mid-drag commits
    /// rather than discarding: the user has already said what they meant.
    /// </summary>
    /// <summary>
    /// Called when the chord is released. Deliberately does nothing: the
    /// surface belongs to the user until they have marked something, and
    /// letting go of the keys is not an instruction to give up.
    /// </summary>
    public void Commit()
    {
    }

    /// <summary>
    /// Which tool produced the last committed mark. Disarming resets the live
    /// tool, so this is captured first — without it a suspect region gives no
    /// way to tell a freehand loop from a dragged box after the fact.
    /// </summary>
    public TraceTool LastCommittedTool { get; private set; } = TraceTool.Freehand;

    /// <summary>Commits regardless of sticky, for the notch's Ask button.</summary>
    public void CommitNow()
    {
        if (!IsArmed)
        {
            return;
        }

        var raw = _rawPoints.ToArray();
        LastCommittedTool = Tool;
        Disarm();

        if (raw.Length == 0)
        {
            return;
        }

        if (!TracePath.IsTrace(raw))
        {
            TapCompleted?.Invoke(this, raw[^1]);
            return;
        }

        TraceCompleted?.Invoke(this, TracePath.Smooth(raw));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsArmed)
        {
            return;
        }

        _drawing = true;
        ClearInk();

        if (Tool == TraceTool.Rectangle)
        {
            _boxOrigin = e.GetPosition(Root);
            DrawBox(_boxOrigin, _boxOrigin);
            CaptureMouse();
            Hint.Opacity = 0;
            return;
        }

        _stroke = new System.Windows.Shapes.Polyline
        {
            Stroke = FrozenBrush(MarkerColor),
            StrokeThickness = 3.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Effect = new DropShadowEffect
            {
                Color = MarkerGlow,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.5
            }
        };
        InkCanvas.Children.Add(_stroke);

        AddPoint(e.GetPosition(Root));
        CaptureMouse();
        Hint.Opacity = 0;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drawing || e.LeftButton != MouseButtonState.Pressed)
        {
            // Before the stroke starts the hint rides with the pointer. Left in
            // the corner of the desktop it is text the user never looks at,
            // because their attention is already where they are about to draw.
            PositionHint(e.GetPosition(Root));
            return;
        }

        var position = e.GetPosition(Root);
        if (Tool == TraceTool.Rectangle)
        {
            DrawBox(_boxOrigin, position);
            return;
        }

        // Appended directly with no easing: this is direct manipulation, and
        // any smoothing of the motion itself reads as lag rather than ink.
        AddPoint(position);
    }

    /// <summary>
    /// Draws the rectangle and records its corners, so a dragged box produces
    /// the same kind of region a freehand loop does.
    /// </summary>
    private void DrawBox(System.Windows.Point from, System.Windows.Point to)
    {
        var x = Math.Min(from.X, to.X);
        var y = Math.Min(from.Y, to.Y);
        var width = Math.Abs(to.X - from.X);
        var height = Math.Abs(to.Y - from.Y);

        if (_box is null)
        {
            _box = new System.Windows.Shapes.Rectangle
            {
                Stroke = FrozenBrush(MarkerColor),
                StrokeThickness = 3,
                StrokeDashArray = [3, 2],
                Fill = FrozenBrush(MediaColor.FromArgb(0x1C, MarkerColor.R, MarkerColor.G, MarkerColor.B)),
                RadiusX = 3,
                RadiusY = 3
            };
            InkCanvas.Children.Add(_box);
        }

        _box.Width = Math.Max(1, width);
        _box.Height = Math.Max(1, height);
        System.Windows.Controls.Canvas.SetLeft(_box, x);
        System.Windows.Controls.Canvas.SetTop(_box, y);

        _rawPoints.Clear();
        foreach (var corner in new[]
                 {
                     new System.Windows.Point(x, y),
                     new System.Windows.Point(x + width, y),
                     new System.Windows.Point(x + width, y + height),
                     new System.Windows.Point(x, y + height)
                 })
        {
            _rawPoints.Add(ToScreenPoint(corner));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_drawing)
        {
            return;
        }

        _drawing = false;
        ReleaseMouseCapture();

        // A completed mark is the user's answer, so Metis carries on rather
        // than asking them to confirm what they just drew. An incomplete
        // gesture leaves the surface up so they can try again.
        if (TracePath.IsTrace(_rawPoints))
        {
            CommitNow();
            return;
        }

        HintText.Text = "Draw around an area, or press Esc";
        Hint.Opacity = 1;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Disarm();
        }
    }

    private void AddPoint(System.Windows.Point windowPoint)
    {
        _stroke?.Points.Add(windowPoint);
        _rawPoints.Add(ToScreenPoint(windowPoint));
    }

    /// <summary>
    /// Places the hint just below and right of the pointer, flipping to the
    /// other side rather than sliding under the edge when there is no room.
    /// </summary>
    private void PositionHint(System.Windows.Point pointer)
    {
        const double gap = 18;
        var width = Hint.ActualWidth > 0 ? Hint.ActualWidth : 150;
        var height = Hint.ActualHeight > 0 ? Hint.ActualHeight : 30;

        var left = pointer.X + gap;
        var top = pointer.Y + gap;

        if (left + width > Width - 8)
        {
            left = pointer.X - gap - width;
        }

        if (top + height > Height - 8)
        {
            top = pointer.Y - gap - height;
        }

        Hint.Margin = new Thickness(
            Math.Max(8, left),
            Math.Max(8, top),
            0,
            0);
    }

    /// <summary>
    /// Puts the hint beside the pointer before the first mouse move, so it is
    /// never briefly shown in the wrong place when the surface appears.
    /// </summary>
    private void PositionHintAtCursor()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var local = fromDevice.Transform(new System.Windows.Point(
            point.X - GetSystemMetrics(SmXVirtualScreen),
            point.Y - GetSystemMetrics(SmYVirtualScreen)));

        PositionHint(local);
    }

    private GuidancePoint ToScreenPoint(System.Windows.Point windowPoint)
    {
        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;
        var device = toDevice.Transform(windowPoint);
        return new GuidancePoint(
            (int)Math.Round(device.X + GetSystemMetrics(SmXVirtualScreen)),
            (int)Math.Round(device.Y + GetSystemMetrics(SmYVirtualScreen)));
    }

    private static SolidColorBrush FrozenBrush(MediaColor colour)
    {
        // Frozen brushes skip change tracking, which matters on a surface that
        // redraws a growing stroke every frame across the whole desktop.
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    private static void Animate(
        UIElement target,
        DependencyProperty property,
        double from,
        double to,
        double milliseconds,
        IEasingFunction easing)
    {
        target.BeginAnimation(
            property,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            });
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

    /// <summary>
    /// Puts this surface immediately beneath another window in the z-order.
    /// Both windows are topmost, so which one ends up on top is otherwise a
    /// race — and when the surface wins it covers the notch and silently eats
    /// every click on the toolbar, which is the only way to finish a trace.
    /// Naming the window to sit under makes the order a fact rather than a
    /// matter of timing.
    /// </summary>
    public void PlaceBelow(nint otherWindow)
    {
        const uint noMove = 0x0002, noSize = 0x0001, noActivate = 0x0010;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero && otherWindow != nint.Zero)
        {
            SetWindowPos(handle, otherWindow, 0, 0, 0, 0, noMove | noSize | noActivate);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

    private void MakeToolWindow()
    {
        // No WS_EX_TRANSPARENT here: this window must receive clicks. It stays
        // NOACTIVATE so tracing never steals focus from the app underneath.
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointStruct point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }
}
