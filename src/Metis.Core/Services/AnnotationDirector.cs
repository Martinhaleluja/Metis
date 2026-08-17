using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Decides how an annotation is drawn, from what it is about and how big it
/// really turned out to be.
///
/// The division of labour matters more than any individual rule here. The model
/// says what it is explaining; this decides what that looks like. So a sentence
/// about a terminal marks the whole terminal, a sentence about a phrase marks
/// that phrase, and neither outcome depends on a model choosing correctly
/// between the words "ring" and "bracket" — which, when it was asked to, it
/// mostly did not.
///
/// It is pure on purpose: every rule below can be checked without a desktop,
/// and the overlay never has to know a scope exists.
/// </summary>
public static class AnnotationDirector
{
    /// <summary>
    /// Past this share of the screen, a target the model called a control is
    /// not one. Something this size is a panel or a window, and ringing it
    /// would draw a circle most of the way round the display.
    /// </summary>
    public const double ControlAreaCeiling = 0.25;

    /// <summary>
    /// Below this share, something the model called a region is a single
    /// control that happens to sit in one. Bracketing it wastes the strongest
    /// mark on the smallest target.
    /// </summary>
    public const double RegionAreaFloor = 0.01;

    /// <summary>
    /// A region this large is bracketed rather than outlined, for the reason
    /// brackets exist: a full outline at this size reads as a window border and
    /// the eye stops seeing it as a mark.
    /// </summary>
    public const double RegionBracketShare = 0.30;

    /// <summary>
    /// Picks the mark for a target of this scope and size.
    /// <paramref name="screenArea"/> is the area of the display the target sits
    /// on; "large" only means anything relative to the screen it is on.
    /// </summary>
    public static GuidanceMarkKind Choose(AnnotationScope scope, int width, int height, long screenArea = 0) =>
        scope switch
        {
            // The whole window is the subject, so the mark has to contain the
            // whole window. Brackets do that without laying a border over the
            // title bar and edges the user is being shown.
            AnnotationScope.Window => GuidanceMarkKind.Bracket,

            // Under the words, never around them. A box around a line of prose
            // says "this area of the screen"; the sentence being spoken is
            // about the words themselves.
            AnnotationScope.TextSpan => GuidanceMarkKind.Underline,

            AnnotationScope.Path => GuidanceMarkKind.Stroke,

            // Nothing on screen to mark, so the honest mark is a direction.
            AnnotationScope.Offscreen => GuidanceMarkKind.Arrow,

            AnnotationScope.Region => ChooseForRegion(width, height, screenArea),

            _ => ChooseForControl(width, height, screenArea)
        };

    /// <summary>
    /// A control gets the shape rules it always had, with one exception: it can
    /// never become an underline.
    ///
    /// Those rules predate scopes, so they had to guess from proportions alone
    /// whether a flat wide rectangle was a line of text — and a toolbar button
    /// or a wide input field trips that test constantly. Now that the subject
    /// is known, the guess is not only unnecessary but wrong: underlining a
    /// button says "these words", when what was meant is "press this".
    /// </summary>
    private static GuidanceMarkKind ChooseForControl(int width, int height, long screenArea)
    {
        var chosen = MarkGeometry.ForShape(width, height, screenArea);
        return chosen == GuidanceMarkKind.Underline ? GuidanceMarkKind.Capsule : chosen;
    }

    private static GuidanceMarkKind ChooseForRegion(int width, int height, long screenArea)
    {
        if (width <= 0 || height <= 0)
        {
            return GuidanceMarkKind.Box;
        }

        return screenArea > 0 && (long)width * height >= screenArea * RegionBracketShare
            ? GuidanceMarkKind.Bracket
            : GuidanceMarkKind.Box;
    }

    /// <summary>
    /// Corrects a scope against the geometry that was actually found.
    ///
    /// A model looking at a screenshot names what it means well and judges size
    /// poorly, and the resolver frequently returns a rectangle far bigger or
    /// smaller than the model imagined — snapping "the run button" to a toolbar,
    /// or "the output panel" to one label inside it. Marking those at the
    /// claimed scope produces the two worst outcomes there are: a ring around
    /// half the display, and brackets around a single word. The rectangle is
    /// the evidence, so the rectangle wins.
    /// </summary>
    public static AnnotationScope Reconcile(AnnotationScope scope, int width, int height, long screenArea)
    {
        // A path is defined by its points and an offscreen target has no
        // rectangle to argue with, so neither can be corrected by one.
        if (scope is AnnotationScope.Path or AnnotationScope.Offscreen)
        {
            return scope;
        }

        if (screenArea <= 0 || width <= 0 || height <= 0)
        {
            return scope;
        }

        var share = (long)width * height / (double)screenArea;

        // Text stays text at any size: a wrapped paragraph is still the words,
        // and an underline under all of it still says the right thing.
        if (scope == AnnotationScope.TextSpan)
        {
            return scope;
        }

        if (scope == AnnotationScope.Control && share >= ControlAreaCeiling)
        {
            return AnnotationScope.Region;
        }

        if (scope == AnnotationScope.Region && share <= RegionAreaFloor)
        {
            return AnnotationScope.Control;
        }

        return scope;
    }

    /// <summary>
    /// The whole decision in one call: reconcile the claimed scope against the
    /// real rectangle, then choose the mark for the scope that survives.
    /// </summary>
    public static ResolvedAnnotation Resolve(
        AnnotationScope scope,
        int screenX,
        int screenY,
        int width,
        int height,
        string? label,
        AnnotationSource source,
        long screenArea = 0,
        IReadOnlyList<GuidancePoint>? points = null)
    {
        var settled = Reconcile(scope, width, height, screenArea);
        return new ResolvedAnnotation(
            settled,
            Choose(settled, width, height, screenArea),
            screenX,
            screenY,
            width,
            height,
            label,
            source,
            points);
    }
}
