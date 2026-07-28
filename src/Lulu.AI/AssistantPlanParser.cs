using System.Text.Json;
using Lulu.Core.Models;

namespace Lulu.AI;

/// <summary>
/// Converts a provider's structured response into a small, bounded desktop plan.
/// Invalid or non-structured responses safely degrade to speech only.
/// </summary>
public static class AssistantPlanParser
{
    private const int MaxActions = 6;
    private const int MaxWaitMilliseconds = 10_000;
    private const int MaxBubbleCueLength = 80;
    private const int MaxLabelLength = 80;
    private const int MaxAutomationIdLength = 160;
    private const int MaxTypedTextLength = 4_000;
    private const int MaxAppNameLength = 100;
    private const int MaxUrlLength = 2_048;
    private const int MaxKeyLength = 32;
    private const int MaxActionIdLength = 80;
    private const int MaxPlanIdLength = 120;
    private const int MaxGoalLength = 2_000;
    private const int MaxExpectedStateLength = 500;

    private static readonly string[] RestrictedClickTerms =
    [
        "buy", "purchase", "checkout", "place order", "pay now", "confirm payment",
        "delete", "remove permanently", "empty recycle bin", "uninstall",
        "submit", "send email", "send message", "publish", "post now",
        "permission", "privacy", "security", "administrator", "admin prompt",
        "user account control", "uac", "firewall", "antivirus", "password",
        "passcode", "two-factor", "2fa", "credential"
    ];

    public static AssistantPlan Parse(
        string? responseText,
        bool hasScreenshot,
        string? userRequest = null)
    {
        var original = responseText?.Trim() ?? string.Empty;
        if (original.Length == 0 || !TryExtractJson(original, out var json))
        {
            return AssistantPlan.SpeechOnly(original);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !LooksLikePlan(root))
            {
                return AssistantPlan.SpeechOnly(original);
            }

            var spokenText = ReadString(root, "spoken_text", "spokenText") ?? string.Empty;
            var bubbleCue = Shorten(ReadString(root, "bubble_cue", "bubbleCue"), MaxBubbleCueLength);
            var screenObserved = ReadBoolean(root, "screen_observed", "screenObserved") && hasScreenshot;
            var planId = Shorten(ReadString(root, "plan_id", "planId"), MaxPlanIdLength);
            var replanNumber = Math.Clamp(ReadInt(root, "replan_number", "replanNumber") ?? 0, 0, 20);
            var status = NormalizeStatus(ReadString(root, "status"));
            var goal = Shorten(ReadString(root, "goal"), MaxGoalLength);
            // Check both the originating request and the returned plan. Gemini can
            // receive voice directly, so the generated text may be the only local
            // representation of a spoken sensitive request.
            var blockRestrictedClicks = ContainsRestrictedClickIntent(userRequest) ||
                                        ContainsRestrictedClickIntent(json);
            var actions = ReadActions(root, hasScreenshot, blockRestrictedClicks);

            // A malformed structured response should still produce useful speech, but
            // never expose the raw JSON to Lulu's speech engine or bubble.
            if (spokenText.Length == 0)
            {
                spokenText = bubbleCue ?? "I couldn't understand that response. Please try again.";
            }

            return new AssistantPlan(
                spokenText,
                bubbleCue,
                actions,
                screenObserved,
                planId,
                replanNumber,
                status,
                goal);
        }
        catch (JsonException)
        {
            return AssistantPlan.SpeechOnly(original);
        }
    }

    private static IReadOnlyList<DesktopAction> ReadActions(
        JsonElement root,
        bool hasScreenshot,
        bool blockRestrictedClicks)
    {
        if (!TryGetProperty(root, out var actionsElement, "actions") ||
            actionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var actions = new List<DesktopAction>(MaxActions);
        foreach (var element in actionsElement.EnumerateArray())
        {
            if (actions.Count >= MaxActions)
            {
                break;
            }

            if (element.ValueKind != JsonValueKind.Object ||
                !TryReadActionKind(element, out var kind))
            {
                continue;
            }

            var actionId = Shorten(ReadString(element, "id", "action_id", "actionId"), MaxActionIdLength)
                           ?? $"step-{actions.Count + 1}";

            if (kind == DesktopActionKind.Wait)
            {
                var delay = ReadInt(element, "delay_ms", "delayMilliseconds", "delay") ?? 500;
                actions.Add(new DesktopAction(
                    kind,
                    DelayMilliseconds: Math.Clamp(delay, 0, MaxWaitMilliseconds),
                    Label: Shorten(ReadString(element, "label"), MaxLabelLength),
                    HasCoordinates: false,
                    Id: actionId));
                continue;
            }

            if (kind is DesktopActionKind.WaitForWindow or DesktopActionKind.WaitForElement or
                DesktopActionKind.WaitForText or DesktopActionKind.Observe or
                DesktopActionKind.Verify or DesktopActionKind.Finish)
            {
                if (!hasScreenshot && kind != DesktopActionKind.Finish)
                {
                    continue;
                }

                var timeout = Math.Clamp(
                    ReadInt(element, "timeout_ms", "timeoutMilliseconds", "timeout") ?? 2_000,
                    0,
                    MaxWaitMilliseconds);
                var text = Shorten(ReadString(element, "text", "value"), MaxTypedTextLength);
                var checkpointAutomationId = Shorten(
                    ReadString(element, "automation_id", "automationId"),
                    MaxAutomationIdLength);
                if (kind == DesktopActionKind.WaitForWindow && string.IsNullOrWhiteSpace(text) ||
                    kind == DesktopActionKind.WaitForText && string.IsNullOrWhiteSpace(text) ||
                    kind == DesktopActionKind.WaitForElement &&
                    string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(checkpointAutomationId))
                {
                    continue;
                }

                actions.Add(new DesktopAction(
                    kind,
                    Label: Shorten(ReadString(element, "label"), MaxLabelLength),
                    AutomationId: checkpointAutomationId,
                    HasCoordinates: false,
                    Text: text,
                    Id: actionId,
                    TimeoutMilliseconds: timeout,
                    ExpectedState: Shorten(
                        ReadString(element, "expected_state", "expectedState"),
                        MaxExpectedStateLength)));
                continue;
            }

            if (kind is DesktopActionKind.TypeText or DesktopActionKind.KeyPress or
                DesktopActionKind.OpenApp or DesktopActionKind.OpenUrl)
            {
                if (!hasScreenshot || blockRestrictedClicks)
                {
                    continue;
                }

                var text = kind switch
                {
                    DesktopActionKind.TypeText => Shorten(ReadString(element, "text", "value"), MaxTypedTextLength),
                    DesktopActionKind.OpenApp => Shorten(ReadString(element, "text", "app", "value"), MaxAppNameLength),
                    DesktopActionKind.OpenUrl => Shorten(ReadString(element, "text", "url", "value"), MaxUrlLength),
                    _ => null
                };
                var key = kind == DesktopActionKind.KeyPress
                    ? Shorten(ReadString(element, "key", "value"), MaxKeyLength)
                    : null;
                if ((kind == DesktopActionKind.TypeText && string.IsNullOrEmpty(text)) ||
                    (kind == DesktopActionKind.OpenApp && !IsSafeAppName(text)) ||
                    (kind == DesktopActionKind.OpenUrl && !IsSafeWebUrl(text)) ||
                    (kind == DesktopActionKind.KeyPress && !IsSupportedKey(key)))
                {
                    continue;
                }

                actions.Add(new DesktopAction(
                    kind,
                    Label: Shorten(ReadString(element, "label"), MaxLabelLength),
                    HasCoordinates: false,
                    Text: text,
                    Key: key,
                    Id: actionId));
                continue;
            }

            var automationId = Shorten(
                ReadString(element, "automation_id", "automationId"),
                MaxAutomationIdLength);
            var x = 0;
            var y = 0;
            var hasCoordinates = TryReadCoordinate(element, "x", out x) &&
                                 TryReadCoordinate(element, "y", out y);
            if (!hasScreenshot ||
                (blockRestrictedClicks && kind != DesktopActionKind.MovePointer) ||
                !hasCoordinates)
            {
                continue;
            }

            actions.Add(new DesktopAction(
                kind,
                Math.Clamp(x, 0, 1000),
                Math.Clamp(y, 0, 1000),
                Label: Shorten(ReadString(element, "label"), MaxLabelLength),
                AutomationId: automationId,
                HasCoordinates: true,
                Id: actionId));
        }

        return actions;
    }

    private static bool TryReadActionKind(JsonElement element, out DesktopActionKind kind)
    {
        var value = ReadString(element, "type", "action", "kind")?
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToLowerInvariant();

        kind = value switch
        {
            "move_pointer" or "move" or "point" or "hover" => DesktopActionKind.MovePointer,
            "left_click" or "leftclick" or "click" => DesktopActionKind.LeftClick,
            "double_click" or "doubleclick" => DesktopActionKind.DoubleClick,
            "right_click" or "rightclick" => DesktopActionKind.RightClick,
            "type_text" or "type" or "write" => DesktopActionKind.TypeText,
            "key_press" or "keypress" or "press_key" => DesktopActionKind.KeyPress,
            "open_app" or "launch_app" => DesktopActionKind.OpenApp,
            "open_url" or "navigate_url" or "navigate_to" => DesktopActionKind.OpenUrl,
            "wait" => DesktopActionKind.Wait,
            "wait_for_window" => DesktopActionKind.WaitForWindow,
            "wait_for_element" => DesktopActionKind.WaitForElement,
            "wait_for_text" => DesktopActionKind.WaitForText,
            "observe" or "reobserve" => DesktopActionKind.Observe,
            "verify" => DesktopActionKind.Verify,
            "finish" or "done" => DesktopActionKind.Finish,
            _ => default
        };

        return value is "move_pointer" or "move" or "point" or "hover"
            or "left_click" or "leftclick" or "click"
            or "double_click" or "doubleclick"
            or "right_click" or "rightclick"
            or "type_text" or "type" or "write"
            or "key_press" or "keypress" or "press_key"
            or "open_app" or "launch_app"
            or "open_url" or "navigate_url" or "navigate_to" or "wait"
            or "wait_for_window" or "wait_for_element" or "wait_for_text"
            or "observe" or "reobserve" or "verify" or "finish" or "done";
    }

    private static bool IsSafeAppName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Any(character => char.IsControl(character));

    private static bool IsSafeWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static bool IsSupportedKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().ToLowerInvariant()
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 4 ||
            parts[..^1].Any(part => part is not ("ctrl" or "control" or "shift" or "alt" or "win" or "windows")))
        {
            return false;
        }

        var key = parts[^1];
        return key.Length == 1 && char.IsLetterOrDigit(key[0]) ||
            key.Length is 2 or 3 && key[0] == 'f' && int.TryParse(key[1..], out var function) && function is >= 1 and <= 12 || key is
            "backspace" or "tab" or "enter" or "return" or "escape" or "esc" or "space" or
            "pageup" or "page_up" or "pagedown" or "page_down" or "end" or "home" or
            "left" or "up" or "right" or "down" or "delete" or "del" or
            "ctrl" or "control" or "shift" or "alt" or "win" or "windows";
    }

    private static bool TryReadCoordinate(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!TryGetProperty(element, out var coordinate, name))
        {
            return false;
        }

        if (coordinate.ValueKind == JsonValueKind.Number && coordinate.TryGetDouble(out var number) && double.IsFinite(number))
        {
            value = (int)Math.Round(Math.Clamp(number, int.MinValue, int.MaxValue));
            return true;
        }

        if (coordinate.ValueKind == JsonValueKind.String &&
            double.TryParse(
                coordinate.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number) &&
            double.IsFinite(number))
        {
            value = (int)Math.Round(Math.Clamp(number, int.MinValue, int.MaxValue));
            return true;
        }

        return false;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static bool ReadBoolean(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool LooksLikePlan(JsonElement root) =>
        root.TryGetProperty("spoken_text", out _) ||
        root.TryGetProperty("spokenText", out _) ||
        root.TryGetProperty("bubble_cue", out _) ||
        root.TryGetProperty("bubbleCue", out _) ||
        root.TryGetProperty("actions", out _);

    private static bool TryExtractJson(string text, out string json)
    {
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
            {
                json = text[(firstLineEnd + 1)..closingFence].Trim();
                return json.Length > 0;
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = text[start..(end + 1)];
            return true;
        }

        json = string.Empty;
        return false;
    }

    private static bool ContainsRestrictedClickIntent(string? request) =>
        !string.IsNullOrWhiteSpace(request) &&
        RestrictedClickTerms.Any(term => request.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string? Shorten(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength
                ? value
                : value[..maxLength];

    private static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "continue" => "continue",
        "blocked" => "blocked",
        _ => "done"
    };
}
