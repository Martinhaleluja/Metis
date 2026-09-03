using System.Runtime.InteropServices;

namespace Metis.Windows;

/// <summary>
/// Keeps a window out of screen captures — Metis's own included.
///
/// The notch sits on top of everything and is open for the whole of a turn, so
/// until now every screenshot Metis sent to a cloud model contained a picture
/// of the conversation it was having: the question, and the previous answer,
/// rendered back into the next request. That is a leak, a waste of image
/// tokens, and a small hall of mirrors.
///
/// It also means Metis stops appearing in anyone else's screen recording or
/// shared call, which is the behaviour people expect of a private assistant.
///
/// <c>WDA_EXCLUDEFROMCAPTURE</c> needs Windows 10 version 2004; on anything
/// older the call fails and the window is simply captured as before, which is
/// why the result is reported rather than assumed.
/// </summary>
public static class WindowCaptureExclusion
{
    private const uint WdaNone = 0x00;
    private const uint WdaExcludeFromCapture = 0x11;

    /// <summary>
    /// Asks Windows to leave this window out of screen captures. Returns false
    /// when the platform will not do it, so a caller can log the fact rather
    /// than believe something that is not true.
    /// </summary>
    public static bool Exclude(nint windowHandle) =>
        windowHandle != nint.Zero &&
        SetWindowDisplayAffinity(windowHandle, WdaExcludeFromCapture);

    /// <summary>Puts a window back into captures.</summary>
    public static bool Include(nint windowHandle) =>
        windowHandle != nint.Zero &&
        SetWindowDisplayAffinity(windowHandle, WdaNone);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
