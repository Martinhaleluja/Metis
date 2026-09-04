using System.Windows;
using System.Windows.Interop;
using Metis.Core.Contracts;
using Metis.Windows;

namespace Metis.App;

/// <summary>
/// Keeps Metis out of every screenshot, its own included.
///
/// The notch is topmost and open for the whole of a turn, so until this existed
/// every picture Metis sent to a cloud model contained the conversation it was
/// having — the question just asked, and the answer before it, rendered back
/// into the next request. It also means Metis no longer turns up in anyone's
/// screen recording or shared call.
/// </summary>
internal static class PrivateWindows
{
    /// <summary>
    /// Marks a window as one screen captures must not see, once it has a handle
    /// to mark. Safe to call before the window is shown.
    /// </summary>
    internal static void KeepOutOfScreenCaptures(this Window window, IDiagnosticLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        void Apply(object? sender, EventArgs args)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("METIS_ALLOW_SCREENSHOT"), "1", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (WindowCaptureExclusion.Exclude(handle))
            {
                return;
            }

            // Windows 10 before version 2004 has no way to do this. Saying so
            // once is better than leaving the impression it worked, because the
            // difference is whether the chat transcript is in the screenshots.
            log?.Info(
                $"This version of Windows will not exclude {window.GetType().Name} from screen captures, " +
                "so it will appear in Metis's own screenshots.");
        }

        if (window.IsInitialized && new WindowInteropHelper(window).Handle != nint.Zero)
        {
            Apply(window, EventArgs.Empty);
            return;
        }

        window.SourceInitialized += Apply;
    }
}
