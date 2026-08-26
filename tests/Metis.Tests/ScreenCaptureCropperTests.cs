using System.Drawing;
using System.Drawing.Imaging;
using Metis.Core.Models;
using Metis.Windows;

namespace Metis.Tests;

public sealed class ScreenCaptureCropperTests
{
    private static byte[] CreateTestJpeg(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.CornflowerBlue);
            g.FillRectangle(Brushes.White, width / 4, height / 4, width / 2, height / 2);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    [Fact]
    public void Crop_returns_original_capture_when_region_is_unusable()
    {
        var capture = new ScreenCapture([1, 2, 3], "Test Window", 1920, 1080);
        var region = new ScreenRegion(0, 0, 0, 0, []);

        var result = ScreenCaptureCropper.Crop(capture, region);

        Assert.Same(capture, result);
    }

    [Fact]
    public void Crop_cuts_out_specified_region_and_preserves_screen_coordinates()
    {
        var imageBytes = CreateTestJpeg(1000, 800);
        var capture = new ScreenCapture(
            imageBytes,
            "Main Window",
            Width: 1000,
            Height: 800,
            ScreenLeft: 100,
            ScreenTop: 50,
            SourceWidth: 1000,
            SourceHeight: 800);

        // Crop 200, 200, 400, 300 in 0-1000 space (which maps to 20% x, 25% y, 40% w, 37.5% h)
        var region = new ScreenRegion(
            NormalizedX: 200,
            NormalizedY: 250,
            NormalizedWidth: 400,
            NormalizedHeight: 300,
            Path: [new GuidancePoint(300, 250), new GuidancePoint(700, 250), new GuidancePoint(700, 550), new GuidancePoint(300, 550)]);

        var cropped = ScreenCaptureCropper.Crop(capture, region);

        Assert.NotEqual(capture.ImageBytes.Length, cropped.ImageBytes.Length);
        Assert.Equal("image/jpeg", cropped.ImageMimeType);
        Assert.Equal(100 + 200, cropped.ScreenLeft); // 100 + (200/1000 * 1000) = 300
        Assert.Equal(50 + 200, cropped.ScreenTop);   // 50 + (250/1000 * 800) = 250
        Assert.Equal(400, cropped.SourceWidth);       // 400/1000 * 1000 = 400
        Assert.Equal(240, cropped.SourceHeight);      // 300/1000 * 800 = 240
    }

    [Fact]
    public void Crop_upscales_tiny_region_to_minimum_edge()
    {
        var imageBytes = CreateTestJpeg(1000, 1000);
        var capture = new ScreenCapture(
            imageBytes,
            "App",
            Width: 1000,
            Height: 1000,
            ScreenLeft: 0,
            ScreenTop: 0,
            SourceWidth: 1000,
            SourceHeight: 1000);

        // Very small region: 50x50 normalized (50x50 pixels)
        var region = new ScreenRegion(
            NormalizedX: 100,
            NormalizedY: 100,
            NormalizedWidth: 50,
            NormalizedHeight: 50,
            Path: [new GuidancePoint(100, 100), new GuidancePoint(150, 100), new GuidancePoint(150, 150), new GuidancePoint(100, 150)]);

        var cropped = ScreenCaptureCropper.Crop(capture, region);

        Assert.True(cropped.Width >= 320, $"Expected width >= 320 but got {cropped.Width}");
        Assert.True(cropped.Height >= 320, $"Expected height >= 320 but got {cropped.Height}");
    }

    [Fact]
    public void Crop_returns_original_capture_when_region_covers_near_full_screen()
    {
        var imageBytes = CreateTestJpeg(1000, 1000);
        var capture = new ScreenCapture(
            imageBytes,
            "App",
            Width: 1000,
            Height: 1000,
            ScreenLeft: 0,
            ScreenTop: 0,
            SourceWidth: 1000,
            SourceHeight: 1000);

        // 98% coverage
        var region = new ScreenRegion(
            NormalizedX: 10,
            NormalizedY: 10,
            NormalizedWidth: 980,
            NormalizedHeight: 980,
            Path: [new GuidancePoint(10, 10), new GuidancePoint(990, 10), new GuidancePoint(990, 990), new GuidancePoint(10, 990)]);

        var result = ScreenCaptureCropper.Crop(capture, region);

        Assert.Same(capture, result);
    }
}
