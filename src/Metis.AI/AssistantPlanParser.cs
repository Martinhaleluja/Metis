using System.Text.Json;
using Metis.Core.Models;

namespace Metis.AI;

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

    /// <summary>
    /// A run of text to underline is a phrase, not a paragraph. Long enough for
    /// a sentence on screen, short enough that a model cannot ask Metis to mark
    /// an entire document.
    /// </summary>
    private const int MaxAnnotationTextLength = 320;

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
            return Fallback(original);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !LooksLikePlan(root))
            {
                return Fallback(original);
            }

            var spokenText = ReadString(root, "spoken_text", "spokenText") ?? string.Empty;
            var bubbleCue = Shorten(ReadString(root, "bubble_cue", "bubbleCue"), MaxBubbleCueLength);
            var screenObserved = ReadBoolean(root, "screen_observed", "screenObserved") && hasScreenshot;
            var planId = Shorten(ReadString(root, "plan_id", "planId"), MaxPlanIdLength);
            var replanNumber = Math.Clamp(ReadInt(root, "replan_number", "replanNumber") ?? 0, 0, 20);
            var status = NormalizeStatus(ReadString(root, "status"));
            var goal = Shorten(ReadString(root, "goal"), MaxGoalLength);
            // The annotation may arrive nested under "annotation" or flattened
            // onto the reply. Both are accepted because both are natural things
            // for a model to produce, and rejecting one costs a mark on screen.
            var isNested = TryGetProperty(root, out var nested, "annotation") &&
                           nested.ValueKind == JsonValueKind.Object;
            var annotationRoot = isNested ? nested : root;

            var scopeName = Shorten(ReadString(annotationRoot, "scope", "highlight", "mark"), 24);
            var elementName = Shorten(ReadString(annotationRoot, "element", "element_name", "control"), MaxLabelLength);

            // A bare "text" is only the annotation's words when it sits inside
            // an annotation object. At the top level that key belongs to any
            // number of other things, and reading it here would underline a
            // phrase the model never pointed at.
            var annotationText = Shorten(
                isNested
                    ? ReadString(annotationRoot, "text", "annotation_text", "annotationText")
                    : ReadString(root, "annotation_text", "annotationText"),
                MaxAnnotationTextLength);
            // Check both the originating request and the returned plan. Gemini can
            // receive voice directly, so the generated text may be the only local
            // representation of a spoken sensitive request.
            var blockRestrictedClicks = ContainsRestrictedClickIntent(userRequest) ||
                                        ContainsRestrictedClickIntent(json);
            var actions = ReadActions(root, hasScreenshot, blockRestrictedClicks);
            var steps = ReadLessonSteps(root, hasScreenshot);

            // A malformed structured response should still produce useful speech, but
            // never expose the raw JSON to Metis's speech engine or bubble.
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
                goal,
                steps,
                scopeName,
                elementName,
                annotationText);
        }
        catch (JsonException)
        {
            return Fallback(original);
        }
    }

    /// <summary>
    /// What Metis says when a structured reply came back too broken to read and
    /// nothing could be rescued from it.
    /// </summary>
    private const string UnreadableReplyMessage =
        "That answer came back cut off. Ask me again and I'll have another go.";

    /// <summary>
    /// Markers that identify a reply as one of Metis's own plans rather than
    /// prose that merely happens to contain a brace. The distinction matters:
    /// a user who asks to be shown some JSON should get it read back, whereas a
    /// half-written plan is Metis's own plumbing and must never be spoken.
    /// </summary>
    private static readonly string[] PlanMarkers =
    [
        "\"spoken_text\"", "\"spokenText\"",
        "\"bubble_cue\"", "\"bubbleCue\"",
        "\"plan_id\"", "\"planId\"",
        "\"screen_observed\"", "\"screenObserved\"",
        "\"actions\"", "\"lesson_steps\"", "\"lessonSteps\""
    ];

    private static readonly string[] SpokenTextKeys = ["\"spoken_text\"", "\"spokenText\""];

    /// <summary>
    /// Decides what to say when the structured reply could not be read.
    ///
    /// A model's answer is truncated far more often than it is malformed, and a
    /// truncated plan usually still carries its opening field intact — so the
    /// sentence Metis was going to say is normally sitting right there in the
    /// wreckage. Rescuing it turns a failed turn into a successful one. Reading
    /// the raw JSON aloud, which is what happened before, is the one outcome
    /// that is never acceptable: it spells out every brace, quote, and colon and
    /// tells the user nothing.
    /// </summary>
    private static AssistantPlan Fallback(string original)
    {
        if (!LooksLikePlanFragment(original))
        {
            // Genuine prose. The model chose to answer in words, which is a
            // perfectly good reply and should be spoken exactly as written.
            return AssistantPlan.SpeechOnly(original);
        }

        return TrySalvageSpokenText(original, out var salvaged)
            ? AssistantPlan.SpeechOnly(salvaged)
            : AssistantPlan.SpeechOnly(UnreadableReplyMessage);
    }

    private static bool LooksLikePlanFragment(string text) =>
        PlanMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// Reads the value of "spoken_text" straight out of the text, without
    /// requiring the surrounding JSON to be complete or even valid. An
    /// unterminated string still yields everything written so far, which is
    /// exactly the case that matters when a reply was cut short mid-sentence.
    /// </summary>
    private static bool TrySalvageSpokenText(string text, out string spoken)
    {
        spoken = string.Empty;

        foreach (var key in SpokenTextKeys)
        {
            var keyIndex = text.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                continue;
            }

            var cursor = keyIndex + key.Length;
            while (cursor < text.Length && (char.IsWhiteSpace(text[cursor]) || text[cursor] == ':'))
            {
                cursor++;
            }

            if (cursor >= text.Length || text[cursor] != '"')
            {
                continue;
            }

            cursor++;
            var value = new System.Text.StringBuilder();
            while (cursor < text.Length && text[cursor] != '"')
            {
                if (text[cursor] != '\\' || cursor + 1 >= text.Length)
                {
                    value.Append(text[cursor]);
                    cursor++;
                    continue;
                }

                cursor++;
                var escape = text[cursor];
                if (escape == 'u' && cursor + 4 < text.Length &&
                    ushort.TryParse(
                        text.AsSpan(cursor + 1, 4),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var codePoint))
                {
                    value.Append((char)codePoint);
                    cursor += 5;
                    continue;
                }

                value.Append(escape switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'b' => '\b',
                    'f' => '\f',
                    _ => escape
                });
                cursor++;
            }

            var candidate = value.ToString().Trim();
            if (candidate.Length > 0)
            {
                spoken = candidate;
                return true;
            }
        }

        return false;
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

            if (kind == DesktopActionKind.RunCommand)
            {
                // Reviewed here as well as at execution: a command the policy
                // refuses outright should never reach a confirmation prompt,
                // because showing the user something they must decline teaches
                // them to click through the ones that matter.
                var command = ReadString(element, "command", "text", "value");
                var review = Metis.Core.Services.SystemCommandPolicy.Review(command);
                if (!hasScreenshot || blockRestrictedClicks || review.IsRefused)
                {
                    continue;
                }

                actions.Add(new DesktopAction(
                    kind,
                    Label: Shorten(ReadString(element, "label"), MaxLabelLength),
                    HasCoordinates: false,
                    Text: review.Command,
                    Id: actionId));
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

            // The extent is optional. Without it a mark can only be a ring;
            // with it the mark takes the target's real proportions.
            actions.Add(new DesktopAction(
                kind,
                Math.Clamp(x, 0, 1000),
                Math.Clamp(y, 0, 1000),
                Label: Shorten(ReadString(element, "label"), MaxLabelLength),
                AutomationId: automationId,
                HasCoordinates: true,
                Id: actionId,
                NormalizedWidth: Math.Clamp(ReadInt(element, "w", "width") ?? 0, 0, 1000),
                NormalizedHeight: Math.Clamp(ReadInt(element, "h", "height") ?? 0, 0, 1000)));
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
            "run_command" or "runcommand" or "command" or "shell" => DesktopActionKind.RunCommand,
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
            or "observe" or "reobserve" or "verify" or "finish" or "done"
            or "run_command" or "runcommand" or "command" or "shell";
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

    private const int MaxLessonSteps = 12;

    /// <summary>
    /// Reads the steps the learner performs themselves. These are never
    /// executed, so they carry no risk of a bad coordinate causing a stray
    /// click — an out-of-range target simply loses its highlight and the step
    /// still reads as instruction.
    /// </summary>
    private static IReadOnlyList<LessonStep>? ReadLessonSteps(JsonElement root, bool hasScreenshot)
    {
        if (!TryGetProperty(root, out var array, "steps", "lesson_steps", "lessonSteps") ||
            array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var steps = new List<LessonStep>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var instruction = Shorten(ReadString(element, "instruction", "step", "do"), 240);
            if (string.IsNullOrWhiteSpace(instruction))
            {
                continue;
            }

            var x = ReadInt(element, "x") ?? -1;
            var y = ReadInt(element, "y") ?? -1;
            if (!hasScreenshot || x is < 0 or > 1000 || y is < 0 or > 1000)
            {
                x = -1;
                y = -1;
            }

            // Size and gesture end are optional; a step without them still
            // teaches, it just points instead of tracing a shape or a path.
            var width = Math.Clamp(ReadInt(element, "w", "width") ?? 0, 0, 1000);
            var height = Math.Clamp(ReadInt(element, "h", "height") ?? 0, 0, 1000);
            var dragToX = ReadInt(element, "to_x", "toX", "drag_to_x") ?? -1;
            var dragToY = ReadInt(element, "to_y", "toY", "drag_to_y") ?? -1;
            if (!hasScreenshot || dragToX is < 0 or > 1000 || dragToY is < 0 or > 1000)
            {
                dragToX = -1;
                dragToY = -1;
            }

            steps.Add(new LessonStep(
                instruction!,
                Shorten(ReadString(element, "why", "reason"), 240),
                Shorten(ReadString(element, "done_when", "doneWhen", "verify"), 240),
                x,
                y,
                Shorten(ReadString(element, "label", "target"), 60),
                Shorten(ReadString(element, "scope", "highlight", "mark"), 24),
                hasScreenshot ? width : 0,
                hasScreenshot ? height : 0,
                dragToX,
                dragToY,
                Shorten(ReadString(element, "element", "element_name", "control"), MaxLabelLength),
                Shorten(ReadString(element, "text", "annotation_text"), MaxAnnotationTextLength)));

            if (steps.Count >= MaxLessonSteps)
            {
                break;
            }
        }

        return steps.Count == 0 ? null : steps;
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
