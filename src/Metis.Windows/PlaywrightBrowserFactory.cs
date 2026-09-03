using Metis.Core.Agents.Browsing;

namespace Metis.Windows;

/// <summary>
/// Makes a browser for an agent that asks for one.
///
/// Separate from the session so that Core can declare what it needs without
/// knowing that Playwright exists, and so the first launch — which downloads a
/// browser if one is not already installed — happens when a browser is actually
/// wanted rather than at startup for everybody.
/// </summary>
public sealed class PlaywrightBrowserFactory(Action<string>? log = null) : IBrowserSessionFactory
{
    public Task<IBrowserSession> CreateAsync(string taskId, CancellationToken cancellationToken)
    {
        log?.Invoke($"Opening a browser for {taskId}.");
        return Task.FromResult<IBrowserSession>(new PlaywrightBrowserSession(log));
    }
}
