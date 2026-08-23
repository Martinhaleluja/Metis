using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Metis.Core.Models;
using Metis.Core.Services;
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

    /// <summary>
    /// How wide the notch becomes when it is the chat. Wide enough for a
    /// readable measure of body text without becoming a window that dominates
    /// the top of the screen.
    /// </summary>
    private const double ChatWidth = 640;

    /// <summary>
    /// The tallest the chat is allowed to grow. Past this the transcript
    /// scrolls inside the notch instead, because a panel that keeps growing
    /// eventually covers the thing the user is asking about.
    /// </summary>
    private const double ChatMaxHeight = 520;

    /// <summary>
    /// The first-run panel is narrower than the chat. A sign-in form stretched
    /// to the chat's 640px puts a 40-character-wide field in the middle of a
    /// mostly empty black sheet, which reads as unfinished rather than roomy.
    /// </summary>
    private const double AuthWidth = 430;

    /// <summary>The host window around <see cref="AuthWidth"/>, with the same slack the chat gets.</summary>
    private const double AuthWindowWidth = 454;

    /// <summary>
    /// Widths and heights of the host window in each of its two shapes. The
    /// window is kept only slightly larger than the notch it draws, because its
    /// transparent margin still swallows clicks aimed at whatever is underneath.
    /// </summary>
    private const double PillWindowWidth = 560;
    private const double PillWindowHeight = 64;
    private const double ChatWindowWidth = 664;
    private const double WindowSlack = 26;

    /// <summary>
    /// Width the notch takes while it is the trace toolbar: six controls plus
    /// the body's own padding, with room to spare so nothing sits against the
    /// rounded edge where it is awkward to hit.
    /// </summary>
    private const double ToolbarWidth = 284;
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

    /// <summary>
    /// The window that had the keyboard before the chat took it, so it can be
    /// handed back when the chat folds away. Without this, dismissing the chat
    /// leaves the user typing into nothing.
    /// </summary>
    private nint _previousForeground;

    /// <summary>The height the body is currently animating towards.</summary>
    private double _bodyHeightTarget = ExpandedHeight;

    /// <summary>Raised when the user pulls the notch down or clicks it.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the user clicks the gear on the notch.</summary>
    public event EventHandler? SettingsRequested;


    /// <summary>Raised when a trace tool is chosen from the notch toolbar.</summary>
    public event EventHandler<TraceTool>? TraceToolPicked;

    /// <summary>Raised when the user confirms the marked-out area.</summary>
    public event EventHandler? TraceConfirmed;

    /// <summary>Raised when the user abandons the trace.</summary>
    public event EventHandler? TraceCancelled;

    /// <summary>True while the notch is showing the trace toolbar.</summary>
    public bool IsTracing { get; private set; }

    /// <summary>
    /// True while the notch is unfolded into any panel rather than resting.
    ///
    /// The resting behaviours — tucking, peeking on hover, being dragged, being
    /// resized to narrate an activity — are all wrong whenever something is
    /// open inside the notch, and they were wrong for the same reason before
    /// there was a second panel. Asking this rather than asking about the chat
    /// specifically is what stopped the first-run panel from being tucked away
    /// underneath the user while they were typing their password into it.
    /// </summary>
    public bool IsPanelOpen => IsChatOpen || IsAuthOpen;

    // ============================== The chat ==============================

    /// <summary>True while the notch is open as the chat.</summary>
    public bool IsChatOpen { get; private set; }

    /// <summary>The conversation itself, for the host to wire to the runtime.</summary>
    public NotchChat Chat => ChatHost;

    /// <summary>Raised when the chat asks for the setup window.</summary>
    public event EventHandler? ChatSetupRequested;

    /// <summary>
    /// Connects the chat panel to the notch's own geometry. Called once, after
    /// the panel has been given a runtime.
    /// </summary>
    public void ConnectChat()
    {
        ChatHost.CloseRequested += (_, _) => CloseChat();
        ChatHost.SetupRequested += (_, _) => ChatSetupRequested?.Invoke(this, EventArgs.Empty);

        // This is what makes the notch grow as you type into it: the panel says
        // its content changed size, and the notch animates to the new height.
        ChatHost.ContentSizeChanged += (_, _) => FitToChat();
    }

    /// <summary>Opens the chat if it is closed, folds it away if it is open.</summary>
    public void ToggleChat()
    {
        if (IsChatOpen)
        {
            CloseChat();
        }
        else
        {
            OpenChat();
        }
    }

    /// <summary>
    /// Unfolds the notch into the chat. The notch does not become a window: it
    /// stays the same object pinned to the same edge and simply grows, which is
    /// what keeps Metis in one findable place.
    /// </summary>
    public void OpenChat()
    {
        if (IsChatOpen)
        {
            FocusChat();
            return;
        }

        // A trace in progress owns the notch, and taking the tools away
        // mid-gesture would strand the user holding a pen. The first-run panel
        // owns it for a harder reason: nothing in Metis is meant to be reachable
        // until it is done, and a chat opened over the top of it would be.
        if (IsTracing || IsAuthOpen)
        {
            return;
        }

        DebugLog?.Invoke("Notch chat opening.");
        IsChatOpen = true;
        _retractTimer.Stop();
        _pressed = false;
        Visibility = Visibility.Visible;

        SetThinking(false);
        StepPips.Visibility = Visibility.Collapsed;

        // Released before assigning, because an animation holding a property at
        // its end value ignores a plain assignment: the pill row stayed visible
        // behind the chat every time after the first without this.
        Animate(NotchContent, OpacityProperty, 0, Fade, null);
        Animate(Grabber, OpacityProperty, 0, Fade, null);
        HoverControls.BeginAnimation(OpacityProperty, null);
        HoverControls.Opacity = 0;

        PositionOverTopEdge();

        ChatHost.BeginAnimation(OpacityProperty, null);
        ChatHost.Visibility = Visibility.Visible;
        ChatHost.Opacity = 0;
        ChatHost.Width = ChatWidth;
        ChatHost.UpdateLayout();

        var target = ChatTargetHeight();
        GrowWindowFor(target);

        Animate(NotchBody, WidthProperty, ChatWidth, Shape,
            new BackEase { Amplitude = 0.16, EasingMode = EasingMode.EaseOut });
        AnimateBodyHeight(target);
        Animate(NotchDrop, TranslateTransform.YProperty, 0, Shape,
            new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(ChatHost, OpacityProperty, 1, Fade, null, beginAfter: 90);

        _isShown = true;
        KeepOnTop?.Invoke();
        FocusChat();
        ReportChatOnceSettled();
    }

    /// <summary>
    /// Says what the chat actually looks like once its entrance has finished.
    /// "Open was called" and "the user can see and click the chat" are
    /// different claims: this panel is animated, layered and topmost, and every
    /// one of those has silently swallowed a surface in this app before without
    /// raising an error. Measuring any earlier just reports the values the
    /// animation starts from.
    /// </summary>
    private void ReportChatOnceSettled()
    {
        if (DebugLog is null)
        {
            return;
        }

        var settled = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        settled.Tick += (_, _) =>
        {
            settled.Stop();
            if (!IsChatOpen)
            {
                return;
            }

            var reachable = false;
            if (ChatHost.ActualWidth > 0 && PresentationSource.FromVisual(ChatHost) is not null)
            {
                var centre = ChatHost.PointToScreen(
                    new System.Windows.Point(ChatHost.ActualWidth / 2, ChatHost.ActualHeight / 2));
                reachable = WindowFromPoint(new PointStruct
                {
                    X = (int)Math.Round(centre.X),
                    Y = (int)Math.Round(centre.Y)
                }) == Handle;
            }

            DebugLog?.Invoke(
                $"Notch chat settled: window=({Left:0},{Top:0}) {Width:0}x{Height:0} " +
                $"body={NotchBody.ActualWidth:0}x{NotchBody.ActualHeight:0} dropY={NotchDrop.Y:0} " +
                $"panel={ChatHost.ActualWidth:0}x{ChatHost.ActualHeight:0} " +
                $"panelOpacity={ChatHost.Opacity:0.00} clickable={reachable} " +
                $"keyboard={ChatHost.IsKeyboardFocusWithin}");
        };

        settled.Start();
    }

    /// <summary>
    /// Folds the chat back into the notch. It folds rather than vanishing, so
    /// the user is shown where it went and where to find it again.
    /// </summary>
    public void CloseChat()
    {
        if (!IsChatOpen)
        {
            return;
        }

        DebugLog?.Invoke("Notch chat closing.");
        IsChatOpen = false;
        ChatHost.CloseMenus();
        ReleaseKeyboard();

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };

        fade.Completed += (_, _) =>
        {
            // The chat may have been reopened during the fade; collapsing then
            // would take it away the moment it was asked for.
            if (!IsChatOpen)
            {
                ChatHost.Visibility = Visibility.Collapsed;
            }
        };

        ChatHost.BeginAnimation(OpacityProperty, fade);

        AnimateBodyHeight(ExpandedHeight);
        Tuck();
        ShrinkWindowAfterFold();
    }

    // =========================== The first run ===========================

    /// <summary>True while the notch is showing the sign-in or welcome panel.</summary>
    public bool IsAuthOpen { get; private set; }

    /// <summary>The first-run panel, for the host to wire to the runtime.</summary>
    public NotchAuth Auth => AuthHost;

    /// <summary>
    /// Connects the first-run panel to the notch's geometry, the same way
    /// ConnectChat does for the chat.
    /// </summary>
    public void ConnectAuth()
    {
        AuthHost.ContentSizeChanged += (_, _) => FitToAuth();
    }

    /// <summary>
    /// Unfolds the notch into the sign-in panel.
    ///
    /// This deliberately does not check IsTracing the way OpenChat does. There
    /// is nothing to interrupt: this runs before anything else in Metis is
    /// visible, which is the whole point of the gate.
    /// </summary>
    public void OpenAuth()
    {
        if (IsAuthOpen)
        {
            FocusAuth();
            return;
        }

        DebugLog?.Invoke("Notch first-run panel opening.");
        IsAuthOpen = true;
        _retractTimer.Stop();
        _pressed = false;
        Visibility = Visibility.Visible;

        SetThinking(false);
        StepPips.Visibility = Visibility.Collapsed;

        // Released before assigning, for the same reason OpenChat does it: an
        // animation holding a property at its end value ignores an assignment.
        Animate(NotchContent, OpacityProperty, 0, Fade, null);
        Animate(Grabber, OpacityProperty, 0, Fade, null);
        HoverControls.BeginAnimation(OpacityProperty, null);
        HoverControls.Opacity = 0;

        PositionOverTopEdge();

        AuthHost.BeginAnimation(OpacityProperty, null);
        AuthHost.Visibility = Visibility.Visible;
        AuthHost.Opacity = 0;
        AuthHost.Width = AuthWidth;
        AuthHost.UpdateLayout();

        var target = AuthTargetHeight();
        GrowWindowFor(target);

        Animate(NotchBody, WidthProperty, AuthWidth, Shape,
            new BackEase { Amplitude = 0.16, EasingMode = EasingMode.EaseOut });
        AnimateBodyHeight(target);
        Animate(NotchDrop, TranslateTransform.YProperty, 0, Shape,
            new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(AuthHost, OpacityProperty, 1, Fade, null, beginAfter: 90);

        _isShown = true;
        KeepOnTop?.Invoke();
        FocusAuth();
    }

    /// <summary>Folds the first-run panel away once it has nothing left to ask.</summary>
    public void CloseAuth()
    {
        if (!IsAuthOpen)
        {
            return;
        }

        DebugLog?.Invoke("Notch first-run panel closing.");
        IsAuthOpen = false;
        ReleaseKeyboard();

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };

        fade.Completed += (_, _) =>
        {
            if (!IsAuthOpen)
            {
                AuthHost.Visibility = Visibility.Collapsed;
            }
        };

        AuthHost.BeginAnimation(OpacityProperty, fade);

        // Back to the resting width as well as the resting height. The chat
        // never has to do this because it closes to the same width it opened
        // from; the auth panel does not.
        Animate(NotchBody, WidthProperty, TuckedWidth, Shape,
            new CubicEase { EasingMode = EasingMode.EaseOut });
        AnimateBodyHeight(ExpandedHeight);
        Tuck();
        ShrinkWindowAfterFold();
    }

    private void FocusAuth()
    {
        var current = GetForegroundWindow();
        if (current != Handle)
        {
            _previousForeground = current;
        }

        SetActivatable(true);
        Activate();
        SetForegroundWindow(Handle);
        AuthHost.FocusFirstField();
    }

    private void FitToAuth()
    {
        if (!IsAuthOpen)
        {
            return;
        }

        var target = AuthTargetHeight();
        if (Math.Abs(target - _bodyHeightTarget) < 0.5)
        {
            return;
        }

        GrowWindowFor(target);
        AnimateBodyHeight(target);
        ShrinkWindowAfterFold();
    }

    private double AuthTargetHeight() =>
        Math.Min(AuthHost.MeasureDesiredHeight(AuthWidth), ChatMaxHeight);

    /// <summary>
    /// Lets the notch take the keyboard, which it normally refuses. The window
    /// carries WS_EX_NOACTIVATE so that narrating an activity never steals focus
    /// from the user's work; that flag also makes it impossible to type into,
    /// so it is lifted for exactly as long as the chat is open.
    /// </summary>
    private void FocusChat()
    {
        var current = GetForegroundWindow();
        if (current != Handle)
        {
            _previousForeground = current;
        }

        SetActivatable(true);
        Activate();
        SetForegroundWindow(Handle);
        ChatHost.FocusComposer();
    }

    /// <summary>
    /// Hands the keyboard back to whatever had it before the chat opened, and
    /// restores the notch's refusal to be activated.
    /// </summary>
    private void ReleaseKeyboard()
    {
        SetActivatable(false);

        if (_previousForeground != nint.Zero && IsWindow(_previousForeground))
        {
            SetForegroundWindow(_previousForeground);
        }

        _previousForeground = nint.Zero;
    }

    private void SetActivatable(bool activatable)
    {
        var handle = Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var style = GetWindowLong(handle, GwlExStyle);
        style = activatable ? style & ~WsExNoActivate : style | WsExNoActivate;
        SetWindowLong(handle, GwlExStyle, style);
    }

    /// <summary>
    /// Re-measures the chat and animates the notch to fit it. This runs on every
    /// keystroke that changes the composer's shape and on every message, so it
    /// does nothing when the height has not actually moved — otherwise each
    /// character would restart a 420ms animation and the panel would never
    /// settle.
    /// </summary>
    private void FitToChat()
    {
        if (!IsChatOpen)
        {
            return;
        }

        var target = ChatTargetHeight();
        if (Math.Abs(target - _bodyHeightTarget) < 0.5)
        {
            return;
        }

        GrowWindowFor(target);
        AnimateBodyHeight(target);
        ShrinkWindowAfterFold();
    }

    private double ChatTargetHeight() =>
        Math.Min(ChatHost.MeasureDesiredHeight(ChatWidth), ChatMaxHeight);

    private void AnimateBodyHeight(double target)
    {
        _bodyHeightTarget = target;
        Animate(NotchBody, HeightProperty, target, Shape,
            new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    /// <summary>
    /// Makes the host window big enough before the notch grows into it. A
    /// window cannot clip a little and show the rest: content past its edge is
    /// simply gone, so this always runs ahead of the animation.
    /// </summary>
    private void GrowWindowFor(double bodyHeight)
    {
        var wanted = bodyHeight + WindowSlack;
        if (Height < wanted)
        {
            Height = wanted;
        }
    }

    /// <summary>
    /// Pulls the window back in once the fold has finished. Shrinking it while
    /// the notch is still moving would cut the motion off part-way, and leaving
    /// it large would keep an invisible sheet over the top of the screen
    /// swallowing clicks meant for the user's own windows.
    /// </summary>
    private void ShrinkWindowAfterFold()
    {
        var settle = new DispatcherTimer { Interval = Shape.TimeSpan + TimeSpan.FromMilliseconds(120) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            Height = Math.Max(PillWindowHeight, _bodyHeightTarget + WindowSlack);
            if (!IsChatOpen)
            {
                PositionOverTopEdge();
            }
        };

        settle.Start();
    }

    /// <summary>
    /// Turns the notch into the trace toolbar. Putting the tools here rather
    /// than in a floating palette keeps the screen clear: the user is trying to
    /// look at their own work, and a second window would sit on top of it.
    /// </summary>
    public void ShowTraceTools(TraceTool active)
    {
        // The notch can only be one thing at a time, and a trace is a gesture
        // already in progress, so the chat gives way to it rather than the
        // other way round.
        CloseChat();

        IsTracing = true;
        _retractTimer.Stop();
        Visibility = Visibility.Visible;

        SetThinking(false);
        StepPips.Visibility = Visibility.Collapsed;
        Animate(NotchContent, OpacityProperty, 0, Fade, null);
        Animate(Grabber, OpacityProperty, 0, Fade, null);

        // The hover controls are right-aligned and the toolbar is centred, so
        // both on screen at once means the gear sits on top of Ask.
        HoverControls.Visibility = Visibility.Collapsed;
        // Release the exit fade before setting opacity. An animation that ends
        // with FillBehavior.HoldEnd keeps hold of the property, so assigning
        // Opacity = 1 here does nothing while the previous fade-out is still
        // holding it at 0 — which left the toolbar laid out, fully sized, and
        // invisible on every arm after the first.
        TraceTools.BeginAnimation(OpacityProperty, null);
        TraceTools.Visibility = Visibility.Visible;
        TraceTools.Opacity = 1;

        // Sizes must exist before the entrance runs, or the first frame shows
        // the controls at whatever the layout system had last — which on a
        // never-measured panel is nothing at all.
        TraceTools.UpdateLayout();

        HighlightActiveTool(active);
        StaggerToolsIn();

        // The notch counts as shown while the toolbar is up. Leaving this false
        // told every hover handler the notch was still resting, and they act on
        // that by shrinking it back down.
        _isShown = true;

        // Wide enough for six controls, and it stays fully out so the tools are
        // reachable without hunting for the notch.
        Animate(NotchBody, WidthProperty, ToolbarWidth, Shape, new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut });
        AnimateBodyHeight(ExpandedHeight);
        Animate(NotchDrop, TranslateTransform.YProperty, 0, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });

        // Must come after the trace surface has been shown, or that surface
        // sits on top and the toolbar cannot be clicked at all.
        LiftAboveOverlays();
    }

    /// <summary>
    /// Checks that the toolbar is genuinely on screen once its entrance has
    /// settled, and describes what it found when it is not. "The call was made"
    /// and "the user can see the tools" are different claims, and only the
    /// second one matters — this toolbar has been silently invisible before,
    /// once because an animation threw and once because another window covered
    /// it, and neither showed up as an error.
    /// </summary>
    public bool VerifyToolbarVisible(out string report)
    {
        var reachable = ToolsAreReachable();

        report = $"visible={TraceTools.Visibility == Visibility.Visible} " +
                 $"panelOpacity={TraceTools.Opacity:0.00} " +
                 $"bodyWidth={NotchBody.ActualWidth:0} dropY={NotchDrop.Y:0} " +
                 $"toolWidth={ToolFreehand.ActualWidth:0} toolOpacity={ToolFreehand.Opacity:0.00} " +
                 $"windowVisible={Visibility == Visibility.Visible} windowOpacity={Opacity:0.00} " +
                 $"clickable={reachable}";

        return reachable
            && TraceTools.Visibility == Visibility.Visible
            && TraceTools.Opacity > 0.9
            && ToolFreehand.ActualWidth > 1
            && ToolFreehand.Opacity > 0.9
            && NotchBody.ActualWidth >= ToolbarWidth - 1
            && NotchDrop.Y > -2
            && Visibility == Visibility.Visible;
    }

    /// <summary>
    /// Brings the tools in one after another rather than all at once. The
    /// stagger reads as a toolbar unfolding, and it also draws the eye left to
    /// right across the controls in the order they are meant to be used.
    /// </summary>
    private void StaggerToolsIn()
    {
        Border[] controls = [ToolFreehand, ToolRectangle, ToolFullScreen, ToolDivider, ToolAsk, ToolCancel];

        for (var index = 0; index < controls.Length; index++)
        {
            var control = controls[index];
            control.Opacity = 0;

            // A fresh transform per run. Reusing whatever is already there risks
            // animating a frozen instance, which throws — and a Style setter
            // hands every element the same frozen object.
            var scale = new ScaleTransform(0.6, 0.6);
            control.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            control.RenderTransform = scale;

            var begin = TimeSpan.FromMilliseconds(40 * index);

            control.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    BeginTime = begin,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(260))
            {
                BeginTime = begin,
                EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
    }

    /// <summary>
    /// Marks which tool is in the user's hand. The colour is animated rather
    /// than swapped so switching tools reads as a change of state, not a
    /// repaint.
    /// </summary>
    public void HighlightActiveTool(TraceTool active)
    {
        foreach (var (tool, element) in new[]
                 {
                     (TraceTool.Freehand, ToolFreehand),
                     (TraceTool.Rectangle, ToolRectangle),
                     (TraceTool.FullScreen, ToolFullScreen)
                 })
        {
            var target = tool == active
                ? MediaColor.FromArgb(0xE0, 0x0A, 0x7C, 0xFF)
                : MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);

            if (element.Background is not SolidColorBrush { IsFrozen: false })
            {
                element.Background = new SolidColorBrush(
                    (element.Background as SolidColorBrush)?.Color ?? MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            }

            if (element.Background is SolidColorBrush { IsFrozen: false } brush)
            {
                brush.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new ColorAnimation(target, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                element.Background = new SolidColorBrush(target);
            }
        }
    }

    /// <summary>Returns the notch to normal once tracing is over.</summary>
    public void HideTraceTools()
    {
        if (!IsTracing)
        {
            return;
        }

        IsTracing = false;
        HoverControls.Visibility = Visibility.Visible;

        // Same reason as the toolbar: the hover fade holds this property, so it
        // has to be released before a plain assignment means anything.
        HoverControls.BeginAnimation(OpacityProperty, null);
        HoverControls.Opacity = 0;

        // Exits run faster than entrances, and without the stagger: leaving
        // should feel decisive where arriving felt considered. Collapsing on
        // completion rather than immediately is what lets the fade actually be
        // seen — collapsing in the same breath skipped straight to the end.
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };

        fade.Completed += (_, _) =>
        {
            // A new trace may have started during the fade; collapsing then
            // would take the toolbar away the moment it was asked for.
            if (!IsTracing)
            {
                TraceTools.Visibility = Visibility.Collapsed;
            }
        };

        TraceTools.BeginAnimation(OpacityProperty, fade);
        Tuck();
    }

    /// <summary>
    /// Where this window reports what it saw. Set by the host so notch input
    /// can be traced without the window taking a dependency on logging.
    /// </summary>
    public Action<string>? DebugLog { get; set; }

    /// <summary>
    /// Asks the host to push Metis's whole always-on-top stack back to the top,
    /// in its intended order. Called at the moments the notch changes shape,
    /// rather than leaving it to the next scheduled pass, so the notch is never
    /// seen appearing from behind another window.
    /// </summary>
    public Action? KeepOnTop { get; set; }

    /// <summary>
    /// Whether the toolbar can actually be clicked, by asking Windows which
    /// window owns the pixels the tools are drawn on. Being visible is not the
    /// same as being reachable: the full-screen trace surface is topmost too,
    /// and when it wins the z-order it covers the notch and takes every click
    /// while the toolbar carries on looking perfectly fine.
    /// </summary>
    private bool ToolsAreReachable()
    {
        if (ToolFreehand.ActualWidth <= 0 || PresentationSource.FromVisual(ToolFreehand) is null)
        {
            return false;
        }

        var self = Handle;
        if (self == nint.Zero)
        {
            return false;
        }

        var centre = ToolFreehand.PointToScreen(new System.Windows.Point(
            ToolFreehand.ActualWidth / 2,
            ToolFreehand.ActualHeight / 2));

        return WindowFromPoint(new PointStruct
        {
            X = (int)Math.Round(centre.X),
            Y = (int)Math.Round(centre.Y)
        }) == self;
    }

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(PointStruct point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// The screen rectangle each tool actually occupies. Working these out by
    /// hand from the layout constants is guesswork, and guessing wrong looks
    /// exactly like the buttons being unclickable.
    /// </summary>
    public string DescribeToolRects()
    {
        var parts = new List<string>();
        foreach (var (name, element) in new (string, FrameworkElement)[]
                 {
                     ("freehand", ToolFreehand),
                     ("rectangle", ToolRectangle),
                     ("fullscreen", ToolFullScreen),
                     ("ask", ToolAsk),
                     ("cancel", ToolCancel)
                 })
        {
            if (element.ActualWidth <= 0 || PresentationSource.FromVisual(element) is null)
            {
                parts.Add($"{name}=unmeasured");
                continue;
            }

            var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = element.PointToScreen(
                new System.Windows.Point(element.ActualWidth, element.ActualHeight));
            parts.Add(
                $"{name}=({topLeft.X:0},{topLeft.Y:0})-({bottomRight.X:0},{bottomRight.Y:0}) " +
                $"centre({(topLeft.X + bottomRight.X) / 2:0},{(topLeft.Y + bottomRight.Y) / 2:0})");
        }

        return string.Join("  ", parts);
    }

    private void TraceTool_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DebugLog?.Invoke($"Notch tool clicked: {(sender as FrameworkElement)?.Name}");
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string name } && Enum.TryParse<TraceTool>(name, out var tool))
        {
            HighlightActiveTool(tool);
            TraceToolPicked?.Invoke(this, tool);
        }
    }

    private void TraceAsk_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        TraceConfirmed?.Invoke(this, EventArgs.Empty);
    }

    private void TraceCancel_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DebugLog?.Invoke("Notch cancel clicked.");
        e.Handled = true;
        TraceCancelled?.Invoke(this, EventArgs.Empty);
    }

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
            PrimeTraceTools();
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
    /// Measures the toolbar once at startup. A collapsed panel is never laid
    /// out, so the first time it was shown the controls could be composited
    /// before their sizes existed — which is why the buttons appeared the first
    /// time as bare glyphs with no chip behind them and no fixed shape. Paying
    /// for that layout pass while nothing is on screen means the first trace
    /// looks exactly like every one after it.
    /// </summary>
    private void PrimeTraceTools()
    {
        // Visibility only. Touching Opacity here would be one more thing that
        // can leave the panel transparent, and the panel is collapsed for the
        // whole of this method anyway, so nothing can be seen regardless.
        TraceTools.Visibility = Visibility.Visible;
        TraceTools.UpdateLayout();
        TraceTools.Visibility = Visibility.Collapsed;
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
        if (IsTracing)
        {
            // The toolbar is in use; an activity update must not take the
            // tools out from under the user's cursor.
            return;
        }

        if (IsPanelOpen)
        {
            // The chat carries the same narration on its own status line, so
            // the notch has nowhere to put this and nothing to add. Resizing
            // the body here would collapse the open panel mid-sentence.
            return;
        }

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
        AnimateBodyHeight(ExpandedHeight);
        Animate(NotchDrop, TranslateTransform.YProperty, 0, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(NotchContent, OpacityProperty, 1, Fade, null, beginAfter: _isShown ? 0 : 120);
        Animate(Grabber, OpacityProperty, 0, Fade, null);
        _isShown = true;
        KeepOnTop?.Invoke();
    }

    /// <summary>
    /// Returns the notch to its resting sliver. It never leaves the screen: a
    /// visible handle is what makes the notch something the user can reach for
    /// at any time, rather than a thing that only exists while Metis is busy.
    /// </summary>
    public void Tuck()
    {
        if (IsTracing || IsPanelOpen)
        {
            // The toolbar is the only way to finish a trace, so the notch stays
            // out for as long as the user is marking something. Retracting it
            // mid-gesture takes the controls away exactly when they are needed.
            // The open chat holds it out for the same reason.
            return;
        }

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
        if (IsPanelOpen)
        {
            // The pointer is inside the chat, not on a resting notch, so there
            // is nothing to reveal and nothing to peek.
            return;
        }

        _hovered = true;
        Animate(HoverControls, OpacityProperty, 1, Fade, null);

        // Never peek while the toolbar is up. The peek shrinks the notch to its
        // resting size, and the pointer arriving here is the user reaching for a
        // tool — so the controls were pulled out from under them at the exact
        // moment they went to click one.
        if (!_isShown && !IsTracing)
        {
            // Peek further out on hover so the notch invites the pull.
            Animate(NotchBody, WidthProperty, 152, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });
            Animate(NotchDrop, TranslateTransform.YProperty, -8, Shape, new CubicEase { EasingMode = EasingMode.EaseOut });
        }
    }

    private void Notch_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (IsPanelOpen)
        {
            return;
        }

        _hovered = false;
        _pressed = false;
        Animate(HoverControls, OpacityProperty, 0, Fade, null);
        if (!_isShown && !IsTracing)
        {
            Tuck();
        }
    }

    private void Notch_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsPanelOpen)
        {
            // Every click inside the chat bubbles up to the body. Reading them
            // as presses on the notch would make typing into the composer close
            // the very panel being typed into.
            return;
        }

        if (IsTracing)
        {
            // Only while the toolbar is up, and only the element that was hit:
            // enough to tell "the click missed the button" from "the button
            // ignored the click", without logging every idle press.
            DebugLog?.Invoke(
                $"Notch press over '{(e.OriginalSource as FrameworkElement)?.Name ?? e.OriginalSource?.GetType().Name}'.");
        }

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
        if (IsPanelOpen || !_pressed || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
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
        if (IsPanelOpen || !_pressed || IsTracing)
        {
            _pressed = false;
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

        // The window is only as wide as the shape the notch is currently in.
        // Its transparent margin is still a solid sheet as far as the mouse is
        // concerned, so an oversized window quietly eats clicks aimed at the
        // windows underneath it.
        Width = Math.Min(
            IsChatOpen ? ChatWindowWidth : IsAuthOpen ? AuthWindowWidth : PillWindowWidth,
            primary);
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

    /// <summary>
    /// Lifts the notch above the other topmost windows. The trace surface
    /// covers the whole desktop, including the strip the notch sits in, so
    /// without this it would swallow every click aimed at the toolbar.
    /// </summary>
    /// <summary>This window's handle, for callers that need to order z against it.</summary>
    public nint Handle => new WindowInteropHelper(this).Handle;

    public void LiftAboveOverlays()
    {
        const int hwndTopmost = -1;
        const uint noMove = 0x0002, noSize = 0x0001, noActivate = 0x0010;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            SetWindowPos(handle, hwndTopmost, 0, 0, 0, 0, noMove | noSize | noActivate);
        }
    }
}
