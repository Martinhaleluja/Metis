using Metis.Windows;

namespace Metis.Tests;

/// <summary>
/// These tests photograph the real desktop, so the one thing they must not do
/// is ask Windows how big it is twice.
///
/// <c>SystemInformation.VirtualScreen</c> is process-global and its answer
/// changes once anything establishes DPI awareness for the process — which, in
/// a suite that also loads WPF markup in parallel, can happen at any moment.
/// Reading it once to predict the capture and letting the service read it again
/// inside the capture meant the two could describe different desktops, and the
/// bounds assertions then failed for reasons that had nothing to do with
/// capturing. Each test now takes one reading and hands it to the service, so
/// expectation and result come from the same observation.
/// </summary>
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
        var capture = await new VirtualDesktopCaptureService(() => expectedBounds)
            .CaptureActiveWindowAsync();

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
        var service = new VirtualDesktopCaptureService(() => expectedBounds);
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

    /// <summary>
    /// The scaling budget is the part worth pinning down, and it needs no
    /// desktop at all once the bounds are supplied: a wide 4K desktop has to
    /// come back inside the compact budget while keeping its original bounds,
    /// because normalized model coordinates are mapped back through them.
    /// </summary>
    [Fact]
    public async Task A_large_desktop_is_scaled_into_the_compact_budget_without_losing_its_bounds()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        // Deliberately offset and larger than the budget, in the shape a
        // second monitor to the left of the primary one produces.
        var bounds = new System.Drawing.Rectangle(-1920, -120, 3840, 1200);
        var service = new VirtualDesktopCaptureService(() => bounds);
        service.UseCompactLocalProfile(true);

        var capture = await service.CaptureActiveWindowAsync();

        Assert.NotNull(capture);
        Assert.Equal(-1920, capture.ScreenLeft);
        Assert.Equal(-120, capture.ScreenTop);
        Assert.Equal(3840, capture.SourceWidth);
        Assert.Equal(1200, capture.SourceHeight);
        Assert.InRange(capture.Width, 1, 1280);
        Assert.InRange(capture.Height, 1, 720);

        // The scale is uniform, so the image keeps the desktop's proportions
        // and a coordinate mapped back through it lands where it started.
        var horizontal = capture.Width / (double)capture.SourceWidth;
        var vertical = capture.Height / (double)capture.SourceHeight;
        Assert.Equal(horizontal, vertical, precision: 2);
    }
}
