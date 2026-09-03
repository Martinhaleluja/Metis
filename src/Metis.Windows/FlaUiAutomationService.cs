using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Windows;

/// <summary>
/// Provides a bounded accessibility snapshot for model context and invokes
/// addressable controls without moving the user's pointer when UIA supports it.
/// </summary>
public sealed class FlaUiAutomationService : IUiAutomationService, IDisposable
{
    private const int MaximumSnapshotElements = 120;

    /// <summary>
    /// How long the snapshot may spend walking before it settles for what it
    /// has.
    ///
    /// Every property read here is a cross-process COM call into whatever
    /// application owns the window, so the cost is not Metis's to predict: an
    /// application that is busy, hung, or simply slow to answer can stall the
    /// walk indefinitely. This used to be bounded only by the turn's own
    /// 75-second deadline, which meant one unresponsive window could spend the
    /// entire turn. A partial list of controls is a good answer; a turn that
    /// never asks the question is not.
    /// </summary>
    private static readonly TimeSpan SnapshotBudget = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// One automation session, reused.
    ///
    /// <c>new UIA3Automation()</c> sets up COM interop and was being paid for on
    /// every call — and an Inspect turn makes two or three calls. The instance
    /// is thread-safe enough for the sequential use it gets here, and it is
    /// created on first use so a machine without UI Automation still starts.
    /// </summary>
    private readonly Lazy<UIA3Automation> _automation = new(
        () => new UIA3Automation(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_automation.IsValueCreated)
        {
            _automation.Value.Dispose();
        }
    }

    public Task<string?> DescribeWindowAsync(
        ScreenCapture capture,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DescribeWindow(capture, cancellationToken), cancellationToken);

    public Task<string?> DescribeElementAtAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DescribeElementAt(screenX, screenY, cancellationToken), cancellationToken);

    public Task<string?> DescribeRegionAsync(
        ScreenCapture capture,
        int screenLeft,
        int screenTop,
        int screenWidth,
        int screenHeight,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DescribeRegion(capture, screenLeft, screenTop, screenWidth, screenHeight, cancellationToken), cancellationToken);

    public Task<UiElementHit?> FindElementAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => FindElement(query, cancellationToken), cancellationToken);

    /// <summary>
    /// Whether this element is a password box.
    ///
    /// Windows' accessibility layer marks these, and it is the only reliable
    /// way to know: a password field looks like any other text box from the
    /// outside, and its contents are exactly what must never be read, described
    /// or sent anywhere.
    /// </summary>
    private static bool IsPasswordField(AutomationElement element)
    {
        try
        {
            // Asked only of controls that could hold typed text. Every property
            // read here is a cross-process call into whichever application owns
            // the window, and the snapshot walks up to a hundred and twenty
            // elements — asking all of them cost well over a second and pushed
            // the walk into its own time budget, which then truncated the list
            // of controls Metis had to point with. A password box is an Edit
            // everywhere Windows draws one; Custom and Document are allowed
            // through because some frameworks report their fields that way.
            var controlType = element.ControlType;
            if (controlType is not (FlaUI.Core.Definitions.ControlType.Edit
                or FlaUI.Core.Definitions.ControlType.Custom
                or FlaUI.Core.Definitions.ControlType.Document))
            {
                return false;
            }

            return element.Properties.IsPassword.TryGetValue(out var isPassword) && isPassword;
        }
        catch
        {
            // An element that will not answer is treated as a password field
            // rather than as an ordinary one. The cost of being wrong in this
            // direction is a missing control name; in the other it is a
            // password in a prompt.
            return true;
        }
    }

    /// <summary>
    /// The password box the user is typing into, if there is one, as a region to
    /// paint out of the screenshot.
    ///
    /// Only the focused field, and deliberately so. A password box that is not
    /// focused is showing dots, which give nothing away; the one being typed
    /// into is the one that may have been switched to plain text by a
    /// "show password" control. One accessibility call, so it is cheap enough
    /// to sit on the capture path.
    /// </summary>
    public ProtectedRegion? FindFocusedPasswordFieldRegion()
    {
        try
        {
            var focused = _automation.Value.FocusedElement();
            if (focused is null || !IsPasswordField(focused))
            {
                return null;
            }

            var bounds = focused.BoundingRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

            return new ProtectedRegion(
                (int)bounds.Left,
                (int)bounds.Top,
                (int)bounds.Width,
                (int)bounds.Height,
                ProtectedRegionReason.PasswordField);
        }
        catch
        {
            // Best effort. The accessibility tree is not always reachable, and
            // a missing redaction here is covered by never reading the field's
            // contents in the first place.
            return null;
        }
    }

    private string? DescribeWindow(ScreenCapture capture, CancellationToken cancellationToken)
    {
        try
        {
            var automation = _automation.Value;
            var root = capture.WindowHandle == 0
                ? automation.GetDesktop()
                : automation.FromHandle(new nint(capture.WindowHandle));
            var queue = new Queue<AutomationElement>();
            var descriptors = new List<UiElementDescriptor>(MaximumSnapshotElements);
            queue.Enqueue(root);
            var budget = Stopwatch.StartNew();

            while (queue.Count > 0 && descriptors.Count < MaximumSnapshotElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (budget.Elapsed > SnapshotBudget)
                {
                    break;
                }

                var element = queue.Dequeue();
                AddDescriptor(element, capture, descriptors);

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
                    if (queue.Count + descriptors.Count >= MaximumSnapshotElements * 2)
                    {
                        break;
                    }

                    queue.Enqueue(child);
                }
            }

            return descriptors.Count == 0
                ? null
                : JsonSerializer.Serialize(
                    descriptors,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Accessibility is best-effort. Vision remains available when an
            // application does not expose a UI Automation tree.
            return null;
        }
    }

    /// <summary>
    /// Walks from the window at a coordinate down to the smallest element that
    /// still contains it, then describes that element and the ancestors that
    /// give it meaning ("Bold" inside "Formatting toolbar").
    /// </summary>
    private string? DescribeElementAt(int screenX, int screenY, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = DesktopTargetWindowLocator.FindAt(screenX, screenY);
            if (window == nint.Zero)
            {
                return null;
            }

            var automation = _automation.Value;
            var root = automation.FromHandle(window);
            var chain = FindElementChain(root, new System.Drawing.Point(screenX, screenY), cancellationToken);
            if (chain.Count == 0)
            {
                return null;
            }

            var described = chain
                .Select(DescribeSingleElement)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .TakeLast(4)
                .ToArray();

            return described.Length == 0 ? null : string.Join(" > ", described);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Inspect degrades to vision only; the caller reports that the
            // pointer target could not be resolved instead of inventing one.
            return null;
        }
    }

    /// <summary>
    /// Walks the accessibility tree to discover and describe all visible elements
    /// situated within or intersecting the specified screen region/rectangle.
    /// </summary>
    private string? DescribeRegion(
        ScreenCapture capture,
        int screenLeft,
        int screenTop,
        int screenWidth,
        int screenHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetRect = new System.Drawing.Rectangle(screenLeft, screenTop, Math.Max(1, screenWidth), Math.Max(1, screenHeight));
            var automation = _automation.Value;
            var root = capture.WindowHandle == 0
                ? automation.GetDesktop()
                : automation.FromHandle(new nint(capture.WindowHandle));

            var queue = new Queue<AutomationElement>();
            var elementsInRegion = new List<string>();
            var budget = Stopwatch.StartNew();
            queue.Enqueue(root);

            while (queue.Count > 0 && elementsInRegion.Count < 30)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (budget.Elapsed > SnapshotBudget)
                {
                    break;
                }

                var element = queue.Dequeue();

                try
                {
                    var bounds = element.BoundingRectangle;
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        var elemRect = new System.Drawing.Rectangle((int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height);
                        if (targetRect.IntersectsWith(elemRect))
                        {
                            var controlType = element.ControlType.ToString();
                            if (IsPasswordField(element))
                            {
                                var withheld = $"{controlType} (password field, contents withheld)";
                                if (!elementsInRegion.Contains(withheld))
                                {
                                    elementsInRegion.Add(withheld);
                                }

                                continue;
                            }

                            var name = element.Name?.Trim();
                            var automationId = element.AutomationId?.Trim();

                            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(automationId))
                            {
                                var label = string.IsNullOrWhiteSpace(name) ? automationId : name;
                                var desc = $"{controlType} \"{Shorten(label, 60)}\"";
                                if (!elementsInRegion.Contains(desc))
                                {
                                    elementsInRegion.Add(desc);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Stale or inaccessible element
                }

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
                    if (queue.Count + elementsInRegion.Count >= 200)
                    {
                        break;
                    }

                    queue.Enqueue(child);
                }
            }

            return elementsInRegion.Count == 0 ? null : string.Join(", ", elementsInRegion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scores every visible control in the foreground window against the words
    /// in the request and returns the best match. Windows knows exactly where
    /// its controls are, so when the model declines to give coordinates this
    /// still finds the thing the user asked about — and finds it precisely.
    /// </summary>
    private UiElementHit? FindElement(string query, CancellationToken cancellationToken)
    {
        var terms = query
            .ToLowerInvariant()
            .Split([' ', '\t', ',', '.', '?', '!', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && !IgnoredWords.Contains(word))
            .Distinct()
            .ToArray();
        if (terms.Length == 0)
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var automation = _automation.Value;

            // Search the window the user is actually in. Falling back to the
            // whole desktop would match controls in Metis's own windows or in
            // apps behind the one being asked about.
            var handle = GetForegroundWindow();
            var window = handle != nint.Zero ? automation.FromHandle(handle) : automation.GetDesktop();

            UiElementHit? best = null;
            var bestScore = 0;

            foreach (var element in window.FindAllDescendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (element.IsOffscreen)
                    {
                        continue;
                    }

                    var bounds = element.BoundingRectangle;
                    if (bounds.Width < 8 || bounds.Height < 8)
                    {
                        continue;
                    }

                    var name = (element.Name ?? string.Empty).Trim();
                    var controlType = element.ControlType.ToString();
                    var lowerName = name.ToLowerInvariant();

                    // Only the visible name counts as textual evidence. Neither
                    // the control type nor the automation id is matched by
                    // substring, because both routinely contain words like
                    // "bar" or "text" and would match a request for a "search
                    // bar" to the window's system MenuBar.
                    var score = terms.Count(term => lowerName.Contains(term, StringComparison.Ordinal)) * 2;

                    if (terms.Any(term => string.Equals(lowerName, term, StringComparison.Ordinal)))
                    {
                        score += 4;
                    }

                    if (WantedTypes(terms).Contains(controlType))
                    {
                        score += 2;
                    }

                    // A weak, purely incidental hit is worse than admitting the
                    // control was not found: a mark on the wrong thing actively
                    // misleads, whereas no mark simply says "I could not tell".
                    if (score < 2)
                    {
                        continue;
                    }

                    if (bounds.Width * bounds.Height < 240_000)
                    {
                        score += 1;
                    }

                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    best = new UiElementHit(
                        name.Length > 0 ? name : controlType,
                        controlType,
                        (int)(bounds.Left + (bounds.Width / 2)),
                        (int)(bounds.Top + (bounds.Height / 2)),
                        (int)bounds.Width,
                        (int)bounds.Height);
                }
                catch
                {
                    // One unreadable element must not stop the search.
                }
            }

            return best;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    /// <summary>
    /// Turns the kind of thing the user named into the control types that
    /// satisfy it. "Where can I type" names no control, but it does say the
    /// answer is a text field — which is enough to find one.
    /// </summary>
    private static HashSet<string> WantedTypes(IEnumerable<string> terms)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            switch (term)
            {
                case "type" or "typing" or "input" or "text" or "enter" or "write" or "field" or "box" or "search":
                    wanted.Add("Edit");
                    wanted.Add("Document");
                    wanted.Add("ComboBox");
                    break;
                case "button" or "press" or "click":
                    wanted.Add("Button");
                    wanted.Add("SplitButton");
                    break;
                case "menu":
                    wanted.Add("MenuItem");
                    wanted.Add("Menu");
                    break;
                case "link":
                    wanted.Add("Hyperlink");
                    break;
                case "checkbox" or "tick":
                    wanted.Add("CheckBox");
                    break;
                case "tab":
                    wanted.Add("TabItem");
                    break;
                case "list" or "dropdown":
                    wanted.Add("List");
                    wanted.Add("ComboBox");
                    break;
            }
        }

        return wanted;
    }

    private static readonly HashSet<string> IgnoredWords = new(StringComparer.Ordinal)
    {
        "show", "where", "the", "can", "you", "please", "find", "point", "highlight",
        "help", "what", "which", "how", "and", "for", "with", "this", "that",
        "into", "metis", "screen", "does", "there", "put"
    };

    private static string? DescribeSingleElement(AutomationElement element)
    {
        try
        {
            var controlType = element.ControlType.ToString();
            if (IsPasswordField(element))
            {
                return $"{controlType} (password field, contents withheld)";
            }

            var name = Shorten(element.Name?.Trim(), 90);
            var automationId = Shorten(element.AutomationId?.Trim(), 90);
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(automationId))
            {
                return controlType;
            }

            var label = string.IsNullOrWhiteSpace(name) ? automationId : name;
            var enabled = element.IsEnabled ? string.Empty : ", disabled";
            return $"{controlType} \"{label}\"{enabled}";
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AutomationElement> FindElementChain(
        AutomationElement root,
        System.Drawing.Point point,
        CancellationToken cancellationToken)
    {
        var chain = new List<AutomationElement> { root };
        var current = root;
        for (var depth = 0; depth < 20; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationElement? next = null;
            double smallestArea = double.MaxValue;
            foreach (var child in current.FindAllChildren())
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
                    // Ignore stale child elements and keep looking at siblings.
                }
            }

            if (next is null)
            {
                break;
            }

            chain.Add(next);
            current = next;
        }

        return chain;
    }

    /// <summary>
    /// Records that a password box is at this spot, and nothing about it.
    /// Metis can still point at it; it cannot describe it.
    /// </summary>
    private static void AddPasswordPlaceholder(
        AutomationElement element,
        ScreenCapture capture,
        ICollection<UiElementDescriptor> descriptors)
    {
        try
        {
            var bounds = element.BoundingRectangle;
            var sourceWidth = Math.Max(1, capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width);
            var sourceHeight = Math.Max(1, capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height);
            var centerX = bounds.Left + (bounds.Width / 2d);
            var centerY = bounds.Top + (bounds.Height / 2d);
            descriptors.Add(new UiElementDescriptor(
                null,
                "password field (contents withheld)",
                element.ControlType.ToString(),
                Math.Clamp((int)Math.Round((centerX - capture.ScreenLeft) / sourceWidth * 1000d), 0, 1000),
                Math.Clamp((int)Math.Round((centerY - capture.ScreenTop) / sourceHeight * 1000d), 0, 1000),
                element.IsEnabled));
        }
        catch
        {
            // If it cannot even be measured, leaving it out entirely is right.
        }
    }

    private static void AddDescriptor(
        AutomationElement element,
        ScreenCapture capture,
        ICollection<UiElementDescriptor> descriptors)
    {
        try
        {
            if (element.Properties.ProcessId.ValueOrDefault == Environment.ProcessId)
            {
                return;
            }

            // A password box is reported as being there and nothing else. The
            // model needs to know a login field exists so it can point at it;
            // it must never learn the automation id, the label, or anything
            // else that could carry what was typed.
            if (IsPasswordField(element))
            {
                AddPasswordPlaceholder(element, capture, descriptors);
                return;
            }

            var automationId = element.AutomationId?.Trim();
            var name = element.Name?.Trim();
            if (string.IsNullOrWhiteSpace(automationId) && string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var bounds = element.BoundingRectangle;
            var sourceWidth = Math.Max(1, capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width);
            var sourceHeight = Math.Max(1, capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height);
            var centerX = bounds.Left + (bounds.Width / 2d);
            var centerY = bounds.Top + (bounds.Height / 2d);
            var normalizedX = (int)Math.Round((centerX - capture.ScreenLeft) / sourceWidth * 1000d);
            var normalizedY = (int)Math.Round((centerY - capture.ScreenTop) / sourceHeight * 1000d);
            descriptors.Add(new UiElementDescriptor(
                automationId,
                Shorten(name, 100),
                element.ControlType.ToString(),
                Math.Clamp(normalizedX, 0, 1000),
                Math.Clamp(normalizedY, 0, 1000),
                element.IsEnabled));
        }
        catch
        {
            // Individual stale/inaccessible elements should not discard the
            // rest of the window's accessibility snapshot.
        }
    }

    private static string? SafeAutomationId(AutomationElement element)
    {
        try
        {
            return element.AutomationId;
        }
        catch
        {
            return null;
        }
    }

    private static (int? X, int? Y) GetCenter(AutomationElement element)
    {
        try
        {
            var bounds = element.BoundingRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return (null, null);
            }

            return (
                (int)Math.Round(bounds.Left + (bounds.Width / 2d)),
                (int)Math.Round(bounds.Top + (bounds.Height / 2d)));
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? Shorten(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength
                ? value
                : value[..maximumLength];

    private sealed record UiElementDescriptor(
        string? AutomationId,
        string? Name,
        string ControlType,
        int X,
        int Y,
        bool IsEnabled);
}
