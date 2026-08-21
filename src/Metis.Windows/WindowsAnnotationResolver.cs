using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Metis.Core.Contracts;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Windows;

/// <summary>
/// Finds where an annotation's subject really is, using Windows rather than the
/// model's estimate wherever Windows can answer.
///
/// Every branch below degrades to the estimate rather than to nothing. An
/// application with no accessibility tree — a game, a canvas, a remote desktop —
/// still gets marked, just less precisely, because a slightly loose mark is
/// still an answer and silence is not.
/// </summary>
public sealed class WindowsAnnotationResolver : IAnnotationResolver
{
    /// <summary>
    /// How far a snapped control may differ in area from the model's own
    /// estimate before the snap is rejected.
    ///
    /// Walking down to the smallest element containing a point is reliable
    /// right up until the point misses the control, at which moment it returns
    /// whatever large container the point landed in instead. Marking that reads
    /// as Metis pointing vaguely at half the window while confidently naming a
    /// button, which is worse than the honest imprecision of the estimate. The
    /// bound is generous because the estimate is itself rough; it is there to
    /// catch the order-of-magnitude miss, not to referee close calls.
    /// </summary>
    private const double ControlSnapAreaFactor = 8d;

    /// <summary>
    /// A region is meant to be a container, so snapping upward is the point
    /// rather than a failure, and only an absurd result is rejected.
    /// </summary>
    private const double RegionSnapAreaFactor = 40d;

    public Task<ResolvedAnnotation?> ResolveAsync(
        AnnotationTarget target,
        ScreenCapture capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(capture);
        return Task.Run(() => Resolve(target, capture, cancellationToken), cancellationToken);
    }

    private static ResolvedAnnotation? Resolve(
        AnnotationTarget target,
        ScreenCapture capture,
        CancellationToken cancellationToken)
    {
        var screenArea = CaptureProjection.Area(capture);

        // A path is its points; there is no rectangle to look up.
        if (target.Scope == AnnotationScope.Path)
        {
            return null;
        }

        var (estimateX, estimateY, estimateWidth, estimateHeight) =
            CaptureProjection.ToScreenRect(target, capture);

        // Nothing was located and nothing can be: an offscreen target is marked
        // with a direction from the estimate, which is all it ever had.
        if (target.Scope == AnnotationScope.Offscreen || !target.HasPoint)
        {
            return target.HasPoint
                ? AnnotationDirector.Resolve(
                    target.Scope, estimateX, estimateY, estimateWidth, estimateHeight,
                    target.Label, AnnotationSource.Estimated, screenArea)
                : null;
        }

        try
        {
            var located = target.Scope switch
            {
                AnnotationScope.Window => LocateWindow(estimateX, estimateY, capture),
                AnnotationScope.TextSpan => LocateText(target, estimateX, estimateY, cancellationToken),
                _ => LocateElement(
                    target, estimateX, estimateY, estimateWidth, estimateHeight, screenArea, cancellationToken)
            };

            if (located is { } hit)
            {
                return AnnotationDirector.Resolve(
                    target.Scope,
                    hit.X,
                    hit.Y,
                    hit.Width,
                    hit.Height,
                    target.Label,
                    hit.Source,
                    screenArea);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Accessibility is best-effort throughout this codebase. A failure
            // here costs precision, never the annotation.
        }

        return AnnotationDirector.Resolve(
            target.Scope, estimateX, estimateY, estimateWidth, estimateHeight,
            target.Label, AnnotationSource.Estimated, screenArea);
    }

    private readonly record struct Located(int X, int Y, int Width, int Height, AnnotationSource Source);

    /// <summary>
    /// The whole window the estimate landed in. This is what turns "that black
    /// window is where you type commands" into a mark that contains the window,
    /// which is the sentence the user actually heard.
    /// </summary>
    private static Located? LocateWindow(int screenX, int screenY, ScreenCapture capture)
    {
        // The captured window is the subject, and it is authoritative: the
        // model answered about that image, so that is the window its sentence
        // is about. Looking up whatever sits under the estimated point instead
        // finds whichever window happens to be higher in the z-order — in
        // testing, a maximized window behind the terminal — and brackets the
        // wrong application entirely.
        var handle = capture.WindowHandle != 0
            ? new nint(capture.WindowHandle)
            : DesktopTargetWindowLocator.FindAt(screenX, screenY);

        if (handle == nint.Zero || !TryGetVisibleFrame(handle, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new Located(
            rect.Left + (width / 2),
            rect.Top + (height / 2),
            width,
            height,
            AnnotationSource.Window);
    }

    /// <summary>
    /// The bounding rectangle of an actual run of text, found through the text
    /// pattern. Only an exact match counts: an underline drawn under
    /// approximately the right words says something Metis did not mean.
    /// </summary>
    private static Located? LocateText(
        AnnotationTarget target,
        int screenX,
        int screenY,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.Text))
        {
            return null;
        }

        var window = DesktopTargetWindowLocator.FindAt(screenX, screenY);
        if (window == nint.Zero)
        {
            return null;
        }

        using var automation = new UIA3Automation();
        var root = automation.FromHandle(window);

        foreach (var element in Descend(root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pattern = element.Patterns.Text.PatternOrDefault;
            if (pattern is null)
            {
                continue;
            }

            System.Drawing.Rectangle[] rectangles;
            try
            {
                var range = pattern.DocumentRange.FindText(target.Text, false, true);
                if (range is null)
                {
                    continue;
                }

                rectangles = range.GetBoundingRectangles();
            }
            catch
            {
                continue;
            }

            if (rectangles.Length == 0)
            {
                continue;
            }

            // A wrapped span reports one rectangle per line. Marking their union
            // keeps the whole phrase underlined rather than only its first line.
            var left = rectangles.Min(rectangle => rectangle.Left);
            var top = rectangles.Min(rectangle => rectangle.Top);
            var right = rectangles.Max(rectangle => rectangle.Right);
            var bottom = rectangles.Max(rectangle => rectangle.Bottom);
            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            return new Located(
                left + (width / 2),
                top + (height / 2),
                width,
                height,
                AnnotationSource.Text);
        }

        return null;
    }

    /// <summary>
    /// The control at the estimated point, or the one the model named. The name
    /// is tried first: a model that can read a button's label off the screenshot
    /// is telling Metis something it can verify, whereas its coordinates are
    /// only ever an estimate.
    /// </summary>
    private static Located? LocateElement(
        AnnotationTarget target,
        int screenX,
        int screenY,
        int estimateWidth,
        int estimateHeight,
        long screenArea,
        CancellationToken cancellationToken)
    {
        var pointWindow = DesktopTargetWindowLocator.FindAt(screenX, screenY);
        var foregroundWindow = DesktopTargetWindowLocator.Foreground();
        if (pointWindow == nint.Zero && foregroundWindow == nint.Zero)
        {
            return null;
        }

        using var automation = new UIA3Automation();

        // The coordinate is only ever an estimate, and during a walkthrough it
        // is an estimate of where things were when the lesson was planned. Once
        // a step has opened an application the point lands in the wrong window
        // entirely, so a named target is looked for in the window the user is
        // actually working in as well. Without this, refreshing the screenshot
        // between steps changed nothing: every later mark was still anchored to
        // the layout of the first one.
        AutomationElement? named = null;
        if (!string.IsNullOrWhiteSpace(target.ElementName))
        {
            foreach (var handle in Candidates(pointWindow, foregroundWindow))
            {
                named = FindByName(automation.FromHandle(handle), target.ElementName!, cancellationToken);
                if (named is not null)
                {
                    break;
                }
            }
        }

        var element = named;
        if (element is null)
        {
            if (pointWindow == nint.Zero)
            {
                return null;
            }

            element = SmallestContaining(
                automation.FromHandle(pointWindow), screenX, screenY, cancellationToken);
        }

        if (element is null)
        {
            return null;
        }

        System.Drawing.Rectangle bounds;
        try
        {
            bounds = element.BoundingRectangle;
        }
        catch
        {
            return null;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        // These size tests exist to catch the descent landing in a container
        // instead of on the control, so they only apply to the descent. A
        // target found by its own name has been identified, not guessed at, and
        // measuring it against a stale estimate of its size is how a correctly
        // re-found control gets thrown away — which is exactly the case this
        // needs to keep.
        if (named is not null)
        {
            return new Located(
                bounds.Left + (bounds.Width / 2),
                bounds.Top + (bounds.Height / 2),
                bounds.Width,
                bounds.Height,
                AnnotationSource.Element);
        }

        // Reject a snap that disagrees wildly with what the model described,
        // because that is the signature of having landed in a container rather
        // than on the control.
        var factor = target.Scope == AnnotationScope.Region ? RegionSnapAreaFactor : ControlSnapAreaFactor;
        if (target.HasExtent)
        {
            var estimated = (double)estimateWidth * estimateHeight;
            var found = (double)bounds.Width * bounds.Height;
            if (estimated > 0 && (found > estimated * factor || found * factor < estimated))
            {
                return null;
            }
        }
        else if (target.Scope == AnnotationScope.Control && screenArea > 0)
        {
            // With no extent to compare against, the size test above cannot
            // run — and that is exactly when the descent is most likely to
            // return the window root, because nothing contradicts it. A control
            // covering a quarter of the screen is not a control, so the
            // estimate is kept rather than snapping to half the desktop.
            var share = (double)bounds.Width * bounds.Height / screenArea;
            if (share >= AnnotationDirector.ControlAreaCeiling)
            {
                return null;
            }
        }

        return new Located(
            bounds.Left + (bounds.Width / 2),
            bounds.Top + (bounds.Height / 2),
            bounds.Width,
            bounds.Height,
            AnnotationSource.Element);
    }

    /// <summary>
    /// The windows worth searching for a named target, nearest guess first: the
    /// one under the estimated point, then the one the user is working in.
    /// </summary>
    private static IEnumerable<nint> Candidates(nint pointWindow, nint foregroundWindow)
    {
        if (pointWindow != nint.Zero)
        {
            yield return pointWindow;
        }

        if (foregroundWindow != nint.Zero && foregroundWindow != pointWindow)
        {
            yield return foregroundWindow;
        }
    }

    /// <summary>
    /// Walks down to the smallest element that still contains the point, the
    /// same descent the inspect gesture uses, so both directions agree about
    /// which control a coordinate belongs to.
    /// </summary>
    private static AutomationElement? SmallestContaining(
        AutomationElement root,
        int screenX,
        int screenY,
        CancellationToken cancellationToken)
    {
        var point = new System.Drawing.Point(screenX, screenY);
        AutomationElement? best = null;
        var current = root;

        for (var depth = 0; depth < 20; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationElement? next = null;
            var smallestArea = double.MaxValue;

            AutomationElement[] children;
            try
            {
                children = current.FindAllChildren();
            }
            catch
            {
                break;
            }

            foreach (var child in children)
            {
                try
                {
                    var bounds = child.BoundingRectangle;
                    if (!bounds.Contains(point))
                    {
                        continue;
                    }

                    var area = Math.Max(0d, bounds.Width) * Math.Max(0d, bounds.Height);
                    if (area < smallestArea)
                    {
                        smallestArea = area;
                        next = child;
                    }
                }
                catch
                {
                    // Stale children are skipped; siblings may still answer.
                }
            }

            if (next is null)
            {
                break;
            }

            best = next;
            current = next;
        }

        return best;
    }

    /// <summary>
    /// Finds a control by the name the model read off the screen. Exact matches
    /// are preferred over contained ones so "Save" does not resolve to "Save
    /// As" when both are present.
    /// </summary>
    private static AutomationElement? FindByName(
        AutomationElement root,
        string name,
        CancellationToken cancellationToken)
    {
        AutomationElement? contained = null;

        foreach (var element in Descend(root, cancellationToken))
        {
            string? elementName;
            try
            {
                elementName = element.Name;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(elementName))
            {
                continue;
            }

            if (string.Equals(elementName, name, StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }

            contained ??= elementName.Contains(name, StringComparison.OrdinalIgnoreCase)
                ? element
                : null;
        }

        return contained;
    }

    private const int MaximumVisitedElements = 400;

    /// <summary>
    /// Breadth-first walk of a window's controls, bounded so a pathological
    /// tree cannot stall an annotation the user is waiting to see.
    /// </summary>
    private static IEnumerable<AutomationElement> Descend(
        AutomationElement root,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        var visited = 0;

        while (queue.Count > 0 && visited < MaximumVisitedElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = queue.Dequeue();
            visited++;
            yield return element;

            AutomationElement[] children;
            try
            {
                children = element.FindAllChildren();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>
    /// The rectangle the user can actually see a window occupying.
    ///
    /// <c>GetWindowRect</c> includes the invisible resize border that modern
    /// Windows puts around every window — several pixels of nothing on each
    /// side. Bracketing that draws the mark visibly clear of the window's
    /// edges, which reads as Metis pointing at the area around the terminal
    /// rather than at the terminal. The compositor knows where the frame really
    /// ends, so it is asked, and its answer is used when it gives one.
    /// </summary>
    private static bool TryGetVisibleFrame(nint windowHandle, out Rect rect)
    {
        if (DwmGetWindowAttribute(
                windowHandle,
                DwmwaExtendedFrameBounds,
                out var frame,
                Marshal.SizeOf<Rect>()) == 0 &&
            frame.Right > frame.Left &&
            frame.Bottom > frame.Top)
        {
            rect = frame;
            return true;
        }

        // Windows without a composited frame — some tool windows, and anything
        // running with the compositor disabled — still answer the older call.
        return GetWindowRect(windowHandle, out rect);
    }

    private const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out Rect value,
        int size);
}
