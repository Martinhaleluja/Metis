using System.Text.Json;
using Metis.Core.Models;

namespace Metis.AI;

/// <summary>
/// Converts a provider's structured response into a small, bounded desktop plan.
/// Invalid or non-structured responses safely degrade to speech only.
/// </summary>
public static class AssistantPlanParser
{
    private const int MaxBubbleCueLength = 80;
    private const int MaxLabelLength = 80;
    private const int MaxGoalLength = 2_000;

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
            var goal = Shorten(ReadString(root, "goal"), MaxGoalLength);

            // What the user said, when they said it rather than typed it. Metis
            // reads the intent from these words itself; the model is only
            // reporting what was on the recording.
            var heardText = Shorten(
                ReadString(root, "heard_text", "heardText", "transcript"), MaxGoalLength);
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
            // Where the mark goes. These used to ride on a pointer action;
            // with no actions left they belong to the annotation itself.
            var markX = ReadInt(annotationRoot, "x", "cx") ?? -1;
            var markY = ReadInt(annotationRoot, "y", "cy") ?? -1;
            if (!hasScreenshot || markX is < 0 or > 1000 || markY is < 0 or > 1000)
            {
                markX = -1;
                markY = -1;
            }

            var markWidth = hasScreenshot ? Math.Clamp(ReadInt(annotationRoot, "w", "width") ?? 0, 0, 1000) : 0;
            var markHeight = hasScreenshot ? Math.Clamp(ReadInt(annotationRoot, "h", "height") ?? 0, 0, 1000) : 0;
            var markLabel = Shorten(ReadString(annotationRoot, "label", "target"), MaxLabelLength);

            var steps = ReadLessonSteps(root, hasScreenshot);
            var spawnRequests = ReadSpawnGoals(root);
            var needsAnotherLook = ReadBoolean(root, "needs_another_look", "needsAnotherLook");
            var lookFor = Shorten(ReadString(root, "look_for", "lookFor"), MaxLabelLength);
            if (hasScreenshot && (markX >= 0 || (steps?.Any(s => s.HasTarget) == true)))
            {
                screenObserved = true;
            }

            // A malformed structured response should still produce useful speech, but
            // never expose the raw JSON to Metis's speech engine or bubble.
            if (spokenText.Length == 0)
            {
                spokenText = bubbleCue ?? "I couldn't understand that response. Please try again.";
            }

            return new AssistantPlan(
                spokenText,
                bubbleCue,
                screenObserved,
                goal,
                steps,
                scopeName,
                elementName,
                annotationText,
                markX,
                markY,
                markWidth,
                markHeight,
                markLabel,
                heardText,
                spawnRequests,
                needsAnotherLook,
                lookFor);
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
        "\"lesson_steps\"", "\"lessonSteps\""
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
    /// How many agents one reply may ask for.
    ///
    /// Low on purpose. Everything spawned here is a real worker that costs
    /// tokens and touches the machine, and a model that has misread a request
    /// can produce a list as long as it likes. Four covers every genuine ask
    /// seen so far and bounds the damage from one that is not.
    /// </summary>
    private const int MaxSpawnRequests = 4;

    /// <summary>The longest goal that can be handed to an agent.</summary>
    private const int MaxSpawnGoalLength = 600;

    /// <summary>
    /// Reads the goals the reply wants handed to background agents.
    ///
    /// Accepts both a list of strings and a list of objects carrying a goal,
    /// because models produce both and the difference is not worth losing a
    /// request over — the same forgiveness ReadLessonSteps applies to its own
    /// alternate key names.
    /// </summary>
    private static IReadOnlyList<string>? ReadSpawnGoals(JsonElement root)
    {
        if (!TryGetProperty(root, out var array, "spawn_agents", "spawnAgents", "agents") ||
            array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var goals = new List<string>();
        foreach (var element in array.EnumerateArray())
        {
            var goal = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Object => ReadString(element, "goal", "task", "instruction", "description"),
                _ => null
            };

            goal = Shorten(goal, MaxSpawnGoalLength);
            if (string.IsNullOrWhiteSpace(goal))
            {
                continue;
            }

            goals.Add(goal.Trim());
            if (goals.Count >= MaxSpawnRequests)
            {
                break;
            }
        }

        return goals.Count > 0 ? goals : null;
    }

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

            // Not gated on hasScreenshot, unlike x/y/to_x above. Those describe
            // something inside the screenshot, so without one they mean nothing.
            // A diagram describes nothing on screen at all — gating it would
            // silently switch the feature off for anyone with screen capture
            // turned off, and buy nothing.
            var diagramShape = Shorten(ReadString(element, "diagram_shape", "diagramShape", "shape"), 24);
            var diagramCentreX = Math.Clamp(ReadInt(element, "diagram_cx", "diagramCx") ?? -1, -1, 1000);
            var diagramCentreY = Math.Clamp(ReadInt(element, "diagram_cy", "diagramCy") ?? -1, -1, 1000);
            var diagramEndX = Math.Clamp(ReadInt(element, "diagram_ex", "diagramEx") ?? -1, -1, 1000);
            var diagramEndY = Math.Clamp(ReadInt(element, "diagram_ey", "diagramEy") ?? -1, -1, 1000);
            var diagramSize = Math.Clamp(ReadInt(element, "diagram_size", "diagramSize") ?? 0, 0, 1000);
            var diagramSides = Math.Clamp(ReadInt(element, "diagram_sides", "diagramSides") ?? 0, 0, 40);
            var diagramRotation = Math.Clamp(ReadInt(element, "diagram_rotation", "diagramRotation") ?? 0, 0, 359);

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
                Shorten(ReadString(element, "text", "annotation_text"), MaxAnnotationTextLength),
                diagramShape,
                diagramCentreX,
                diagramCentreY,
                diagramEndX,
                diagramEndY,
                diagramSize,
                diagramSides,
                diagramRotation));

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
