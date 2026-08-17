using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Chooses which mark fits a target, from its shape alone.
///
/// This is the single rule both directions share: a region the user traced and
/// a region Metis answers about are marked the same way, because they run
/// through the same function. Keeping it here — pure, with no dependency on the
/// overlay — is what stops the two drifting apart.
/// </summary>
public static class MarkGeometry
{
    /// <summary>Wider than this and a ring no longer fits the target.</summary>
    public const double CapsuleAspect = 1.45;

    /// <summary>Taller than wide by the same margin; also not ring-shaped.</summary>
    public const double PortraitAspect = 1 / CapsuleAspect;

    /// <summary>A span this flat is text, not a control.</summary>
    public const double UnderlineAspect = 4.0;

    /// <summary>Text rows are short. Above this it is a panel, not a line.</summary>
    public const int UnderlineMaxHeight = 40;

    /// <summary>Beyond this share of the screen, brackets replace an outline.</summary>
    public const double BracketAreaShare = 0.18;

    /// <summary>
    /// Picks the form for a target of the given size. <paramref name="screenArea"/>
    /// is the area of the surface it sits on, used only for the bracket test —
    /// "large" is meaningful relative to the screen, not in absolute pixels.
    /// </summary>
    public static GuidanceMarkKind ForShape(int width, int height, long screenArea = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return GuidanceMarkKind.FocusRing;
        }

        var aspect = width / (double)height;

        // A flat, wide span is text being read out, not a control to press.
        if (height <= UnderlineMaxHeight && aspect >= UnderlineAspect)
        {
            return GuidanceMarkKind.Underline;
        }

        // Large areas get corner brackets: a full outline reads as a border and
        // visually swallows the content it is meant to draw attention to.
        if (screenArea > 0 && (long)width * height >= screenArea * BracketAreaShare)
        {
            return GuidanceMarkKind.Box;
        }

        return aspect is > CapsuleAspect or < PortraitAspect
            ? GuidanceMarkKind.Capsule
            : GuidanceMarkKind.FocusRing;
    }

    /// <summary>
    /// The form for a hand-traced region. A deliberate trace keeps its lasso —
    /// the user drew that shape and replacing it with a tidy rectangle would
    /// discard what they actually said.
    /// </summary>
    public static GuidanceMarkKind ForTrace(int pointCount) =>
        pointCount >= 3 ? GuidanceMarkKind.Lasso : GuidanceMarkKind.FocusRing;
}
