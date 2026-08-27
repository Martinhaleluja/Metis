using System.Drawing;
using Metis.Core.Models;

namespace Metis.Windows;

/// <summary>
/// Paints protected parts of a captured frame out of existence.
///
/// Flat black rather than a blur: a blur is a guess about how much detail is
/// safe to leave behind, and it can be wrong. Black says plainly that something
/// was removed, which is also what lets Metis tell the model the picture is
/// incomplete instead of letting it narrate a smear.
/// </summary>
internal static class ScreenRedaction
{
    /// <summary>
    /// Blacks out every protected region that overlaps the captured frame, and
    /// returns how many were actually painted.
    ///
    /// The regions arrive in screen coordinates, which on a multi-monitor
    /// desktop can start at a negative left or top; the frame starts at its own
    /// origin. Translating one into the other is the whole of the arithmetic
    /// here, and getting it wrong would blank the wrong part of the picture, so
    /// each rectangle is clipped to the frame before it is used.
    /// </summary>
    internal static int Apply(
        Bitmap frame,
        Rectangle frameBounds,
        IReadOnlyList<ProtectedRegion>? regions)
    {
        if (regions is not { Count: > 0 })
        {
            return 0;
        }

        var painted = 0;
        using var graphics = Graphics.FromImage(frame);
        foreach (var region in regions)
        {
            var onFrame = Rectangle.Intersect(
                new Rectangle(
                    region.Left - frameBounds.Left,
                    region.Top - frameBounds.Top,
                    region.Width,
                    region.Height),
                new Rectangle(0, 0, frame.Width, frame.Height));

            if (onFrame.Width <= 0 || onFrame.Height <= 0)
            {
                continue;
            }

            graphics.FillRectangle(Brushes.Black, onFrame);
            painted++;
        }

        return painted;
    }
}
