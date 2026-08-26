using Metis.Core.Agents.Browsing;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Keeps one browser per task, opened the first time an agent needs it.
///
/// Per task rather than per application, so two agents working at once do not
/// fight over the same window, and so closing one task's browser cannot pull
/// the page out from under another.
/// </summary>
public sealed class BrowserSessions(IBrowserSessionFactory? factory) : IAsyncDisposable
{
    private readonly Dictionary<string, IBrowserSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Available => factory is not null;

    public async Task<IBrowserSession?> ForAsync(string taskId, CancellationToken cancellationToken)
    {
        if (factory is null)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(taskId, out var existing) && existing.IsOpen)
            {
                return existing;
            }

            var session = await factory.CreateAsync(taskId, cancellationToken);
            _sessions[taskId] = session;
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the browser a task was using. Called when the task ends.</summary>
    public async Task CloseAsync(string taskId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_sessions.Remove(taskId, out var session))
            {
                await session.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
        _gate.Dispose();
    }
}

/// <summary>Shared plumbing for the four browser tools.</summary>
public abstract class BrowserToolBase(BrowserSessions sessions) : IAgentTool
{
    public abstract AgentToolDeclaration Declaration { get; }

    protected abstract Task<BrowserActionResult> RunAsync(
        IBrowserSession session,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var session = await sessions.ForAsync(context.TaskId, cancellationToken);
        if (session is null)
        {
            return AgentToolResult.Fail(
                "The browser is not available in this build. Use fetch_url_content for plain pages instead.");
        }

        try
        {
            var result = await RunAsync(session, arguments, cancellationToken);

            if (result.HandOver != SensitiveKind.None)
            {
                // Not a failure of the agent's, and it must not be retried in a
                // loop. Saying plainly what is needed is what lets the model
                // stop and tell the user rather than keep hammering the page.
                return AgentToolResult.Fail(
                    $"{result.Message} Do not try again until the user says they have finished.");
            }

            return result.Success
                ? AgentToolResult.Ok(result.Message)
                : AgentToolResult.Fail(result.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return AgentToolResult.Fail($"The browser failed: {exception.Message}");
        }
    }
}

public sealed class BrowserOpenTool(BrowserSessions sessions) : BrowserToolBase(sessions)
{
    public override AgentToolDeclaration Declaration { get; } = new(
        Name: "browser_open",
        Description: "Opens a web page in a visible browser window the user can watch. Use this for anything needing a real browser - logging-in sites, single-page apps, or forms. For plain static pages fetch_url_content is cheaper.",
        Category: "browser",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters: [new("url", "string", "The address to open", Required: true)]);

    protected override Task<BrowserActionResult> RunAsync(
        IBrowserSession session,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken) =>
        session.OpenAsync(arguments.GetValueOrDefault("url")?.ToString() ?? string.Empty, cancellationToken);
}

public sealed class BrowserReadTool(BrowserSessions sessions) : BrowserToolBase(sessions)
{
    public override AgentToolDeclaration Declaration { get; } = new(
        Name: "browser_read",
        Description: "Reads the visible text of the page the browser is on. Do this before clicking, so you act on what is actually there.",
        Category: "browser",
        RiskLevel: AgentRiskLevel.Low,
        Parameters: [new("max_characters", "number", "How much to read (default 4000)", Required: false)]);

    protected override Task<BrowserActionResult> RunAsync(
        IBrowserSession session,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken) =>
        session.ReadAsync(
            SearchContentTool.ReadInt(arguments, "max_characters", 4000, 500, 20000),
            cancellationToken);
}

public sealed class BrowserClickTool(BrowserSessions sessions) : BrowserToolBase(sessions)
{
    public override AgentToolDeclaration Declaration { get; } = new(
        Name: "browser_click",
        Description: "Clicks something on the page, named the way a person would name it - the button's label, a link's text, or a CSS selector.",
        Category: "browser",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters: [new("target", "string", "What to click, such as 'Sign in' or '#submit'", Required: true)]);

    protected override Task<BrowserActionResult> RunAsync(
        IBrowserSession session,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken) =>
        session.ClickAsync(arguments.GetValueOrDefault("target")?.ToString() ?? string.Empty, cancellationToken);
}

public sealed class BrowserTypeTool(BrowserSessions sessions) : BrowserToolBase(sessions)
{
    public override AgentToolDeclaration Declaration { get; } = new(
        Name: "browser_type",
        Description: "Types into a field on the page. Metis refuses to type into password, card or sign-up fields and will hand the browser to the user instead - do not attempt to work around that.",
        Category: "browser",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("target", "string", "The field, such as 'Search' or '#email'", Required: true),
            new("text", "string", "What to type", Required: true)
        ]);

    protected override Task<BrowserActionResult> RunAsync(
        IBrowserSession session,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken) =>
        session.TypeAsync(
            arguments.GetValueOrDefault("target")?.ToString() ?? string.Empty,
            arguments.GetValueOrDefault("text")?.ToString() ?? string.Empty,
            cancellationToken);
}
