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
    /// <summary>
    /// The ceiling for a capture that has to keep every pixel it can — pointing
    /// at one small control, or explaining a region the user traced.
    /// </summary>
    private const int CloudMaximumWidth = 2560;
    private const int CloudMaximumHeight = 1440;
    private const long CloudJpegQuality = 80L;

    /// <summary>
    /// The ceiling for an ordinary question about the screen.
    ///
    /// This used to be the one above, which on a 1080p desktop meant no
    /// downscale at all: a couple of hundred kilobytes to upload and around
    /// fifteen hundred image tokens for the model to read before it could
    /// begin, on every single turn. Halving each dimension cuts both to roughly
    /// a third, and a screenshot at this size is still comfortably legible —
    /// window titles, menu items and button labels all survive it.
    /// </summary>
    private const int StandardMaximumWidth = 1280;
    private const int StandardMaximumHeight = 720;
    private const long StandardJpegQuality = 75L;

    private const int LocalMaximumWidth = 1280;
    private const int LocalMaximumHeight = 720;
    private const long LocalJpegQuality = 68L;
    private const string CaptureMimeType = "image/jpeg";
    private volatile bool _compactLocalProfile;
    private readonly Func<Rectangle> _readVirtualScreenBounds;
    private readonly Func<IReadOnlyList<ProtectedRegion>> _readProtectedRegions;

    /// <summary>
    /// Creates a capture service reading the live virtual desktop bounds.
    /// </summary>
    /// <param name="readVirtualScreenBounds">
    /// Where the desktop's bounds come from. Injectable so a caller can pin
    /// them to a single reading: <c>SystemInformation.VirtualScreen</c> is
    /// process-global and answers differently once anything establishes DPI
    /// awareness, so code that reads it once to predict a capture and again
    /// inside the capture can legitimately get two different desktops.
    /// </param>
    public VirtualDesktopCaptureService(
        Func<Rectangle>? readVirtualScreenBounds = null,
        Func<IReadOnlyList<ProtectedRegion>>? readProtectedRegions = null)
    {
        _readVirtualScreenBounds = readVirtualScreenBounds ??
                                   (static () => System.Windows.Forms.SystemInformation.VirtualScreen);
        _readProtectedRegions = readProtectedRegions ?? DefaultProtectedRegions;
    }

    public void UseCompactLocalProfile(bool enabled) => _compactLocalProfile = enabled;

    /// <summary>
    /// The applications the user has told Metis never to look at. Read on every
    /// capture rather than held, so a change in Settings takes effect on the
    /// next question rather than the next restart.
    /// </summary>
    public IReadOnlyCollection<string> ExcludedApplications { get; set; } = [];

    /// <summary>
    /// The password box the user is typing into, if anything can tell us. Set by
    /// the runtime once the accessibility service exists, because a password
    /// field is the one protected region Windows does not mark for us.
    /// </summary>
    public Func<ProtectedRegion?>? ReadFocusedPasswordField { get; set; }

    private IReadOnlyList<ProtectedRegion> DefaultProtectedRegions()
    {
        var regions = new List<ProtectedRegion>(
            CaptureGuard.FindProtectedRegions(ExcludedApplications, out _));

        if (ReadFocusedPasswordField?.Invoke() is { } focusedPassword)
        {
            regions.Add(focusedPassword);
        }

        return regions;
    }

    public Task<ScreenCapture?> CaptureActiveWindowAsync(CancellationToken cancellationToken = default) =>
        CaptureActiveWindowAsync(ScreenCaptureDetail.Full, cancellationToken);

    public Task<ScreenCapture?> CaptureActiveWindowAsync(
        ScreenCaptureDetail detail,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(detail, cancellationToken), cancellationToken);

    /// <summary>
    /// The bounds the next capture will cover, read without capturing anything.
    /// Lets work that only needs to know where the desktop is — the
    /// accessibility scan — start at the same moment as the capture rather
    /// than waiting for it to finish.
    /// </summary>
    public ScreenBounds? PeekCaptureBounds()
    {
        var bounds = _readVirtualScreenBounds();
        return bounds.Width <= 1 || bounds.Height <= 1
            ? null
            : new ScreenBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    private ScreenCapture? Capture(ScreenCaptureDetail detail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = _readVirtualScreenBounds();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return null;
        }

        using var desktop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(desktop);
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size,
                CopyPixelOperation.SourceCopy);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                      or System.Runtime.InteropServices.ExternalException)
        {
            // The display DC may be inaccessible if the screen is locked, in a
            // secure desktop prompt, or in a headless test environment.
        }

        // Painted out here, on the full-resolution frame, before anything is
        // scaled or encoded. Doing it at this point means the protected pixels
        // never exist in a buffer that goes on to be uploaded — the redaction is
        // not a filter applied to a finished image, it is a hole in the only
        // copy that is ever made.
        var withheld = ScreenRedaction.Apply(desktop, bounds, _readProtectedRegions());

        cancellationToken.ThrowIfCancellationRequested();
        var (maximumWidth, maximumHeight, jpegQuality) = (_compactLocalProfile, detail) switch
        {
            (true, _) => (LocalMaximumWidth, LocalMaximumHeight, LocalJpegQuality),
            (false, ScreenCaptureDetail.Full) => (CloudMaximumWidth, CloudMaximumHeight, CloudJpegQuality),
            _ => (StandardMaximumWidth, StandardMaximumHeight, StandardJpegQuality)
        };
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
            CaptureMimeType,
            withheld);
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
