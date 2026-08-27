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
    string ImageMimeType = "image/png");

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
