using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Metis.Core.Models;

namespace Metis.Windows;

/// <summary>
/// Crops a capture down to the area the user traced.
///
/// This does two jobs at once. The answer gets sharper, because the model is
/// looking at what was circled instead of hunting for it in a full desktop. And
/// the request gets cheaper, because a cropped image carries far fewer tokens
/// than a whole screen — the same lever that makes hosted inference affordable.
/// </summary>
public static class ScreenCaptureCropper
{
    /// <summary>
    /// A crop below this many pixels on a side is upscaled to it, so a small
    /// traced control does not arrive as an unreadable thumbnail.
    /// </summary>
    private const int MinimumEdge = 320;

    /// <summary>
    /// Returns a capture cropped to the region, or the original when the crop
    /// would be meaningless. Never throws: a failed crop falls back to the full
    /// screenshot, which still answers the question.
    /// </summary>
    public static ScreenCapture Crop(ScreenCapture capture, ScreenRegion region, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(region);

        if (!region.IsUsable || capture.ImageBytes.Length == 0)
        {
            return capture;
        }

        try
        {
            using var source = new MemoryStream(capture.ImageBytes, false);
            using var image = Image.FromStream(source);

            // The region is in normalized capture space, so it maps onto the
            // encoded image regardless of how much that image was scaled down.
            var x = (int)Math.Round(region.NormalizedX / 1000d * image.Width);
            var y = (int)Math.Round(region.NormalizedY / 1000d * image.Height);
            var width = (int)Math.Round(region.NormalizedWidth / 1000d * image.Width);
            var height = (int)Math.Round(region.NormalizedHeight / 1000d * image.Height);

            x = Math.Clamp(x, 0, Math.Max(0, image.Width - 2));
            y = Math.Clamp(y, 0, Math.Max(0, image.Height - 2));
            width = Math.Clamp(width, 2, image.Width - x);
            height = Math.Clamp(height, 2, image.Height - y);

            // Cropping to nearly the whole screen saves nothing and only risks
            // clipping context the answer needs.
            if ((long)width * height > (long)image.Width * image.Height * 0.95)
            {
                return capture;
            }

            var scale = Math.Max(1d, Math.Min(
                MinimumEdge / (double)Math.Max(1, width),
                MinimumEdge / (double)Math.Max(1, height)));
            var targetWidth = (int)Math.Round(width * scale);
            var targetHeight = (int)Math.Round(height * scale);

            using var cropped = new Bitmap(targetWidth, targetHeight);
            using (var graphics = Graphics.FromImage(cropped))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(
                    image,
                    new Rectangle(0, 0, targetWidth, targetHeight),
                    new Rectangle(x, y, width, height),
                    GraphicsUnit.Pixel);
            }

            using var output = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec => codec.MimeType == "image/jpeg");
            if (encoder is null)
            {
                return capture;
            }

            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 82L);
            cropped.Save(output, encoder, parameters);

            var bytes = output.ToArray();
            log?.Invoke(
                $"Cropped capture to the traced region: {width}x{height} source pixels, " +
                $"{capture.ImageBytes.Length / 1024} KiB down to {bytes.Length / 1024} KiB.");

            // The crop keeps the region's own screen bounds so any coordinate
            // the model returns still maps back to the right place on screen.
            var sourceWidth = capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;
            var sourceHeight = capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;

            return capture with
            {
                ImageBytes = bytes,
                ImageMimeType = "image/jpeg",
                Width = targetWidth,
                Height = targetHeight,
                ScreenLeft = capture.ScreenLeft + (int)Math.Round(region.NormalizedX / 1000d * sourceWidth),
                ScreenTop = capture.ScreenTop + (int)Math.Round(region.NormalizedY / 1000d * sourceHeight),
                SourceWidth = (int)Math.Round(region.NormalizedWidth / 1000d * sourceWidth),
                SourceHeight = (int)Math.Round(region.NormalizedHeight / 1000d * sourceHeight)
            };
        }
        catch (Exception exception)
        {
            // A failed crop must not cost the user their answer.
            log?.Invoke($"The traced region could not be cropped, so the full screen was sent. {exception.Message}");
            return capture;
        }
    }
}
