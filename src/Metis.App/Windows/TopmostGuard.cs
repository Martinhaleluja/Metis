using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Metis.App.Windows;

/// <summary>
/// Keeps Metis's always-on-top surfaces actually on top, and in a deliberate
/// order relative to each other.
///
/// <c>Topmost="True"</c> is not a standing claim. It puts a window into the
/// topmost band once, at the position it happened to be in at that moment;
/// anything that joins the band afterwards — the shell's own taskbar and its
/// flyouts, another always-on-top application, a window an app raises when it is
/// activated — lands above Metis and stays there. The notch then sits behind the
/// thing it is supposed to be narrating, which reads as Metis having vanished.
///
/// Re-asserting on a timer is the reliable answer: <c>SetWindowPos</c> with
/// HWND_TOPMOST on a window already at the top of the band is a no-op, and when
/// it is not, it is exactly the correction needed. The windows are pushed in
/// order on each pass and the last one wins, which is how the stack below stays
/// in the same order it was declared in rather than depending on which surface
/// last happened to be shown.
///
/// SWP_NOACTIVATE is what makes this safe to run while the user is typing: the
/// notch is raised without being given focus, so nothing is taken away from
/// whatever they are working in.
/// </summary>
public sealed class TopmostGuard : IDisposable
{
    private const int HwndTopmost = -1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>
    /// Bottom of the intended stack first. The last window added ends up above
    /// all the others.
    /// </summary>
    private readonly List<Window> _stack = [];

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    /// <summary>
    /// A second is short enough that a window landing on top of Metis is
    /// corrected before it registers as a glitch, and long enough that three
    /// <c>SetWindowPos</c> calls cost nothing worth measuring.
    /// </summary>
    public TopmostGuard()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Reassert();
    }

    /// <summary>
    /// Adds a window to the stack. Windows added later sit above windows added
    /// earlier, so callers should add from the bottom up.
    /// </summary>
    public void Add(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _stack.Add(window);
    }

    public void Start() => _timer.Start();

    /// <summary>
    /// Pushes the whole stack back to the top of the topmost band, in order.
    /// Worth calling directly at the moments that matter — a surface being
    /// shown, the chat opening — rather than waiting up to a second for the
    /// next pass.
    /// </summary>
    public void Reassert()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var window in _stack)
        {
            // A hidden window is left where it is. Ordering something the user
            // cannot see achieves nothing, and it would only have to be redone
            // when the window is next shown anyway.
            if (!window.IsVisible)
            {
                continue;
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == nint.Zero)
            {
                continue;
            }

            // Owned windows — the notch's own model and conversation menus —
            // are carried along by Windows, which never places an owner above
            // the windows it owns. So raising the notch cannot cover its own
            // dropdown.
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _stack.Clear();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
}
