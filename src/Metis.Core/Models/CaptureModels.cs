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

public sealed record RecordedAudio(byte[] WavBytes, TimeSpan Duration, string DeviceName);
