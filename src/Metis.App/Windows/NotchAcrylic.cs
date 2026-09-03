using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Metis.App.Windows;

/// <summary>
/// Puts real Windows acrylic behind the notch, so what is on the desktop shows
/// through it blurred rather than being hidden behind a flat panel.
///
/// Two things make this harder than it sounds, and both dictate the shape of
/// what is here.
///
/// The first is that the notch is a layered window — <c>AllowsTransparency</c>
/// is what lets it be a rounded pill with click-through space beside it — and
/// DWM's documented backdrops (<c>DWMWA_SYSTEMBACKDROP_TYPE</c>, Mica, and the
/// rest) do not apply to layered windows at all. <c>SetWindowCompositionAttribute</c>
/// does. It is undocumented, which is why every call here is defensive: a
/// Windows build that does not recognise it must leave the notch looking exactly
/// as it did, not leave it invisible.
///
/// The second is that acrylic paints the whole window rectangle, and the notch's
/// window is deliberately a little larger than the pill it draws. Without a
/// region, the effect would be a blurred rectangle with square corners sitting
/// behind a rounded panel — which looks like a rendering fault rather than like
/// glass. <see cref="ShapeTo"/> clips the effect to the body's own rounded
/// bounds, and the caller re-applies it whenever the body resizes, so the glass
/// keeps the notch's shape through every animation.
/// </summary>
internal static class NotchAcrylic
{
    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;

        /// <summary>
        /// The tint, as ABGR rather than ARGB — the byte order is reversed from
        /// every other colour in this codebase, which is the single easiest
        /// thing to get wrong here and produces a plausible-looking wrong hue.
        /// </summary>
        public uint GradientColor;

        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    private const int WcaAccentPolicy = 19;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint handle, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint handle, nint region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    /// <summary>
    /// Turns the effect on for a window, tinted with <paramref name="tint"/>.
    ///
    /// The tint's alpha is what decides how much of the desktop comes through:
    /// fully opaque is a solid panel with no point to it, and too transparent
    /// stops the text on top being readable over a bright wallpaper. Returns
    /// false when the platform declined, so the caller can leave its ordinary
    /// opaque brush in place rather than shipping an unreadable notch.
    /// </summary>
    public static bool Enable(Window window, System.Windows.Media.Color tint)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return false;
        }

        var policy = new AccentPolicy
        {
            AccentState = AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = (uint)((tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R),
            AnimationId = 0
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = pointer,
                SizeOfData = size
            };

            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            // A Windows without this entry point at all. Nothing to do but let
            // the caller keep its opaque brush.
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>
    /// Clips the window — and so the acrylic inside it — to a rounded rectangle
    /// in device pixels.
    ///
    /// Called on every size change of the body rather than once, because the
    /// notch spends a good part of its life mid-animation and glass that keeps
    /// the old shape while the panel grows is worse than no glass at all.
    /// </summary>
    public static void ShapeTo(Window window, Rect bounds, double cornerRadius)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Win32 regions are in physical pixels; everything above is in WPF's
        // device-independent units, so a display at anything but 100% scaling
        // needs this conversion or the glass is the wrong size by exactly the
        // scale factor.
        var source = PresentationSource.FromVisual(window);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;

        var left = (int)Math.Floor(bounds.Left * scaleX);
        var top = (int)Math.Floor(bounds.Top * scaleY);
        var right = (int)Math.Ceiling(bounds.Right * scaleX);
        var bottom = (int)Math.Ceiling(bounds.Bottom * scaleY);
        var radius = (int)Math.Round(cornerRadius * 2 * scaleX);

        var region = CreateRoundRectRgn(left, top, right + 1, bottom + 1, radius, radius);
        if (region == nint.Zero)
        {
            return;
        }

        // Windows takes ownership of the region on success, so it must only be
        // deleted when the call failed — deleting it either way is a crash, and
        // never deleting it leaks a GDI object per resize.
        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    /// <summary>
    /// Removes the region, so the window is its full rectangle again. Used when
    /// the effect could not be enabled, because a window clipped to a shape it
    /// is no longer drawing glass in would cut off its own drop shadow.
    /// </summary>
    public static void ClearShape(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != nint.Zero)
        {
            SetWindowRgn(handle, nint.Zero, true);
        }
    }
}
