namespace Metis.Core.Agents;

/// <summary>
/// Runtime context provided to an agent tool during execution.
/// </summary>
public sealed record AgentToolContext(
    string TaskId,
    string WorkingDirectory,
    IProgress<string>? ProgressReporter,
    Action<AgentLogEntry>? Logger,
    Action<AgentArtifact>? ArtifactEmitter,

    /// <summary>
    /// Whether this task may touch paths outside its workspace.
    ///
    /// False unless the user pointed the task at one of their own folders when
    /// spawning it. Tools do not decide this for themselves; they ask
    /// <see cref="AgentWorkspace.Resolve"/> and act on the answer.
    /// </summary>
    bool AllowOutsideWorkspace = false)
{
    /// <summary>
    /// Turns a path a tool was given into one it may use, or explains why not.
    ///
    /// Every tool that takes a path goes through here. Before this existed each
    /// one resolved paths itself with the same two-line idiom and no check
    /// afterwards, so the workspace was a suggestion rather than a boundary.
    /// </summary>
    public PathDecision ResolvePath(string? rawPath) =>
        AgentWorkspace.Resolve(WorkingDirectory, rawPath, AllowOutsideWorkspace);
}

/// <summary>
/// Contract for an autonomous tool that an agent can invoke.
/// </summary>
public interface IAgentTool
{
    /// <summary>Declaration describing the tool, its parameters, and its safety classification.</summary>
    AgentToolDeclaration Declaration { get; }

    /// <summary>Executes the tool asynchronously in the background.</summary>
    Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Central registry of available agent tools.
/// </summary>
public interface IAgentToolRegistry
{
    /// <summary>Registers an agent tool.</summary>
    void Register(IAgentTool tool);

    /// <summary>Retrieves a registered tool by its unique name.</summary>
    IAgentTool? GetTool(string name);

    /// <summary>Lists all registered tool declarations.</summary>
    IReadOnlyList<AgentToolDeclaration> GetDeclarations(IReadOnlyList<string>? categories = null);
}

/// <summary>
/// Default thread-safe implementation of <see cref="IAgentToolRegistry"/>.
/// </summary>
public sealed class AgentToolRegistry : IAgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Register(IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        lock (_lock)
        {
            _tools[tool.Declaration.Name] = tool;
        }
    }

    public IAgentTool? GetTool(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_lock)
        {
            return _tools.GetValueOrDefault(name);
        }
    }

    public IReadOnlyList<AgentToolDeclaration> GetDeclarations(IReadOnlyList<string>? categories = null)
    {
        lock (_lock)
        {
            var query = _tools.Values.Select(t => t.Declaration);
            if (categories is { Count: > 0 })
            {
                var catSet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
                query = query.Where(d => catSet.Contains(d.Category));
            }
            return query.ToList();
        }
    }
}
