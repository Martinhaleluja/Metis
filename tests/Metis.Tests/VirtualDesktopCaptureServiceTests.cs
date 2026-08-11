using Metis.Windows;

namespace Metis.Tests;

public sealed class VirtualDesktopCaptureServiceTests
{
    [Fact]
    public async Task Capture_returns_one_jpeg_for_the_complete_virtual_desktop()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var expectedBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var capture = await new VirtualDesktopCaptureService().CaptureActiveWindowAsync();

        Assert.NotNull(capture);
        Assert.Equal("image/jpeg", capture.ImageMimeType);
        Assert.Equal("Virtual desktop (all monitors)", capture.CaptureBackend);
        Assert.Equal(expectedBounds.Left, capture.ScreenLeft);
        Assert.Equal(expectedBounds.Top, capture.ScreenTop);
        Assert.Equal(expectedBounds.Width, capture.SourceWidth);
        Assert.Equal(expectedBounds.Height, capture.SourceHeight);
        Assert.InRange(capture.Width, 1, 2560);
        Assert.InRange(capture.Height, 1, 1440);
        Assert.True(capture.ImageBytes.Length > 2);
        Assert.Equal(0xFF, capture.ImageBytes[0]);
        Assert.Equal(0xD8, capture.ImageBytes[1]);
    }

    [Fact]
    public async Task Compact_local_profile_preserves_full_bounds_with_a_720p_visual_budget()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        var expectedBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var service = new VirtualDesktopCaptureService();
        service.UseCompactLocalProfile(true);
        var capture = await service.CaptureActiveWindowAsync();

        Assert.NotNull(capture);
        Assert.Equal(expectedBounds.Left, capture.ScreenLeft);
        Assert.Equal(expectedBounds.Top, capture.ScreenTop);
        Assert.Equal(expectedBounds.Width, capture.SourceWidth);
        Assert.Equal(expectedBounds.Height, capture.SourceHeight);
        Assert.InRange(capture.Width, 1, 1280);
        Assert.InRange(capture.Height, 1, 720);
    }
}
