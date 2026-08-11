using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Windows;

/// <summary>
/// Captures the complete Windows virtual desktop, including every monitor, in
/// one coordinate space. The image is scaled only after capture so normalized
/// model coordinates still map back to the original multi-monitor bounds.
/// </summary>
public sealed class VirtualDesktopCaptureService : IScreenCaptureService
{
    private const int CloudMaximumWidth = 2560;
    private const int CloudMaximumHeight = 1440;
    private const long CloudJpegQuality = 80L;
    private const int LocalMaximumWidth = 1280;
    private const int LocalMaximumHeight = 720;
    private const long LocalJpegQuality = 68L;
    private const string CaptureMimeType = "image/jpeg";
    private volatile bool _compactLocalProfile;

    public void UseCompactLocalProfile(bool enabled) => _compactLocalProfile = enabled;

    public Task<ScreenCapture?> CaptureActiveWindowAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private ScreenCapture? Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return null;
        }

        using var desktop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(desktop))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size,
                CopyPixelOperation.SourceCopy);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var maximumWidth = _compactLocalProfile ? LocalMaximumWidth : CloudMaximumWidth;
        var maximumHeight = _compactLocalProfile ? LocalMaximumHeight : CloudMaximumHeight;
        var jpegQuality = _compactLocalProfile ? LocalJpegQuality : CloudJpegQuality;
        var scale = Math.Min(
            1d,
            Math.Min(maximumWidth / (double)bounds.Width, maximumHeight / (double)bounds.Height));
        var outputWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        using var resized = scale < 1d
            ? new Bitmap(outputWidth, outputHeight, PixelFormat.Format24bppRgb)
            : null;
        if (resized is not null)
        {
            using var resizedGraphics = Graphics.FromImage(resized);
            resizedGraphics.CompositingQuality = CompositingQuality.HighQuality;
            resizedGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            resizedGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            resizedGraphics.DrawImage(desktop, 0, 0, outputWidth, outputHeight);
        }

        using var stream = new MemoryStream();
        SaveJpeg(resized ?? desktop, stream, jpegQuality);
        return new ScreenCapture(
            stream.ToArray(),
            "Entire Windows desktop",
            outputWidth,
            outputHeight,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            0,
            "Virtual desktop (all monitors)",
            CaptureMimeType);
    }

    private static void SaveJpeg(Image image, Stream stream, long jpegQuality)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .First(candidate => candidate.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);
        image.Save(stream, codec, parameters);
    }
}
