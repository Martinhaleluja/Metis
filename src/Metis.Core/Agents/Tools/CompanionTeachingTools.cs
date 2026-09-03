using System.Globalization;
using System.Text.Json;
using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Delegate hooks providing companion teaching actions to agent tools.
/// </summary>
public sealed class CompanionTeachingHooks
{
    public Func<AnnotationTarget, Task<ResolvedAnnotation?>>? ResolveAnnotationAsync { get; init; }
    public Action<GuidanceOverlayRequest>? ShowOverlay { get; init; }
    public Action<CompanionGuidance>? ShowCompanionGuidance { get; init; }
    public Action<CompanionDemo>? ShowCompanionDemo { get; init; }
    public Action? ClearOverlay { get; init; }
    public Func<DiagramCanvas>? GetDiagramCanvas { get; init; }
}

/// <summary>
/// Tool that flies the companion character to point directly at an on-screen element or coordinate.
/// </summary>
public sealed class PointAtElementTool : IAgentTool
{
    private readonly CompanionTeachingHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "point_at_element",
        Description: "Flies the companion character to point at a specific UI element, text span, or screen coordinate.",
        Category: "companion_teaching",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("element_name", "string", "Name of the UI accessibility element to point at", Required: false),
            new("text", "string", "Exact text span on screen to locate and point at", Required: false),
            new("label", "string", "Short text badge to show above the companion", Required: false),
            new("x", "number", "Normalized X coordinate (0-1000) if element name is unknown", Required: false),
            new("y", "number", "Normalized Y coordinate (0-1000) if element name is unknown", Required: false)
        ]);

    public PointAtElementTool(CompanionTeachingHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var elementName = arguments.GetValueOrDefault("element_name")?.ToString();
        var text = arguments.GetValueOrDefault("text")?.ToString();
        var label = arguments.GetValueOrDefault("label")?.ToString() ?? elementName ?? text ?? "Here";

        int? normX = ParseNormalizedCoord(arguments.GetValueOrDefault("x"));
        int? normY = ParseNormalizedCoord(arguments.GetValueOrDefault("y"));

        if (string.IsNullOrWhiteSpace(elementName) && string.IsNullOrWhiteSpace(text) && (!normX.HasValue || !normY.HasValue))
        {
            return AgentToolResult.Fail("Provide at least an 'element_name', 'text', or 'x' and 'y' coordinates.");
        }

        var target = new AnnotationTarget(
            Scope: !string.IsNullOrWhiteSpace(text) ? AnnotationScope.TextSpan : AnnotationScope.Control,
            NormalizedX: normX ?? 500,
            NormalizedY: normY ?? 500,
            NormalizedWidth: 0,
            NormalizedHeight: 0,
            Label: label,
            ElementName: elementName,
            Text: text);

        ResolvedAnnotation? resolved = null;
        if (_hooks.ResolveAnnotationAsync is not null)
        {
            resolved = await _hooks.ResolveAnnotationAsync(target);
        }

        var screenX = resolved?.ScreenX ?? target.NormalizedX;
        var screenY = resolved?.ScreenY ?? target.NormalizedY;

        var guidance = new CompanionGuidance(
            ScreenX: screenX,
            ScreenY: screenY,
            Cue: label,
            HoldDuration: TimeSpan.FromSeconds(6));

        _hooks.ShowCompanionGuidance?.Invoke(guidance);

        var mark = resolved?.ToMark() ?? new GuidanceMark(
            Kind: GuidanceMarkKind.FocusRing,
            ScreenX: screenX,
            ScreenY: screenY,
            Label: label);

        _hooks.ShowOverlay?.Invoke(new GuidanceOverlayRequest([mark], DimBackground: false, HoldDuration: TimeSpan.FromSeconds(6)));

        return AgentToolResult.Ok($"Companion pointed at '{label}' at ({screenX}, {screenY}).");
    }

    private static int? ParseNormalizedCoord(object? val)
    {
        if (val is null) return null;
        if (double.TryParse(val.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            if (d > 0.0 && d <= 1.0)
            {
                return (int)(d * 1000);
            }
            return (int)d;
        }
        return null;
    }
}

/// <summary>
/// Tool that draws visual overlays (focus rings, capsules, brackets, arrows, underlines) on screen.
/// </summary>
public sealed class HighlightRegionTool : IAgentTool
{
    private readonly CompanionTeachingHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "highlight_region",
        Description: "Draws an overlay mark (ring, box, capsule, bracket, arrow, or underline) on the screen to guide the user.",
        Category: "companion_teaching",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("kind", "string", "Mark kind: 'focus_ring', 'box', 'capsule', 'bracket', 'arrow', 'underline', 'label'", Required: false, DefaultValue: "focus_ring"),
            new("x", "number", "Screen pixel or normalized X coordinate of target", Required: true),
            new("y", "number", "Screen pixel or normalized Y coordinate of target", Required: true),
            new("width", "number", "Width of region (optional)", Required: false, DefaultValue: 0),
            new("height", "number", "Height of region (optional)", Required: false, DefaultValue: 0),
            new("label", "string", "Label text badge displayed on the highlight", Required: false),
            new("step_number", "number", "Numbered step badge (optional)", Required: false, DefaultValue: 0),
            new("dim_background", "boolean", "Dim the rest of the screen to focus attention (optional)", Required: false, DefaultValue: false)
        ]);

    public HighlightRegionTool(CompanionTeachingHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var kindStr = arguments.GetValueOrDefault("kind")?.ToString();
        var markKind = GuidanceMarkKinds.Parse(kindStr);

        var x = ParseInt(arguments.GetValueOrDefault("x")) ?? 0;
        var y = ParseInt(arguments.GetValueOrDefault("y")) ?? 0;
        var width = ParseInt(arguments.GetValueOrDefault("width")) ?? 0;
        var height = ParseInt(arguments.GetValueOrDefault("height")) ?? 0;
        var label = arguments.GetValueOrDefault("label")?.ToString();
        var stepNum = ParseInt(arguments.GetValueOrDefault("step_number")) ?? 0;
        var dim = arguments.GetValueOrDefault("dim_background")?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

        var mark = new GuidanceMark(
            Kind: markKind,
            ScreenX: x,
            ScreenY: y,
            Width: width,
            Height: height,
            Label: label,
            StepNumber: stepNum);

        var req = new GuidanceOverlayRequest([mark], DimBackground: dim, HoldDuration: TimeSpan.FromSeconds(8));
        _hooks.ShowOverlay?.Invoke(req);

        return Task.FromResult(AgentToolResult.Ok($"Highlighted region at ({x}, {y}) with mark '{markKind}'."));
    }

    private static int? ParseInt(object? val)
    {
        if (val is null) return null;
        if (double.TryParse(val.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return (int)d;
        }
        return null;
    }
}

/// <summary>
/// Tool that draws abstract blackboard vector diagrams (polygons, circles, lines, vectors, waves, labels).
/// </summary>
public sealed class DrawDiagramTool : IAgentTool
{
    private readonly CompanionTeachingHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "draw_diagram",
        Description: "Draws a 2D blackboard vector diagram (polygon, circle, line, arrow/vector, sine wave, or label) on the canvas.",
        Category: "companion_teaching",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("shape", "string", "Shape type: 'polygon', 'circle', 'line', 'arrow', 'wave', 'label'", Required: true),
            new("center_x", "number", "Normalized X center (0-1000)", Required: false, DefaultValue: 500),
            new("center_y", "number", "Normalized Y center (0-1000)", Required: false, DefaultValue: 500),
            new("size", "number", "Normalized size/radius (0-1000)", Required: false, DefaultValue: 300),
            new("sides", "number", "Number of sides for polygon (3=triangle, 4=square, etc.) or cycles for wave", Required: false, DefaultValue: 3),
            new("end_x", "number", "Normalized end X for line/arrow/wave", Required: false),
            new("end_y", "number", "Normalized end Y for line/arrow/wave", Required: false),
            new("rotation_degrees", "number", "Rotation angle in degrees", Required: false, DefaultValue: 0),
            new("label", "string", "Descriptive label for the diagram element", Required: false)
        ]);

    public DrawDiagramTool(CompanionTeachingHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var shapeStr = arguments.GetValueOrDefault("shape")?.ToString();
        var shape = DiagramShapeKinds.Parse(shapeStr);
        if (shape == DiagramShapeKind.None)
        {
            return Task.FromResult(AgentToolResult.Fail($"Unknown diagram shape '{shapeStr}'. Use 'polygon', 'circle', 'line', 'arrow', 'wave', or 'label'."));
        }

        var cx = ParseInt(arguments.GetValueOrDefault("center_x")) ?? 500;
        var cy = ParseInt(arguments.GetValueOrDefault("center_y")) ?? 500;
        var size = ParseInt(arguments.GetValueOrDefault("size")) ?? 300;
        var sides = ParseInt(arguments.GetValueOrDefault("sides")) ?? 3;
        var ex = ParseInt(arguments.GetValueOrDefault("end_x"));
        var ey = ParseInt(arguments.GetValueOrDefault("end_y"));
        var rot = ParseInt(arguments.GetValueOrDefault("rotation_degrees")) ?? 0;
        var label = arguments.GetValueOrDefault("label")?.ToString();

        var step = new LessonStep(
            Instruction: label ?? $"Drawing {shapeStr}",
            Why: "Visual explanation",
            DoneWhen: "Rendered",
            DiagramShapeKind: shapeStr,
            DiagramCenterX: cx,
            DiagramCenterY: cy,
            DiagramSize: size,
            DiagramSides: sides,
            DiagramEndX: ex ?? -1,
            DiagramEndY: ey ?? -1,
            DiagramRotationDegrees: rot,
            TargetLabel: label);

        var canvas = _hooks.GetDiagramCanvas?.Invoke() ?? DiagramCanvas.Centred(0, 0, 1920, 1080, 0.7);
        var mark = DiagramMarkBuilder.Build(step, canvas);

        if (mark is null)
        {
            return Task.FromResult(AgentToolResult.Fail("Failed to build diagram mark."));
        }

        var req = new GuidanceOverlayRequest([mark], DimBackground: true, HoldDuration: TimeSpan.FromSeconds(12), Accumulate: true);
        _hooks.ShowOverlay?.Invoke(req);

        return Task.FromResult(AgentToolResult.Ok($"Rendered diagram shape '{shapeStr}' on canvas."));
    }

    private static int? ParseInt(object? val)
    {
        if (val is null) return null;
        if (double.TryParse(val.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return (int)d;
        }
        return null;
    }
}

/// <summary>
/// Tool that demonstrates a gesture (e.g. mouse drag, lasso path, swipe) using the companion as a ghost cursor.
/// </summary>
public sealed class DemonstrateGestureTool : IAgentTool
{
    private readonly CompanionTeachingHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "demonstrate_gesture",
        Description: "Moves the companion as a ghost cursor along a path to demonstrate a swipe, drag, or movement gesture.",
        Category: "companion_teaching",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("points", "string", "JSON array of points: [[x1, y1], [x2, y2], ...] or [{\"x\": 10, \"y\": 20}, ...]", Required: true),
            new("label", "string", "Short label describing the gesture", Required: false),
            new("hold_at_end", "boolean", "Whether to stay at the end point (default false)", Required: false, DefaultValue: false)
        ]);

    public DemonstrateGestureTool(CompanionTeachingHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPoints = arguments.GetValueOrDefault("points")?.ToString();
        if (string.IsNullOrWhiteSpace(rawPoints))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'points' is required."));
        }

        var label = arguments.GetValueOrDefault("label")?.ToString();
        var hold = arguments.GetValueOrDefault("hold_at_end")?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

        var pointsList = new List<GuidancePoint>();
        try
        {
            using var doc = JsonDocument.Parse(rawPoints);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
                    {
                        var px = item[0].GetInt32();
                        var py = item[1].GetInt32();
                        pointsList.Add(new GuidancePoint(px, py));
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        var xProp = item.TryGetProperty("x", out var xVal) ? xVal.GetInt32() : 0;
                        var yProp = item.TryGetProperty("y", out var yVal) ? yVal.GetInt32() : 0;
                        pointsList.Add(new GuidancePoint(xProp, yProp));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to parse points array: {ex.Message}"));
        }

        if (pointsList.Count < 2)
        {
            return Task.FromResult(AgentToolResult.Fail("At least 2 points are required for a gesture demonstration."));
        }

        var demo = new CompanionDemo(pointsList, label, hold);
        _hooks.ShowCompanionDemo?.Invoke(demo);

        return Task.FromResult(AgentToolResult.Ok($"Demonstrating gesture with {pointsList.Count} points: '{label}'."));
    }
}

/// <summary>
/// Tool that clears all active screen overlays and blackboard diagrams.
/// </summary>
public sealed class ClearAnnotationsTool : IAgentTool
{
    private readonly CompanionTeachingHooks _hooks;

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "clear_annotations",
        Description: "Clears all active on-screen highlight marks, step badges, and blackboard diagrams.",
        Category: "companion_teaching",
        RiskLevel: AgentRiskLevel.Low,
        Parameters: []);

    public ClearAnnotationsTool(CompanionTeachingHooks? hooks = null)
    {
        _hooks = hooks ?? new();
    }

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        _hooks.ClearOverlay?.Invoke();
        return Task.FromResult(AgentToolResult.Ok("Cleared active on-screen annotations."));
    }
}
