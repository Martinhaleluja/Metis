using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Converts between the normalized 0-1000 space a model answers in and the
/// screen pixels a mark is drawn in.
///
/// The model is shown a resized screenshot and answers in a resolution-free
/// space, so every coordinate that comes back has to be projected through the
/// capture it belongs to before it means anything. Keeping that arithmetic here
/// rather than in the runtime is what lets the annotation resolver do the same
/// projection without a desktop attached, and what makes the off-by-a-monitor
/// mistakes checkable in a test.
/// </summary>
public static class CaptureProjection
{
    /// <summary>
    /// The smallest mark worth drawing. A control reported as a few pixels
    /// across is a rounding artefact of the normalized space, not a real
    /// target, and a mark that small reads as a speck of dust on the screen.
    /// </summary>
    public const int MinimumMarkWidth = 28;
    public const int MinimumMarkHeight = 24;

    private static int SourceWidth(ScreenCapture capture) =>
        capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;

    private static int SourceHeight(ScreenCapture capture) =>
        capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;

    /// <summary>The area of the captured surface, for the "is this large?" tests.</summary>
    public static long Area(ScreenCapture capture) =>
        (long)SourceWidth(capture) * SourceHeight(capture);

    public static (int X, int Y) ToScreenPoint(int normalizedX, int normalizedY, ScreenCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return (
            capture.ScreenLeft + (int)Math.Round(normalizedX / 1000d * SourceWidth(capture)),
            capture.ScreenTop + (int)Math.Round(normalizedY / 1000d * SourceHeight(capture)));
    }

    public static (int Width, int Height) ToScreenSize(
        int normalizedWidth,
        int normalizedHeight,
        ScreenCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return (
            Math.Max(MinimumMarkWidth, (int)Math.Round(normalizedWidth / 1000d * SourceWidth(capture))),
            Math.Max(MinimumMarkHeight, (int)Math.Round(normalizedHeight / 1000d * SourceHeight(capture))));
    }

    /// <summary>
    /// Projects a whole target at once: centre point plus extent. A target
    /// without a reported extent gets a modest default box rather than nothing,
    /// so the mark still has a shape to be chosen for it.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ToScreenRect(
        AnnotationTarget target,
        ScreenCapture capture)
    {
        ArgumentNullException.ThrowIfNull(target);
        var (x, y) = ToScreenPoint(target.NormalizedX, target.NormalizedY, capture);
        var (width, height) = target.HasExtent
            ? ToScreenSize(target.NormalizedWidth, target.NormalizedHeight, capture)
            : (64, 64);
        return (x, y, width, height);
    }

    /// <summary>
    /// True when a screen point lies inside the rectangle described by a centre
    /// and an extent. Used to decide whether a real control's bounds and a
    /// model's guess are talking about the same thing.
    /// </summary>
    public static bool Contains(int centreX, int centreY, int width, int height, int pointX, int pointY) =>
        pointX >= centreX - (width / 2) && pointX <= centreX + (width / 2) &&
        pointY >= centreY - (height / 2) && pointY <= centreY + (height / 2);
}
