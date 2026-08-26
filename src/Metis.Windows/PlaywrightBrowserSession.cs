using System.IO;
using Metis.Core.Agents.Browsing;
using Microsoft.Playwright;

namespace Metis.Windows;

/// <summary>
/// A real Chromium window an agent drives and the user watches.
///
/// Headed on purpose. The user asked to see it working, to be able to switch
/// away and carry on with something else, and to stop it — and a headless
/// browser offers none of that. The cost is that the window is real and takes
/// up space; the benefit is that an autonomous program working on your behalf
/// is not doing so invisibly.
///
/// The banner over the page comes from an init script rather than a floating
/// window on top of the browser. A separate always-on-top window would need to
/// track the browser's position, would drift the moment anything moved, and
/// would sit over other applications when the user switched away. Riding inside
/// the page means it survives navigation, moves with the window because it is
/// part of it, and disappears when the page does.
/// </summary>
public sealed class PlaywrightBrowserSession : IBrowserSession
{
    private readonly Action<string>? _log;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _disposed;

    /// <summary>
    /// Marks the banner so it can be kept out of what the agent reads and
    /// clicks. Without this the agent can see its own overlay, try to press its
    /// own Stop button, and read its own status text back as page content.
    /// </summary>
    private const string OverlayId = "__metis_agent_overlay__";

    public bool IsOpen => _page is not null && !_page.IsClosed;

    public string CurrentUrl => _page?.Url ?? string.Empty;

    public PlaywrightBrowserSession(Action<string>? log = null) => _log = log;

    private async Task<IPage> EnsurePageAsync(CancellationToken cancellationToken)
    {
        if (_page is not null && !_page.IsClosed)
        {
            return _page;
        }

        _playwright ??= await Playwright.CreateAsync();

        _browser ??= await LaunchAsync(_playwright);

        _context ??= await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = ViewportSize.NoViewport
        });

        // Injected before any page script runs, and re-injected on every
        // navigation, so the banner cannot be lost by clicking a link.
        await _context.AddInitScriptAsync(OverlayScript);

        _page = await _context.NewPageAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return _page;
    }

    /// <summary>
    /// Starts a browser, preferring one the user already has.
    ///
    /// Playwright ships its own Chromium, and using it was the obvious first
    /// choice — but that build depends on a Visual C++ runtime that is not
    /// present on every Windows machine, and when it is missing the only
    /// symptom is "spawn UNKNOWN", which says nothing. Driving the installed
    /// Chrome or Edge avoids the dependency, avoids a 190 MB download on first
    /// use, and means the window that opens is the browser the user already
    /// recognises.
    ///
    /// The bundled Chromium is still the last resort, for a machine with
    /// neither installed.
    /// </summary>
    private async Task<IBrowser> LaunchAsync(IPlaywright playwright)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Headless = false,
            Args =
            [
                // Autofill is off deliberately. An agent should never be able
                // to complete a form from the user's saved details -- that is
                // exactly the hand-over this design is built around.
                "--disable-features=Translate,AutofillServerCommunication,AutofillEnableAccountWalletStorage"
            ]
        };

        foreach (var channel in (string?[])["chrome", "msedge", null])
        {
            try
            {
                options.Channel = channel;
                var browser = await playwright.Chromium.LaunchAsync(options);
                _log?.Invoke($"Browser started using {channel ?? "the bundled Chromium"}.");
                return browser;
            }
            catch (Exception exception)
            {
                var firstLine = exception.Message.ReplaceLineEndings(" ");
                _log?.Invoke($"Could not start {channel ?? "bundled Chromium"}: {Shorten(firstLine, 160)}");
            }
        }

        throw new InvalidOperationException(
            "No browser could be started. Install Google Chrome or Microsoft Edge, "
            + "or run 'playwright install chromium' with the Visual C++ runtime present.");
    }

    public async Task<BrowserActionResult> OpenAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var page = await EnsurePageAsync(cancellationToken);

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await ShowActivityAsync($"Opened {page.Url}", cancellationToken);

            return BrowserActionResult.Ok($"Opened {page.Url}. Title: {await page.TitleAsync()}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BrowserActionResult.Fail($"Could not open {url}: {exception.Message}");
        }
    }

    public async Task<BrowserActionResult> ClickAsync(string description, CancellationToken cancellationToken)
    {
        var blocked = await GuardAsync(cancellationToken);
        if (blocked != SensitiveKind.None)
        {
            return BrowserActionResult.Stop(blocked);
        }

        try
        {
            var page = await EnsurePageAsync(cancellationToken);
            var target = Locate(page, description);

            await target.First.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            await ShowActivityAsync($"Clicked {description}", cancellationToken);

            return BrowserActionResult.Ok($"Clicked '{description}'. Now on {page.Url}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BrowserActionResult.Fail(
                $"Could not click '{description}': {exception.Message}. Read the page to see what is actually there.");
        }
    }

    public async Task<BrowserActionResult> TypeAsync(string description, string text, CancellationToken cancellationToken)
    {
        // Checked before typing rather than after. This is the one that matters:
        // it is what stops an agent putting anything into a password or a card
        // field, whatever it believes it is filling in.
        var blocked = await GuardAsync(cancellationToken);
        if (blocked != SensitiveKind.None)
        {
            return BrowserActionResult.Stop(blocked);
        }

        try
        {
            var page = await EnsurePageAsync(cancellationToken);
            var target = Locate(page, description);

            await target.First.FillAsync(text, new LocatorFillOptions { Timeout = 10_000 });
            await ShowActivityAsync($"Typed into {description}", cancellationToken);

            return BrowserActionResult.Ok($"Typed into '{description}'.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BrowserActionResult.Fail($"Could not type into '{description}': {exception.Message}");
        }
    }

    public async Task<BrowserActionResult> ReadAsync(int maxCharacters, CancellationToken cancellationToken)
    {
        try
        {
            var page = await EnsurePageAsync(cancellationToken);

            // The banner is removed from what is read, so the agent never sees
            // its own status text as page content.
            var text = await page.EvaluateAsync<string>($$"""
                () => {
                  const overlay = document.getElementById('{{OverlayId}}');
                  if (overlay) overlay.remove();
                  return document.body ? document.body.innerText : '';
                }
                """);

            var trimmed = string.IsNullOrWhiteSpace(text) ? "(the page has no readable text)" : text.Trim();
            if (trimmed.Length > maxCharacters)
            {
                trimmed = trimmed[..maxCharacters] + "\n…(truncated)";
            }

            return BrowserActionResult.Ok($"{page.Url}\n\n{trimmed}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BrowserActionResult.Fail($"Could not read the page: {exception.Message}");
        }
    }

    public async Task<PageSignals> InspectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var page = await EnsurePageAsync(cancellationToken);

            // Read as JSON rather than into a Dictionary. Playwright hands a
            // Dictionary<string, object?> back empty -- no error, no warning,
            // every value simply missing -- which silently turned every signal
            // below into false and disabled the hand-over gate entirely. It
            // typed into a real password field before this was caught.
            var raw = await page.EvaluateAsync<System.Text.Json.JsonElement>("""
                () => {
                  const has = sel => !!document.querySelector(sel);
                  const frames = Array.from(document.querySelectorAll('iframe'))
                    .map(f => (f.src || '') + ' ' + (f.title || '') + ' ' + (f.id || ''));
                  const joined = frames.join(' ').toLowerCase();
                  const buttons = Array.from(
                      document.querySelectorAll('button, input[type=submit], a[role=button]'))
                    .map(b => (b.innerText || b.value || '').trim())
                    .filter(t => t.length > 0 && t.length < 60)
                    .slice(0, 25);
                  return {
                    url: location.href,
                    password: has('input[type=password]'),
                    newPassword: has('input[autocomplete="new-password"]'),
                    card: has('input[autocomplete="cc-number"]') || has('input[name*="cardnumber" i]'),
                    otp: has('input[autocomplete="one-time-code"]'),
                    captcha: joined.includes('recaptcha') || joined.includes('hcaptcha')
                             || joined.includes('turnstile') || has('.g-recaptcha'),
                    payframe: joined.includes('stripe') || joined.includes('paypal')
                              || joined.includes('checkout') || joined.includes('adyen'),
                    buttons: buttons
                  };
                }
                """);

            return new PageSignals(
                Url: ReadString(raw, "url") ?? page.Url,
                HasPasswordField: ReadBool(raw, "password"),
                HasNewPasswordField: ReadBool(raw, "newPassword"),
                HasCardNumberField: ReadBool(raw, "card"),
                HasOneTimeCodeField: ReadBool(raw, "otp"),
                HasCaptchaFrame: ReadBool(raw, "captcha"),
                HasPaymentFrame: ReadBool(raw, "payframe"),
                ButtonLabels: ReadStrings(raw, "buttons"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log?.Invoke($"Could not inspect the page: {exception.Message}");

            // Unable to tell is not the same as safe. Reporting a password
            // field means the agent stops and asks, which is the right way to
            // be wrong when the page cannot be read.
            return new PageSignals(Url: CurrentUrl, HasPasswordField: true);
        }
    }

    private static bool ReadBool(System.Text.Json.JsonElement element, string name) =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == System.Text.Json.JsonValueKind.True;

    private static string? ReadString(System.Text.Json.JsonElement element, string name) =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStrings(System.Text.Json.JsonElement element, string name)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(text => text.Length > 0)
            .ToList();
    }


    public async Task ShowActivityAsync(string activity, CancellationToken cancellationToken)
    {
        if (_page is null || _page.IsClosed)
        {
            return;
        }

        try
        {
            await _page.EvaluateAsync(
                "([id, text]) => window.__metisSetActivity && window.__metisSetActivity(id, text)",
                new object[] { OverlayId, activity });
        }
        catch
        {
            // The banner is a courtesy. Failing to update it must never fail
            // the action the agent was actually taking.
        }
    }

    /// <summary>
    /// The check that runs before anything is clicked or typed.
    /// </summary>
    private async Task<SensitiveKind> GuardAsync(CancellationToken cancellationToken)
    {
        var signals = await InspectAsync(cancellationToken);
        var kind = SensitiveSurface.Detect(signals);

        if (kind != SensitiveKind.None)
        {
            _log?.Invoke($"Handing the browser over: {kind} at {signals.Url}");
            await ShowActivityAsync($"Waiting for you — {kind}", cancellationToken);
        }

        return kind;
    }

    /// <summary>
    /// Finds what the agent described. Tries the human ways of naming a thing
    /// before falling back to treating the description as a CSS selector, so a
    /// model can say "the Search button" rather than having to know the markup.
    /// </summary>
    private static ILocator Locate(IPage page, string description)
    {
        var trimmed = description.Trim();

        if (trimmed.StartsWith('#') || trimmed.StartsWith('.') || trimmed.StartsWith('['))
        {
            return page.Locator(trimmed);
        }

        return page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = trimmed })
            .Or(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = trimmed }))
            .Or(page.GetByLabel(trimmed))
            .Or(page.GetByPlaceholder(trimmed))
            .Or(page.GetByText(trimmed))
            .Or(page.Locator(trimmed));
    }

    private static string Shorten(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_context is not null)
            {
                await _context.CloseAsync();
            }

            if (_browser is not null)
            {
                await _browser.CloseAsync();
            }
        }
        catch
        {
            // Closing a browser that has already gone is not worth reporting.
        }
        finally
        {
            _playwright?.Dispose();
            _playwright = null;
            _browser = null;
            _context = null;
            _page = null;
        }
    }

    /// <summary>
    /// The banner. Fixed to the top of the page, above everything, and marked
    /// so the agent's own reading and clicking skip it.
    /// </summary>
    private const string OverlayScript = """
        window.__metisSetActivity = (id, text) => {
          let bar = document.getElementById(id);
          if (!bar) {
            bar = document.createElement('div');
            bar.id = id;
            bar.setAttribute('data-metis', 'overlay');
            bar.style.cssText = [
              'position:fixed', 'top:0', 'left:0', 'right:0', 'z-index:2147483647',
              'background:#0A7CFF', 'color:#fff', 'font:600 13px/1.4 system-ui,sans-serif',
              'padding:8px 14px', 'display:flex', 'gap:12px', 'align-items:center',
              'box-shadow:0 2px 10px rgba(0,0,0,.3)', 'pointer-events:none'
            ].join(';');
            const dot = document.createElement('span');
            dot.style.cssText = 'width:8px;height:8px;border-radius:50%;background:#fff;flex:0 0 auto';
            const label = document.createElement('span');
            label.id = id + '_text';
            label.style.cssText = 'flex:1 1 auto;overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
            const hint = document.createElement('span');
            hint.textContent = 'Metis agent is working here';
            hint.style.cssText = 'opacity:.85;font-weight:500;flex:0 0 auto';
            bar.appendChild(dot);
            bar.appendChild(label);
            bar.appendChild(hint);
            (document.body || document.documentElement).appendChild(bar);
            document.documentElement.style.scrollPaddingTop = '40px';
          }
          const label = document.getElementById(id + '_text');
          if (label) label.textContent = text;
        };

        document.addEventListener('DOMContentLoaded', () => {
          if (window.__metisLastActivity) {
            window.__metisSetActivity('__metis_agent_overlay__', window.__metisLastActivity);
          }
        });
        """;
}
