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
public sealed class FlaUiAutomationService : IUiAutomationService
{
    private const int MaximumSnapshotElements = 120;

    public Task<string?> DescribeWindowAsync(
        ScreenCapture capture,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DescribeWindow(capture, cancellationToken), cancellationToken);

    public Task<string?> DescribeElementAtAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DescribeElementAt(screenX, screenY, cancellationToken), cancellationToken);

    public Task<UiAutomationResult> TryInvokeAsync(
        string automationId,
        ScreenCapture capture,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => TryInvoke(automationId, capture, cancellationToken), cancellationToken);

    public Task<UiAutomationResult> TryInvokeAtAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => TryInvokeAt(screenX, screenY, cancellationToken), cancellationToken);

    private static string? DescribeWindow(ScreenCapture capture, CancellationToken cancellationToken)
    {
        try
        {
            using var automation = new UIA3Automation();
            var root = capture.WindowHandle == 0
                ? automation.GetDesktop()
                : automation.FromHandle(new nint(capture.WindowHandle));
            var queue = new Queue<AutomationElement>();
            var descriptors = new List<UiElementDescriptor>(MaximumSnapshotElements);
            queue.Enqueue(root);

            while (queue.Count > 0 && descriptors.Count < MaximumSnapshotElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
    private static string? DescribeElementAt(int screenX, int screenY, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = DesktopTargetWindowLocator.FindAt(screenX, screenY);
            if (window == nint.Zero)
            {
                return null;
            }

            using var automation = new UIA3Automation();
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

    private static string? DescribeSingleElement(AutomationElement element)
    {
        try
        {
            var name = Shorten(element.Name?.Trim(), 90);
            var controlType = element.ControlType.ToString();
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

    private static UiAutomationResult TryInvoke(
        string automationId,
        ScreenCapture capture,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return new UiAutomationResult(false, "No usable AutomationElement ID was supplied.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var automation = new UIA3Automation();
            var root = capture.WindowHandle == 0
                ? automation.GetDesktop()
                : automation.FromHandle(new nint(capture.WindowHandle));
            var normalizedId = automationId.Trim();
            var element = string.Equals(SafeAutomationId(root), normalizedId, StringComparison.Ordinal)
                ? root
                : root.FindFirstDescendant(condition => condition.ByAutomationId(normalizedId));
            if (element is null)
            {
                return new UiAutomationResult(false, $"UI Automation could not find '{normalizedId}'.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return TryPerformDefaultAction(element, $"AutomationElement '{normalizedId}'");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new UiAutomationResult(
                false,
                $"UI Automation could not invoke '{automationId.Trim()}': {exception.Message}");
        }
    }

    private static UiAutomationResult TryInvokeAt(
        int screenX,
        int screenY,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = DesktopTargetWindowLocator.FindAt(screenX, screenY);
            if (window == nint.Zero)
            {
                return new UiAutomationResult(false, "No non-Metis application exists at that desktop coordinate.");
            }

            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            var chain = FindElementChain(root, new System.Drawing.Point(screenX, screenY), cancellationToken);
            for (var index = chain.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = TryPerformDefaultAction(chain[index], "the control at the requested coordinate");
                if (result.Success)
                {
                    return result with { ScreenX = screenX, ScreenY = screenY };
                }
            }

            return new UiAutomationResult(
                false,
                "The control at that coordinate does not expose a cursorless UI Automation action.",
                screenX,
                screenY);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new UiAutomationResult(
                false,
                $"UI Automation could not invoke the control at ({screenX}, {screenY}): {exception.Message}",
                screenX,
                screenY);
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

    private static UiAutomationResult TryPerformDefaultAction(AutomationElement element, string description)
    {
        var (screenX, screenY) = GetCenter(element);
        try
        {
            if (element.Patterns.Invoke.TryGetPattern(out var invokePattern))
            {
                invokePattern.Invoke();
                return new UiAutomationResult(true, $"Invoked {description} without moving the Windows pointer.", screenX, screenY);
            }

            if (element.Patterns.SelectionItem.TryGetPattern(out var selectionPattern))
            {
                selectionPattern.Select();
                return new UiAutomationResult(true, $"Selected {description} without moving the Windows pointer.", screenX, screenY);
            }

            if (element.Patterns.Toggle.TryGetPattern(out var togglePattern))
            {
                togglePattern.Toggle();
                return new UiAutomationResult(true, $"Toggled {description} without moving the Windows pointer.", screenX, screenY);
            }

            if (element.Patterns.ExpandCollapse.TryGetPattern(out var expandPattern))
            {
                expandPattern.Expand();
                return new UiAutomationResult(true, $"Expanded {description} without moving the Windows pointer.", screenX, screenY);
            }
        }
        catch
        {
            // The caller may try an ancestor or the background-message fallback.
        }

        return new UiAutomationResult(false, $"{description} has no usable cursorless action.", screenX, screenY);
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
