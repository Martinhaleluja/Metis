namespace Metis.Core.Agents;

/// <summary>
/// The words the interface uses for what an agent is doing.
///
/// This lives here rather than in the drawer that draws it because the drawer
/// is in the desktop project, which the test project cannot reference — and the
/// bug this replaces was precisely the sort a test catches and a reviewer does
/// not. The task badge was <c>Status.ToString().ToUpperInvariant()</c>, so a
/// task waiting on the user announced itself as "AWAITINGAPPROVAL": one
/// run-on word with no space and no glyph, sitting directly above a tidy column
/// of "✓ VERIFIED" and "▶ RUNNING" step badges. Two vocabularies in one card
/// read as two different products, and the one that read worst was the one
/// asking the user for permission.
///
/// Every label is a glyph, a space, and one or two words in capitals. The
/// glyphs are shared with the step vocabulary on purpose: a tick means finished
/// and a triangle means going, whichever row of the card it appears in.
/// </summary>
public static class AgentStatusVocabulary
{
    /// <summary>
    /// The badge for a whole task.
    ///
    /// "NEEDS YOU" rather than "AWAITING APPROVAL" because it is addressed to
    /// the person reading it, and because it is the one status that will not
    /// resolve itself if they look away.
    /// </summary>
    public static string ForTask(AgentTaskStatus status) => status switch
    {
        AgentTaskStatus.Queued => "⋯ QUEUED",
        AgentTaskStatus.Planning => "◌ PLANNING",
        AgentTaskStatus.Running => "▶ RUNNING",
        AgentTaskStatus.AwaitingApproval => "⚠ NEEDS YOU",
        AgentTaskStatus.Paused => "‖ PAUSED",
        AgentTaskStatus.Completed => "✓ DONE",
        AgentTaskStatus.Failed => "✕ FAILED",
        AgentTaskStatus.Cancelled => "↷ STOPPED",
        _ => "⋯ PENDING"
    };

    /// <summary>The badge for one step within a task.</summary>
    public static string ForStep(AgentStepStatus status) => status switch
    {
        AgentStepStatus.Success => "✓ VERIFIED",
        AgentStepStatus.Running => "▶ RUNNING",
        AgentStepStatus.Failed => "✕ FAILED",
        AgentStepStatus.Skipped => "↷ SKIPPED",
        _ => "⋯ PENDING"
    };

    /// <summary>
    /// Whether an agent may reach outside the folder it was given.
    ///
    /// The highest-consequence fact on a task record, and one the drawer did not
    /// show anywhere at all. An agent confined to its workspace can at worst
    /// make a mess of its own folder; one that is not can rewrite anything its
    /// owner can, and the spawn panel offers the user's own folders freely.
    /// </summary>
    public static string ForScope(bool allowOutsideWorkspace) =>
        allowOutsideWorkspace
            ? "⚠ Can read and write outside its folder"
            : "✓ Confined to its own folder";
}
