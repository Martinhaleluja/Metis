using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Tool for performing web search queries.
/// </summary>
public sealed class WebSearchTool : IAgentTool
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(25) };

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "web_search",
        Description: "Searches the web for given query terms and returns top relevant summaries and URLs.",
        Category: "web",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("query", "string", "The search query string", Required: true),
            new("max_results", "number", "Maximum number of search results to return (default 5, max 20)", Required: false, DefaultValue: 5)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var query = arguments.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return AgentToolResult.Fail("Parameter 'query' is required.");
        }

        var maxResults = 5;
        if (arguments.TryGetValue("max_results", out var mrObj) && mrObj is not null && int.TryParse(mrObj.ToString(), out var mr))
        {
            maxResults = Math.Max(1, Math.Min(mr, 20));
        }

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var requestUrl = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return AgentToolResult.Fail($"Web search returned HTTP status {(int)response.StatusCode} ({response.StatusCode})");
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var results = ExtractDuckDuckGoResults(html, maxResults);

            if (results.Count == 0)
            {
                return AgentToolResult.Ok($"No web search results found for '{query}'.");
            }

            var output = $"Web Search Results for '{query}' (Found {results.Count} results):\n\n" +
                         string.Join("\n\n", results.Select((r, i) => $"{i + 1}. **{r.Title}**\n   URL: {r.Url}\n   Snippet: {r.Snippet}"));

            return AgentToolResult.Ok(output);
        }
        catch (Exception ex)
        {
            return AgentToolResult.Fail($"Web search failed: {ex.Message}");
        }
    }

    private static List<(string Title, string Url, string Snippet)> ExtractDuckDuckGoResults(string html, int max)
    {
        var list = new List<(string Title, string Url, string Snippet)>();

        // Match result blocks
        var snippetMatches = Regex.Matches(html, @"<a class=""result__snippet[^""]*""[^>]*href=""(?<url>[^""]*)""[^>]*>(?<snippet>.*?)</a>", RegexOptions.Singleline);
        var titleMatches = Regex.Matches(html, @"<a class=""result__url[^""]*""[^>]*href=""(?<url>[^""]*)""[^>]*>(?<title>.*?)</a>", RegexOptions.Singleline);

        for (var i = 0; i < Math.Min(snippetMatches.Count, max); i++)
        {
            var snippet = StripHtml(snippetMatches[i].Groups["snippet"].Value).Trim();
            var url = snippetMatches[i].Groups["url"].Value;
            var title = i < titleMatches.Count ? StripHtml(titleMatches[i].Groups["title"].Value).Trim() : "Result";

            // DuckDuckGo redirects through //duckduckgo.com/l/?uddg=...
            if (url.Contains("uddg="))
            {
                var match = Regex.Match(url, @"uddg=([^&]+)");
                if (match.Success)
                {
                    url = Uri.UnescapeDataString(match.Groups[1].Value);
                }
            }

            if (!string.IsNullOrEmpty(snippet))
            {
                list.Add((title, url, snippet));
            }
        }

        return list;
    }

    private static string StripHtml(string input) =>
        System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(input, "<.*?>", string.Empty), @"\s+", " "));
}

/// <summary>
/// Tool for fetching and reading web page text content.
/// </summary>
public sealed class FetchUrlContentTool : IAgentTool
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "fetch_url_content",
        Description: "Fetches text content from a web URL. Converts HTML to clean readable text/markdown format up to 128KB.",
        Category: "web",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("url", "string", "The HTTP/HTTPS URL to fetch", Required: true),
            new("max_characters", "number", "Maximum characters to return (default 16000, max 128000)", Required: false, DefaultValue: 16000),
            new("timeout_seconds", "number", "HTTP request timeout in seconds (default 30, max 120)", Required: false, DefaultValue: 30)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var url = arguments.GetValueOrDefault("url")?.ToString();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return AgentToolResult.Fail("A valid HTTP or HTTPS URL is required.");
        }

        var maxChars = 16000;
        if (arguments.TryGetValue("max_characters", out var mcObj) && mcObj is not null && int.TryParse(mcObj.ToString(), out var mc))
        {
            maxChars = Math.Max(1000, Math.Min(mc, 128000));
        }

        var timeoutSec = 30;
        if (arguments.TryGetValue("timeout_seconds", out var toObj) && toObj is not null && int.TryParse(toObj.ToString(), out var to))
        {
            timeoutSec = Math.Max(5, Math.Min(to, 120));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var response = await HttpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return AgentToolResult.Fail($"HTTP request failed with status {(int)response.StatusCode} ({response.StatusCode})");
            }

            var html = await response.Content.ReadAsStringAsync(cts.Token);
            var text = CleanHtmlToMarkdown(html);

            if (text.Length > maxChars)
            {
                text = text[..maxChars] + Environment.NewLine + "... [Truncated: content exceeded character limit]";
            }

            return AgentToolResult.Ok(text);
        }
        catch (OperationCanceledException)
        {
            return AgentToolResult.Fail($"Request to '{url}' timed out after {timeoutSec} seconds.");
        }
        catch (Exception ex)
        {
            return AgentToolResult.Fail($"Failed to fetch URL content: {ex.Message}");
        }
    }

    private static string CleanHtmlToMarkdown(string html)
    {
        // Strip script, style, noscript, svg, audio, video tags
        var stripped = Regex.Replace(html, @"<(script|style|noscript|svg|canvas|video|audio)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

        // Convert headings
        stripped = Regex.Replace(stripped, @"<h1[^>]*>(.*?)</h1>", "\n# $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<h2[^>]*>(.*?)</h2>", "\n## $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<h3[^>]*>(.*?)</h3>", "\n### $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<h[4-6][^>]*>(.*?)</h[4-6]>", "\n#### $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Convert list items
        stripped = Regex.Replace(stripped, @"<li[^>]*>(.*?)</li>", "\n* $1", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Convert paragraphs and line breaks
        stripped = Regex.Replace(stripped, @"<p[^>]*>(.*?)</p>", "\n$1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Strip remaining tags
        stripped = Regex.Replace(stripped, @"<[^>]+>", string.Empty);

        // Decode HTML entities
        stripped = System.Net.WebUtility.HtmlDecode(stripped);

        // Collapse excess blank lines
        return Regex.Replace(stripped, @"\n{3,}", "\n\n").Trim();
    }
}

/// <summary>
/// Tool for downloading a file from a URL to disk with streaming progress reporting.
/// </summary>
public sealed class DownloadFileTool : IAgentTool
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public AgentToolDeclaration Declaration { get; } = new(
        Name: "download_file",
        Description: "Downloads a file from a URL and saves it to local disk with streaming progress reporting.",
        Category: "web",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("url", "string", "The URL of the file to download", Required: true),
            new("destination_path", "string", "Local file path where the downloaded file should be saved", Required: true),
            new("timeout_seconds", "number", "Maximum download time in seconds (default 120, max 600)", Required: false, DefaultValue: 120)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var url = arguments.GetValueOrDefault("url")?.ToString();
        var dst = arguments.GetValueOrDefault("destination_path")?.ToString();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(dst))
        {
            return AgentToolResult.Fail("Both 'url' and 'destination_path' are required.");
        }

        var timeoutSec = 120;
        if (arguments.TryGetValue("timeout_seconds", out var toObj) && toObj is not null && int.TryParse(toObj.ToString(), out var to))
        {
            timeoutSec = Math.Max(10, Math.Min(to, 600));
        }

        // Downloading is how something from the internet reaches the disk, so
        // where it lands matters more here than anywhere else.
        var dstDecision = context.ResolvePath(dst);
        if (!dstDecision.Allowed)
        {
            return AgentToolResult.Fail(dstDecision.DenialReason!);
        }

        var fullDst = dstDecision.FullPath!;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            var dir = Path.GetDirectoryName(fullDst);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var srcStream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var dstStream = File.Create(fullDst);

            var buffer = new byte[81920]; // 80KB buffer
            long totalRead = 0;
            int bytesRead;
            var lastReportTime = DateTimeOffset.MinValue;

            while ((bytesRead = await srcStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
            {
                await dstStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                totalRead += bytesRead;

                if (DateTimeOffset.Now - lastReportTime > TimeSpan.FromMilliseconds(500))
                {
                    lastReportTime = DateTimeOffset.Now;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        var pct = (int)((totalRead * 100) / totalBytes.Value);
                        context.ProgressReporter?.Report($"Downloading {Path.GetFileName(fullDst)}: {pct}% ({totalRead / 1048576.0:F1} / {totalBytes.Value / 1048576.0:F1} MB)");
                    }
                    else
                    {
                        context.ProgressReporter?.Report($"Downloading {Path.GetFileName(fullDst)}: {totalRead / 1048576.0:F1} MB");
                    }
                }
            }

            var fi = new FileInfo(fullDst);
            var mimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var artifact = new AgentArtifact(
                Guid.NewGuid().ToString("N"),
                fi.Name,
                fullDst,
                mimeType,
                fi.Length,
                DateTimeOffset.Now,
                $"Downloaded file: {fi.Name} ({fi.Length / 1024.0:F1} KB)");

            context.ArtifactEmitter?.Invoke(artifact);
            return AgentToolResult.Ok($"Downloaded {fi.Length / 1024.0:F1} KB to '{fullDst}' successfully.", artifact);
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(fullDst))
            {
                try { File.Delete(fullDst); } catch { }
            }
            return AgentToolResult.Fail($"Download timed out after {timeoutSec} seconds or was cancelled.");
        }
        catch (Exception ex)
        {
            if (File.Exists(fullDst))
            {
                try { File.Delete(fullDst); } catch { }
            }
            return AgentToolResult.Fail($"Download failed: {ex.Message}");
        }
    }
}
