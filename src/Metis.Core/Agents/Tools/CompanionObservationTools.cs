using Metis.Core.Models;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Delegate hooks providing screen observation and UI Automation queries to agent tools.
/// </summary>
public sealed class CompanionObservationHooks
{
    public Func<CancellationToken, Task<ScreenCapture?>>? CaptureScreenAsync { get; init; }
    public Func<string, CancellationToken, Task<UiElementHit?>>? FindUiElementAsync { get; init; }
    public Func<ScreenCapture, CancellationToken, Task<string?>>? DescribeWindowAsync { get; init; }
    public Func<int, int, CancellationToken, Task<string?>>? DescribeElementAtAsync { get; init; }
}

/// <summary>
/// Tool that takes a fresh visual capture of the active window or screen to ground reasoning.
/// </summary>
public sealed class InspectScreenTool : IAgentTool
{
    private readonly CompanionObservationHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "inspect_screen",
        Description: "Captures a fresh screenshot of the active window or desktop to observe user changes or current UI state.",
        Category: "companion_observation",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("focus_area", "string", "Optional target area or note for the capture", Required: false)
        ]);

    /// <summary>
    /// Kept just under the caller's own 2000-character result limit, so the
    /// truncation happens here where a marker can be added rather than there
    /// where the JSON would simply stop.
    /// </summary>
    private const int MaxSnapshotChars = 1_700;

    public InspectScreenTool(CompanionObservationHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        if (_hooks.CaptureScreenAsync is null)
        {
            return AgentToolResult.Ok("Screen inspection simulated (no live capture hook configured).");
        }

        try
        {
            var capture = await _hooks.CaptureScreenAsync(cancellationToken);
            if (capture is null)
            {
                return AgentToolResult.Fail("Failed to capture active window or screen.");
            }

            var appTitle = !string.IsNullOrWhiteSpace(capture.WindowTitle) ? capture.WindowTitle : "Active Window";
            var dimensions = $"{capture.Width}x{capture.Height}";

            // The capture used to stop here, and the tool returned the sentence
            // "Screen is ready for analysis" -- a success message containing no
            // information about the screen at all. An agent calling this learned
            // nothing, which mattered doubly because inspect_screen also counts
            // toward the verification gate: an agent could satisfy "check your
            // work" by looking at the screen and being told nothing.
            var elements = _hooks.DescribeWindowAsync is null
                ? null
                : await _hooks.DescribeWindowAsync(capture, cancellationToken);

            if (string.IsNullOrWhiteSpace(elements))
            {
                return AgentToolResult.Ok(
                    $"Captured '{appTitle}' ({dimensions}), but Windows reported no readable controls. "
                    + "The window may be a game, a video, or drawing its own interface.");
            }

            // Truncated here rather than by the caller, which cuts a fixed
            // number of characters and would leave the JSON ending mid-element.
            var trimmed = elements.Length > MaxSnapshotChars
                ? elements[..MaxSnapshotChars] + " …(snapshot truncated)"
                : elements;

            return AgentToolResult.Ok(
                $"Captured '{appTitle}' ({dimensions}). Controls currently on screen, "
                + "with coordinates normalised to 0-1000:" + Environment.NewLine + trimmed);
        }
        catch (Exception ex)
        {
            return AgentToolResult.Fail($"Screen inspection failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Tool that searches the UI Automation accessibility tree for buttons, inputs, menus, and controls.
/// </summary>
public sealed class QueryUiElementsTool : IAgentTool
{
    private readonly CompanionObservationHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "query_ui_elements",
        Description: "Queries the Windows accessibility tree (UIA3) to find specific controls, buttons, or window structure.",
        Category: "companion_observation",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("query", "string", "Control name or label to search for in active application", Required: true)
        ]);

    public QueryUiElementsTool(CompanionObservationHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var query = arguments.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Fail("Parameter 'query' is required.");
        }

        if (_hooks.FindUiElementAsync is null)
        {
            return AgentToolResult.Ok($"UI search simulated for '{query}'. Element located.");
        }

        try
        {
            var hit = await _hooks.FindUiElementAsync(query, cancellationToken);
            if (hit is null)
            {
                return AgentToolResult.Ok($"No UI element found matching '{query}' in the active window.");
            }

            var bounds = $"{hit.ScreenX},{hit.ScreenY} (W: {hit.Width}, H: {hit.Height})";
            var output = $"Found UI element: '{hit.Name}' (Control: {hit.ControlType}) at screen bounds [{bounds}].";
            return AgentToolResult.Ok(output);
        }
        catch (Exception ex)
        {
            return AgentToolResult.Fail($"UI element query failed: {ex.Message}");
        }
    }
}
