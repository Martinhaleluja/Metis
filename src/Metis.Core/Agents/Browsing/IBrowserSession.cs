namespace Metis.Core.Agents.Browsing;

/// <summary>What happened when an agent tried to do something in the browser.</summary>
public sealed record BrowserActionResult(
    bool Success,
    string Message,

    /// <summary>
    /// Set when the action was refused because the page is asking for something
    /// only the person should give it. Not a failure — the agent is meant to
    /// stop, say so, and wait.
    /// </summary>
    SensitiveKind HandOver = SensitiveKind.None)
{
    public static BrowserActionResult Ok(string message) => new(true, message);

    public static BrowserActionResult Fail(string message) => new(false, message);

    public static BrowserActionResult Stop(SensitiveKind kind) =>
        new(false, SensitiveSurface.Explain(kind), kind);
}

/// <summary>
/// A browser an agent is driving, and the user is watching.
///
/// Declared here, in Core, with no reference to any browser library, so the
/// tools and the rules stay testable and the actual automation lives in the
/// Windows project where it belongs. The same shape the companion and
/// observation tools already use.
///
/// The session is deliberately visible. A headless browser would be simpler and
/// faster and is what most automation does, but the user asked to be able to
/// watch it work, switch away, and stop it — none of which mean anything if
/// there is no window.
/// </summary>
public interface IBrowserSession : IAsyncDisposable
{
    bool IsOpen { get; }

    /// <summary>The page the browser is on, for reporting.</summary>
    string CurrentUrl { get; }

    Task<BrowserActionResult> OpenAsync(string url, CancellationToken cancellationToken);

    Task<BrowserActionResult> ClickAsync(string description, CancellationToken cancellationToken);

    Task<BrowserActionResult> TypeAsync(string description, string text, CancellationToken cancellationToken);

    /// <summary>The readable text of the page, for the agent to reason about.</summary>
    Task<BrowserActionResult> ReadAsync(int maxCharacters, CancellationToken cancellationToken);

    /// <summary>What the page is asking for, if anything.</summary>
    Task<PageSignals> InspectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates the banner shown over the page, so the user can see what the
    /// agent is doing without reading a log.
    /// </summary>
    Task ShowActivityAsync(string activity, CancellationToken cancellationToken);
}

/// <summary>Makes a browser for a task, when one is first needed.</summary>
public interface IBrowserSessionFactory
{
    Task<IBrowserSession> CreateAsync(string taskId, CancellationToken cancellationToken);
}
