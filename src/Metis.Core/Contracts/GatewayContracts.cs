using System.Text.Json.Serialization;
using Metis.Core.Models;

namespace Metis.Core.Contracts;

/// <summary>
/// What the desktop app sends the gateway for one turn, and what comes back.
///
/// This lives in Metis.Core so client and gateway share one definition, for
/// exactly the reason <see cref="Metis.Core.Services.Entitlements"/> does: two
/// hand-written copies of a wire format drift, and the field that goes missing
/// is never the one you would have chosen.
///
/// It mirrors <see cref="GeminiRequest"/> field for field. A test asserts that
/// it still does, because the failure mode when it stops is silent — a new piece
/// of context gets added to <c>GeminiRequest</c>, every provider running on the
/// user's own key starts using it, and managed turns quietly get worse answers
/// with nothing in the logs to say why.
/// </summary>
public sealed record AssistRequest
{
    /// <summary>The user's question. Required.</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// A GUID the client generates, so a request can be followed from the
    /// desktop log through the gateway log into <c>usage_events</c>. Validated
    /// as a real GUID server-side before it is trusted: it ends up in a
    /// <c>uuid</c> column.
    /// </summary>
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// Which managed provider to ask. Null means "whatever this plan gets",
    /// which is what Free always gets and what most users should send.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// What this request is for, recorded against usage so agent traffic can be
    /// told apart from conversation. Free-form but conventionally "chat",
    /// "inspect", or "agent_step".
    /// </summary>
    [JsonPropertyName("feature")]
    public string Feature { get; init; } = "chat";

    /// <summary>Whether to stream the reply as server-sent events.</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;

    // ---- Everything GeminiRequest carries, minus the binary parts -----------

    [JsonPropertyName("activeWindowTitle")]
    public string? ActiveWindowTitle { get; init; }

    [JsonPropertyName("automationContext")]
    public string? AutomationContext { get; init; }

    [JsonPropertyName("screenshotMimeType")]
    public string ScreenshotMimeType { get; init; } = "image/png";

    [JsonPropertyName("screenshotWidth")]
    public int ScreenshotWidth { get; init; }

    [JsonPropertyName("screenshotHeight")]
    public int ScreenshotHeight { get; init; }

    [JsonPropertyName("screenshotScreenLeft")]
    public int ScreenshotScreenLeft { get; init; }

    [JsonPropertyName("screenshotScreenTop")]
    public int ScreenshotScreenTop { get; init; }

    [JsonPropertyName("screenshotSourceWidth")]
    public int ScreenshotSourceWidth { get; init; }

    [JsonPropertyName("screenshotSourceHeight")]
    public int ScreenshotSourceHeight { get; init; }

    [JsonPropertyName("mode")]
    public OperatingMode Mode { get; init; } = OperatingMode.Guide;

    [JsonPropertyName("activation")]
    public ActivationKind Activation { get; init; } = ActivationKind.Typed;

    [JsonPropertyName("pointer")]
    public AssistPointer? Pointer { get; init; }

    [JsonPropertyName("region")]
    public AssistRegion? Region { get; init; }

    [JsonPropertyName("taskContext")]
    public string? TaskContext { get; init; }

    [JsonPropertyName("skillContext")]
    public string? SkillContext { get; init; }

    [JsonPropertyName("userSkillPacks")]
    public string? UserSkillPacks { get; init; }

    [JsonPropertyName("chatRecall")]
    public string? ChatRecall { get; init; }

    [JsonPropertyName("recentTurns")]
    public string? RecentTurns { get; init; }

    [JsonPropertyName("academicTeaching")]
    public bool AcademicTeaching { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    /// <summary>
    /// How many regions of the screenshot were painted black before it was sent.
    ///
    /// This must survive the trip. The prompt kernel uses it to tell the model
    /// that a black rectangle is content it was forbidden to see rather than
    /// something it actually observed; drop it here and the model will describe
    /// a redacted banking window as a dark panel, confidently and wrongly. That
    /// would be a privacy regression created by routing through a server, which
    /// is the one thing this change must not do.
    /// </summary>
    [JsonPropertyName("withheldScreenRegions")]
    public int WithheldScreenRegions { get; init; }

    /// <summary>
    /// Rebuilds the request the prompt kernel understands. Called on the gateway,
    /// with the screenshot and audio read separately from the multipart body:
    /// this is the one place the wire shape becomes the domain shape, so it is
    /// also the one place a forgotten field shows up.
    /// </summary>
    public GeminiRequest ToGeminiRequest(byte[]? screenshot, byte[]? audio) => new(
        Prompt,
        screenshot,
        audio,
        ActiveWindowTitle,
        AutomationContext,
        ScreenshotMimeType,
        ScreenshotWidth,
        ScreenshotHeight,
        ScreenshotScreenLeft,
        ScreenshotScreenTop,
        ScreenshotSourceWidth,
        ScreenshotSourceHeight,
        Mode,
        Activation,
        Pointer?.ToPointerContext(),
        TaskContext,
        SkillContext,
        UserSkillPacks,
        ChatRecall,
        RecentTurns,
        Region?.ToScreenRegion(),
        AcademicTeaching,
        UserName,
        WithheldScreenRegions);

    /// <summary>The mirror of the above, for the client building the request.</summary>
    public static AssistRequest FromGeminiRequest(
        GeminiRequest request,
        string requestId,
        string? provider,
        string? model,
        string feature,
        bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AssistRequest
        {
            Prompt = request.Prompt,
            RequestId = requestId,
            Provider = provider,
            Model = model,
            Feature = feature,
            Stream = stream,
            ActiveWindowTitle = request.ActiveWindowTitle,
            AutomationContext = request.AutomationContext,
            ScreenshotMimeType = request.ScreenshotMimeType,
            ScreenshotWidth = request.ScreenshotWidth,
            ScreenshotHeight = request.ScreenshotHeight,
            ScreenshotScreenLeft = request.ScreenshotScreenLeft,
            ScreenshotScreenTop = request.ScreenshotScreenTop,
            ScreenshotSourceWidth = request.ScreenshotSourceWidth,
            ScreenshotSourceHeight = request.ScreenshotSourceHeight,
            Mode = request.Mode,
            Activation = request.Activation,
            Pointer = AssistPointer.From(request.Pointer),
            Region = AssistRegion.From(request.Region),
            TaskContext = request.TaskContext,
            SkillContext = request.SkillContext,
            UserSkillPacks = request.UserSkillPacks,
            ChatRecall = request.ChatRecall,
            RecentTurns = request.RecentTurns,
            AcademicTeaching = request.AcademicTeaching,
            UserName = request.UserName,
            WithheldScreenRegions = request.WithheldScreenRegions
        };
    }
}

/// <summary>Where the user was pointing, on the wire.</summary>
public sealed record AssistPointer(
    [property: JsonPropertyName("screenX")] int ScreenX,
    [property: JsonPropertyName("screenY")] int ScreenY,
    [property: JsonPropertyName("normalizedX")] int NormalizedX,
    [property: JsonPropertyName("normalizedY")] int NormalizedY,
    [property: JsonPropertyName("hoveredElement")] string? HoveredElement)
{
    public PointerContext ToPointerContext() =>
        new(ScreenX, ScreenY, NormalizedX, NormalizedY, HoveredElement);

    public static AssistPointer? From(PointerContext? pointer) =>
        pointer is null
            ? null
            : new AssistPointer(
                pointer.ScreenX, pointer.ScreenY,
                pointer.NormalizedX, pointer.NormalizedY, pointer.HoveredElement);
}

/// <summary>
/// A region the user traced, on the wire.
///
/// The traced path itself is sent as a count rather than as points. The prompt
/// only ever reports how many there were, and a path can run to hundreds of
/// coordinates describing the exact shape of a gesture over someone's screen —
/// which is more about what they were doing than the answer needs.
/// </summary>
public sealed record AssistRegion(
    [property: JsonPropertyName("normalizedX")] int NormalizedX,
    [property: JsonPropertyName("normalizedY")] int NormalizedY,
    [property: JsonPropertyName("normalizedWidth")] int NormalizedWidth,
    [property: JsonPropertyName("normalizedHeight")] int NormalizedHeight,
    [property: JsonPropertyName("pathPointCount")] int PathPointCount)
{
    public ScreenRegion ToScreenRegion() => new(
        NormalizedX, NormalizedY, NormalizedWidth, NormalizedHeight,
        Enumerable.Repeat(new GuidancePoint(0, 0), Math.Clamp(PathPointCount, 0, 4096)).ToArray());

    public static AssistRegion? From(ScreenRegion? region) =>
        region is null
            ? null
            : new AssistRegion(
                region.NormalizedX, region.NormalizedY,
                region.NormalizedWidth, region.NormalizedHeight,
                region.Path?.Count ?? 0);
}

/// <summary>What the gateway sends back for a non-streaming turn.</summary>
public sealed record AssistResponse(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model")] string Model,

    /// <summary>
    /// The model's reply, verbatim, still as the raw assistant-plan JSON.
    ///
    /// The gateway deliberately does not parse it. AssistantPlanParser is six
    /// hundred lines of rescuing meaning from truncated replies, and a second
    /// copy on the server would be the exact drift every comment in this
    /// codebase warns about. The client already has the good one.
    /// </summary>
    [property: JsonPropertyName("text")] string Text,

    [property: JsonPropertyName("usage")] AssistUsage? Usage,
    [property: JsonPropertyName("allowance")] AssistAllowance? Allowance);

public sealed record AssistUsage(
    [property: JsonPropertyName("promptTokens")] int PromptTokens,
    [property: JsonPropertyName("thoughtTokens")] int ThoughtTokens,
    [property: JsonPropertyName("outputTokens")] int OutputTokens)
{
    public ModelUsageReport ToReport() => new(PromptTokens, ThoughtTokens, OutputTokens);
}

/// <summary>
/// How much of this month's included AI is left. Returned on every successful
/// turn so the desktop usage meter costs nothing extra to keep current.
/// </summary>
public sealed record AssistAllowance(
    [property: JsonPropertyName("usedUsd")] decimal UsedUsd,
    [property: JsonPropertyName("limitUsd")] decimal LimitUsd,
    [property: JsonPropertyName("resetsUtc")] DateTimeOffset ResetsUtc,

    // The counts, alongside the money.
    //
    // The dollar figure is what Metis needs to protect itself; these are what
    // the person spending them can actually act on. "You have used $0.42" tells
    // nobody whether to slow down, and there is no way to work backwards from it
    // to how many more questions are left. The account page draws these three.
    //
    // Defaulted so an older gateway that sends only the money still deserialises,
    // and so the client shows an empty meter rather than throwing.
    [property: JsonPropertyName("turnsUsed")] int TurnsUsed = 0,
    [property: JsonPropertyName("dictationMinutesUsed")] int DictationMinutesUsed = 0,
    [property: JsonPropertyName("agentStepsUsed")] int AgentStepsUsed = 0);

/// <summary>
/// One frame of a streamed reply.
///
/// The shape is Metis's own rather than any provider's, so the client has a
/// single reader whatever the gateway chose upstream. <c>type</c> is one of
/// "delta", "usage", "error", "done".
/// </summary>
public sealed record AssistStreamFrame(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("usage")] AssistUsage? Usage = null,
    [property: JsonPropertyName("allowance")] AssistAllowance? Allowance = null,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("message")] string? Message = null);

/// <summary>
/// What <c>GET /v1/me</c> returns: the account, its entitlements, its limits,
/// and a signature over all of it so the desktop app can trust a cached copy
/// while it is offline.
/// </summary>
public sealed record MeResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("environment")] string Environment,
    [property: JsonPropertyName("emailVerified")] bool EmailVerified,
    [property: JsonPropertyName("billingIsLive")] bool BillingIsLive,
    [property: JsonPropertyName("features")] IReadOnlyList<string> Features,
    [property: JsonPropertyName("limits")] PlanLimits Limits,
    [property: JsonPropertyName("allowance")] AssistAllowance? Allowance,
    [property: JsonPropertyName("issuedUtc")] DateTimeOffset IssuedUtc,
    [property: JsonPropertyName("expiresUtc")] DateTimeOffset ExpiresUtc,

    /// <summary>
    /// The whole of the above, canonically serialised and signed, as
    /// <c>base64url(payload).base64url(signature)</c>. See EntitlementSigner.
    /// </summary>
    [property: JsonPropertyName("signed")] string Signed);
