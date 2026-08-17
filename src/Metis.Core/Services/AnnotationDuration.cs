using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// How long a mark stays on screen before it clears itself.
///
/// Marks used to be held for minutes, because Metis waited for the user to
/// carry out each step and the mark had to survive however long they took. It
/// no longer waits: it explains a step, moves to the next, and re-reads the
/// screen as it goes. So a mark's life is now the time it takes to look at,
/// and a mark that outlives its sentence is pointing at something Metis has
/// stopped talking about — which is worse than no mark, because the user
/// believes it.
/// </summary>
public static class AnnotationDuration
{
    /// <summary>
    /// The standard hold: long enough to follow a sentence to the thing it is
    /// about and still be looking when the next one starts.
    /// </summary>
    public static readonly TimeSpan Standard = TimeSpan.FromSeconds(7);

    /// <summary>
    /// Small targets clear sooner. A ring around one button is read at a
    /// glance, and leaving it up past that turns a pointer into clutter over
    /// the control the user is trying to use.
    /// </summary>
    public static readonly TimeSpan Small = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The share of the screen below which a target counts as small. A control
    /// is small relative to the display it is on, not in absolute pixels.
    /// </summary>
    public const double SmallAreaShare = 0.04;

    /// <summary>
    /// Picks the hold for a resolved annotation.
    /// </summary>
    public static TimeSpan For(ResolvedAnnotation annotation, long screenArea = 0)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return For(annotation.Scope, annotation.Width, annotation.Height, screenArea);
    }

    public static TimeSpan For(AnnotationScope scope, int width, int height, long screenArea = 0)
    {
        // A whole window or a region is a lot to take in, and an arrow has to
        // be followed from the edge of the screen, so these keep the full hold
        // whatever their measured size says.
        if (scope is AnnotationScope.Window or AnnotationScope.Region or AnnotationScope.Offscreen)
        {
            return Standard;
        }

        if (width <= 0 || height <= 0)
        {
            return Standard;
        }

        // Without a screen to compare against, fall back to judging the target
        // on its own: anything under a few hundred pixels a side is a control.
        if (screenArea <= 0)
        {
            return width <= 320 && height <= 320 ? Small : Standard;
        }

        var share = (long)width * height / (double)screenArea;
        return share <= SmallAreaShare ? Small : Standard;
    }
}
