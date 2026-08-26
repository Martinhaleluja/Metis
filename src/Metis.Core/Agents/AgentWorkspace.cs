namespace Metis.Core.Agents;

/// <summary>Why a path was refused, or null when it was allowed.</summary>
public sealed record PathDecision(string? FullPath, string? DenialReason)
{
    public bool Allowed => DenialReason is null && FullPath is not null;

    public static PathDecision Allow(string fullPath) => new(fullPath, null);

    public static PathDecision Deny(string reason) => new(null, reason);
}

/// <summary>
/// Where an agent is allowed to work.
///
/// There was no such boundary before. Every path-taking tool resolved its
/// argument the same way — <c>Path.IsPathRooted(raw) ? raw : Path.Combine(dir,
/// raw)</c> — which means an absolute path went anywhere on the machine, and a
/// relative one containing <c>..</c> did too, because Path.Combine does not
/// normalise and nothing checked afterwards. The default working directory was
/// the user's whole profile. So an agent asked to tidy Downloads could, without
/// anything going wrong exactly, write to AppData or delete from Documents.
///
/// That was survivable while agents did small errands. It is not survivable now
/// that they run package installs and drive a browser, so this is the floor
/// everything else is built on.
///
/// The check is deliberately string-only: no filesystem access, no existence
/// test, nothing that can throw on a locked directory or behave differently
/// depending on what happens to be on disk. That makes it testable, and it
/// makes it fast enough to sit in front of every file operation.
/// </summary>
public static class AgentWorkspace
{
    /// <summary>The folder an agent gets to itself when no other is chosen.</summary>
    public static string RootFor(string taskId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Metis",
        "agents",
        "workspace",
        taskId);

    /// <summary>
    /// Turns a path an agent asked for into one it may actually use.
    /// </summary>
    /// <param name="workspaceRoot">The folder this agent is confined to.</param>
    /// <param name="rawPath">Whatever the model put in the tool argument.</param>
    /// <param name="allowOutside">
    /// Set for tasks the user deliberately pointed at one of their own folders.
    /// Even then the path is normalised, so the result is always absolute and
    /// free of <c>..</c> — what changes is only whether leaving the root is
    /// refused.
    /// </param>
    public static PathDecision Resolve(string workspaceRoot, string? rawPath, bool allowOutside = false)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return PathDecision.Deny("No path was given.");
        }

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return PathDecision.Deny("This agent has no workspace, so no path can be resolved.");
        }

        string full;
        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));

            // Relative paths are relative to the workspace; absolute ones are
            // taken as written and then judged. Combine before GetFullPath so
            // that ".." is resolved against the root rather than against
            // whatever the process happens to have as its current directory.
            var combined = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.Combine(root, rawPath);

            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(combined));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Invalid characters, a malformed device path, or something longer
            // than the OS will take. Refusing beats letting it reach the disk.
            return PathDecision.Deny($"That path cannot be read as a path. {exception.Message}");
        }

        if (allowOutside || IsUnder(root, full))
        {
            return PathDecision.Allow(full);
        }

        return PathDecision.Deny(
            $"'{full}' is outside this agent's workspace ({root}). "
            + "Work inside the workspace, or ask the user to grant this task access to their own folders.");
    }

    /// <summary>
    /// Whether one path sits inside another.
    ///
    /// The separator on the end of the root is what makes this correct:
    /// comparing the bare strings would accept <c>C:\work\task10</c> as being
    /// inside <c>C:\work\task1</c>, which is the classic way this check is got
    /// wrong. Case-insensitive because Windows paths are.
    /// </summary>
    public static bool IsUnder(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmedRoot = Path.TrimEndingDirectorySeparator(root);
        var trimmedCandidate = Path.TrimEndingDirectorySeparator(candidate);

        if (string.Equals(trimmedRoot, trimmedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = trimmedRoot + Path.DirectorySeparatorChar;
        return trimmedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
