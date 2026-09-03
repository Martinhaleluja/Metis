using System.Drawing;
using Metis.AI;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Windows;

namespace Metis.Tests;

/// <summary>
/// Metis photographs the whole desktop and sends it to a cloud model, so the
/// parts of the screen that must never leave the machine have to be gone before
/// the image is encoded — not filtered afterwards, and not merely omitted from
/// the description.
/// </summary>
public sealed class CapturePrivacyTests
{
    private static ProtectedRegion Region(int left, int top, int width, int height) =>
        new(left, top, width, height, ProtectedRegionReason.ApplicationProtected);

    [Fact]
    public void A_protected_window_is_painted_out_and_the_rest_is_untouched()
    {
        using var frame = new Bitmap(200, 100);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.White);
        }

        var painted = ScreenRedaction.Apply(
            frame,
            new Rectangle(0, 0, 200, 100),
            [Region(50, 20, 40, 30)]);

        Assert.Equal(1, painted);
        Assert.Equal(Color.Black.ToArgb(), frame.GetPixel(60, 30).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), frame.GetPixel(89, 49).ToArgb());

        // Just outside the rectangle on every side.
        Assert.Equal(Color.White.ToArgb(), frame.GetPixel(49, 30).ToArgb());
        Assert.Equal(Color.White.ToArgb(), frame.GetPixel(90, 30).ToArgb());
        Assert.Equal(Color.White.ToArgb(), frame.GetPixel(60, 19).ToArgb());
        Assert.Equal(Color.White.ToArgb(), frame.GetPixel(60, 50).ToArgb());
    }

    /// <summary>
    /// A second monitor to the left of the primary one gives the desktop a
    /// negative origin, and window rectangles are in that same screen space
    /// while the captured frame starts at zero. Getting the translation wrong
    /// would blank a piece of the wrong window and leave the protected one
    /// showing.
    /// </summary>
    [Fact]
    public void Regions_are_translated_out_of_screen_coordinates()
    {
        using var frame = new Bitmap(100, 100);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.White);
        }

        var painted = ScreenRedaction.Apply(
            frame,
            new Rectangle(-1920, -50, 100, 100),
            [Region(-1900, -40, 20, 20)]);

        Assert.Equal(1, painted);
        Assert.Equal(Color.Black.ToArgb(), frame.GetPixel(25, 15).ToArgb());
        Assert.Equal(Color.White.ToArgb(), frame.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void A_region_entirely_off_the_frame_paints_nothing()
    {
        using var frame = new Bitmap(50, 50);

        Assert.Equal(0, ScreenRedaction.Apply(frame, new Rectangle(0, 0, 50, 50), [Region(500, 500, 20, 20)]));
        Assert.Equal(0, ScreenRedaction.Apply(frame, new Rectangle(0, 0, 50, 50), []));
        Assert.Equal(0, ScreenRedaction.Apply(frame, new Rectangle(0, 0, 50, 50), null));
    }

    [Fact]
    public async Task A_capture_reports_how_many_regions_it_withheld()
    {
        var capture = new VirtualDesktopCaptureService(
            () => new Rectangle(0, 0, 400, 300),
            () => [Region(10, 10, 50, 50), Region(100, 100, 50, 50)]);

        var shot = await capture.CaptureActiveWindowAsync(ScreenCaptureDetail.Standard);

        Assert.NotNull(shot);
        Assert.Equal(2, shot!.WithheldRegions);
    }

    [Fact]
    public async Task A_capture_with_nothing_protected_withholds_nothing()
    {
        var capture = new VirtualDesktopCaptureService(
            () => new Rectangle(0, 0, 400, 300),
            () => []);

        var shot = await capture.CaptureActiveWindowAsync(ScreenCaptureDetail.Standard);

        Assert.NotNull(shot);
        Assert.Equal(0, shot!.WithheldRegions);
    }

    /// <summary>
    /// The model has to be told the picture is incomplete. An unexplained black
    /// rectangle is something it will describe as a dark panel or an empty pane,
    /// and a confident wrong answer about withheld content is worse than none.
    /// </summary>
    [Fact]
    public void The_model_is_told_when_something_was_withheld()
    {
        var withheld = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("What is this?", [1, 2, 3], WithheldScreenRegions: 2),
            "gemini-3.5-flash");
        var clean = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("What is this?", [1, 2, 3]),
            "gemini-3.5-flash");

        Assert.Contains("withheld_regions: 2", withheld, StringComparison.Ordinal);
        // The teaching rules mention the field by name, so the check has to be
        // for the line that reports a count, not for the word.
        Assert.DoesNotContain("withheld_regions: ", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void The_teaching_rules_forbid_guessing_at_a_blacked_out_region()
    {
        var instruction = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("What is this?"),
            "gemini-3.5-flash");

        Assert.Contains("not permitted to see", instruction, StringComparison.Ordinal);
    }
}
