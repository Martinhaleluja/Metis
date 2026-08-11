using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Metis.App.Windows;

/// <summary>
/// Attaches a window to the notch. Anchored windows are fixed under the notch
/// and cannot be dragged: the notch is the one stable landmark in the interface,
/// so its windows stay where the user last saw them rather than scattering
/// across the desktop. They unfurl downward out of the notch when opened and
/// fold back up into it when dismissed.
/// </summary>
public static class NotchAnchor
{
    private static readonly Duration Open = new(TimeSpan.FromMilliseconds(360));
    private static readonly Duration Close = new(TimeSpan.FromMilliseconds(240));

    /// <summary>
    /// Centres the window beneath the notch. Called on every open because the
    /// screen resolution, DPI, or primary display can change while Metis runs.
    /// </summary>
    public static void Position(Window window, double anchorBottom)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Left = (SystemParameters.PrimaryScreenWidth - window.Width) / 2;
        window.Top = anchorBottom + 6;
    }

    /// <summary>
    /// Grows the window out of the notch. The transform origin sits at the top
    /// centre so the motion reads as unfurling from the notch's mouth, and the
    /// horizontal scale starts near the notch's own width rather than at zero.
    /// </summary>
    public static void AnimateOpen(FrameworkElement shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var scale = PrepareTransform(shell);

        shell.Opacity = 0;
        scale.ScaleX = 0.22;
        scale.ScaleY = 0.02;

        shell.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(180))));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, Open) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, Open) { EasingFunction = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>
    /// Folds the window back into the notch, invoking <paramref name="onFolded"/>
    /// once it has actually disappeared so the caller hides the window only
    /// after the motion completes rather than cutting it short.
    /// </summary>
    public static void AnimateClose(FrameworkElement shell, Action onFolded)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(onFolded);
        var scale = PrepareTransform(shell);

        var collapse = new DoubleAnimation(0.02, Close)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        collapse.Completed += (_, _) => onFolded();

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.22, Close) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, collapse);
        shell.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, Close) { BeginTime = TimeSpan.FromMilliseconds(80) });
    }

    private static ScaleTransform PrepareTransform(FrameworkElement shell)
    {
        shell.RenderTransformOrigin = new System.Windows.Point(0.5, 0);
        if (shell.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            shell.RenderTransform = scale;
        }

        return scale;
    }
}
