using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Windows;

public sealed class ActiveWindowCaptureService : IScreenCaptureService
{
    private const int DwmExtendedFrameBounds = 9;
    private const int MaximumWidth = 1600;
    private const int MaximumHeight = 1000;
    private const long JpegQuality = 82L;
    private const string CaptureMimeType = "image/jpeg";

    public Task<ScreenCapture?> CaptureActiveWindowAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    private static ScreenCapture? Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = CaptureWindowSelector.GetTargetWindow();
        if (window == nint.Zero || IsIconic(window))
        {
            return null;
        }

        if (DwmGetWindowAttribute(window, DwmExtendedFrameBounds, out var bounds, Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(window, out bounds))
        {
            return null;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 1 || height <= 1)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }
        catch
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scale = Math.Min(1d, Math.Min(MaximumWidth / (double)width, MaximumHeight / (double)height));
        var outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height * scale));

        using var resized = scale < 1d ? new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb) : null;
        if (resized is not null)
        {
            using var resizedGraphics = Graphics.FromImage(resized);
            resizedGraphics.CompositingQuality = CompositingQuality.HighQuality;
            resizedGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            resizedGraphics.DrawImage(bitmap, 0, 0, outputWidth, outputHeight);
        }

        using var stream = new MemoryStream();
        SaveJpeg(resized ?? bitmap, stream);
        return new ScreenCapture(
            stream.ToArray(),
            GetWindowTitle(window),
            outputWidth,
            outputHeight,
            bounds.Left,
            bounds.Top,
            width,
            height,
            window.ToInt64(),
            "GDI fallback",
            CaptureMimeType);
    }

    private static void SaveJpeg(Image image, Stream stream)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .First(candidate => candidate.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
        image.Save(stream, codec, parameters);
    }

    private static string GetWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return "Untitled active window";
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowText(window, builder, builder.Capacity) > 0
            ? builder.ToString()
            : "Untitled active window";
    }

#pragma warning disable CS0649 // user32 and DWM populate this structure.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
#pragma warning restore CS0649

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int valueSize);
}
