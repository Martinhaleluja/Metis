namespace Metis.Core.Models;

public sealed record ScreenCapture(
    byte[] ImageBytes,
    string WindowTitle,
    int Width,
    int Height,
    int ScreenLeft = 0,
    int ScreenTop = 0,
    int SourceWidth = 0,
    int SourceHeight = 0,
    long WindowHandle = 0,
    string CaptureBackend = "Unknown",
    string ImageMimeType = "image/png",

    /// <summary>
    /// How many parts of the screen were painted out before this image was
    /// encoded. Carried on the capture so the model can be told the picture is
    /// incomplete: an unexplained black rectangle is something a model will
    /// happily describe as though it were real.
    /// </summary>
    int WithheldRegions = 0);

/// <summary>Why a piece of the screen was withheld from a capture.</summary>
public enum ProtectedRegionReason
{
    /// <summary>
    /// The application set a display affinity on its own window, which is
    /// Windows' way of letting a program say "do not record this". View-once
    /// media, banking apps and password managers use it.
    /// </summary>
    ApplicationProtected,

    /// <summary>A password field, found through the accessibility tree.</summary>
    PasswordField,

    /// <summary>The user listed this application as one Metis must not look at.</summary>
    UserExcluded
}

/// <summary>
/// A rectangle of the screen that must not reach a capture, in screen
/// coordinates before any scaling.
/// </summary>
public sealed record ProtectedRegion(
    int Left,
    int Top,
    int Width,
    int Height,
    ProtectedRegionReason Reason);

/// <summary>
/// Where a capture is about to be taken from, known before it is taken.
///
/// The accessibility scan needs the coordinate space a capture will use, and
/// nothing else about it — not the pixels, not the encoded size. Publishing the
/// bounds separately is what lets the scan and the capture run at the same time
/// instead of one after the other.
/// </summary>
public sealed record ScreenBounds(int Left, int Top, int Width, int Height);

public sealed record RecordedAudio(byte[] WavBytes, TimeSpan Duration, string DeviceName);
