using System.Text;
using System.Text.RegularExpressions;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Searches inside files for text.
///
/// There was no way to do this. <c>search_files</c> matches filenames only, so
/// the one question that dominates working on a codebase — "where is this used?"
/// — could only be answered by shelling out to <c>Select-String</c> and then
/// having the answer cut to two kilobytes. An agent asked to fix a bug spent its
/// turns rediscovering the shape of the project instead of changing it.
/// </summary>
public sealed class SearchContentTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "search_content",
        Description: "Searches the text inside files for a string or regular expression and returns matching lines with their file and line number. Use this to find where something is defined or used.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("query", "string", "Text or regular expression to find", Required: true),
            new("directory_path", "string", "Folder to search. Defaults to the workspace.", Required: false),
            new("file_pattern", "string", "Filename glob to limit the search, such as *.cs", Required: false),
            new("is_regex", "boolean", "Treat the query as a regular expression", Required: false),
            new("case_sensitive", "boolean", "Match case exactly", Required: false),
            new("max_results", "number", "Most matches to return (default 60)", Required: false)
        ]);

    /// <summary>
    /// Folders that are always skipped.
    ///
    /// Not an optimisation. A recursive search through node_modules returns
    /// thousands of matches in code nobody wrote, fills the result budget, and
    /// tells the agent nothing about the project it is working on.
    /// </summary>
    private static readonly string[] SkippedFolders =
    [
        "node_modules", ".git", "bin", "obj", "dist", "build", ".vs", ".next", "__pycache__", "venv"
    ];

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var query = arguments.GetValueOrDefault("query")?.ToString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'query' is required."));
        }

        var decision = context.ResolvePath(arguments.GetValueOrDefault("directory_path")?.ToString() ?? ".");
        if (!decision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
        }

        var root = decision.FullPath!;
        if (!Directory.Exists(root))
        {
            return Task.FromResult(AgentToolResult.Fail($"Directory not found: {root}"));
        }

        var pattern = arguments.GetValueOrDefault("file_pattern")?.ToString();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "*";
        }

        var isRegex = ReadBool(arguments, "is_regex");
        var caseSensitive = ReadBool(arguments, "case_sensitive");
        var maxResults = ReadInt(arguments, "max_results", 60, 1, 300);

        Regex? expression = null;
        if (isRegex)
        {
            try
            {
                expression = new Regex(
                    query,
                    caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException exception)
            {
                return Task.FromResult(AgentToolResult.Fail($"That is not a valid regular expression. {exception.Message}"));
            }
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var builder = new StringBuilder();
        var matches = 0;
        var filesSearched = 0;

        try
        {
            foreach (var file in EnumerateFiles(root, pattern!, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (matches >= maxResults)
                {
                    break;
                }

                if (LooksBinary(file))
                {
                    continue;
                }

                filesSearched++;
                var lineNumber = 0;

                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;

                    var hit = expression is not null
                        ? expression.IsMatch(line)
                        : line.Contains(query, comparison);

                    if (!hit)
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(root, file);
                    var trimmed = line.Trim();
                    if (trimmed.Length > 200)
                    {
                        trimmed = trimmed[..200] + "…";
                    }

                    builder.AppendLine($"{relative}:{lineNumber}: {trimmed}");
                    matches++;

                    if (matches >= maxResults)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(AgentToolResult.Fail($"Search failed: {exception.Message}"));
        }

        if (matches == 0)
        {
            return Task.FromResult(AgentToolResult.Ok(
                $"No matches for '{query}' in {filesSearched} file(s) under {root}."));
        }

        var header = matches >= maxResults
            ? $"First {matches} matches for '{query}' (there may be more):"
            : $"{matches} match(es) for '{query}':";

        return Task.FromResult(AgentToolResult.Ok($"{header}\n{builder}"));
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern, CancellationToken cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, pattern);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (!SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    queue.Enqueue(directory);
                }
            }
        }
    }

    /// <summary>
    /// A cheap guess at whether a file is worth reading as text. Reading a few
    /// megabytes of PNG line by line wastes the budget and produces nothing.
    /// </summary>
    private static bool LooksBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> buffer = stackalloc byte[512];
            var read = stream.Read(buffer);

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    internal static bool ReadBool(IReadOnlyDictionary<string, object?> arguments, string name) =>
        arguments.TryGetValue(name, out var value) &&
        value is not null &&
        bool.TryParse(value.ToString(), out var parsed) &&
        parsed;

    internal static int ReadInt(
        IReadOnlyDictionary<string, object?> arguments,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        if (arguments.TryGetValue(name, out var value) &&
            value is not null &&
            int.TryParse(value.ToString(), out var parsed))
        {
            return Math.Clamp(parsed, minimum, maximum);
        }

        return fallback;
    }
}

/// <summary>
/// Changes part of a file, leaving the rest alone.
///
/// The only way to edit anything was <c>write_file</c>, which replaces the whole
/// file. So a one-line fix meant reading the entire file in and sending the
/// entire file back out — two turns, and bounded by the model's own output
/// limit, which on the Claude path is 4096 tokens. Anything above roughly ten
/// kilobytes simply could not be edited: the agent would truncate it and write
/// back a shorter, broken version of the file it was trying to fix.
///
/// Replacing an exact snippet avoids all of that, and fails loudly when the
/// snippet is not unique rather than guessing which occurrence was meant.
/// </summary>
public sealed class EditFileTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "edit_file",
        Description: "Replaces an exact piece of text in a file, leaving everything else untouched. Prefer this over write_file for changing an existing file. The text to find must appear exactly once.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("file_path", "string", "File to change", Required: true),
            new("find", "string", "The exact text to replace, including indentation", Required: true),
            new("replace_with", "string", "What to put in its place. Empty string deletes it.", Required: true),
            new("expected_occurrences", "number", "How many times 'find' should appear. Defaults to 1.", Required: false)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPath = arguments.GetValueOrDefault("file_path")?.ToString();
        var find = arguments.GetValueOrDefault("find")?.ToString();
        var replaceWith = arguments.GetValueOrDefault("replace_with")?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return AgentToolResult.Fail("Parameter 'file_path' is required.");
        }

        if (string.IsNullOrEmpty(find))
        {
            return AgentToolResult.Fail("Parameter 'find' is required. To create a file, use write_file.");
        }

        var decision = context.ResolvePath(rawPath);
        if (!decision.Allowed)
        {
            return AgentToolResult.Fail(decision.DenialReason!);
        }

        var fullPath = decision.FullPath!;
        if (!File.Exists(fullPath))
        {
            return AgentToolResult.Fail($"File not found: {fullPath}");
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (Exception exception)
        {
            return AgentToolResult.Fail($"Could not read {fullPath}: {exception.Message}");
        }

        var occurrences = CountOccurrences(content, find);
        var expected = SearchContentTool.ReadInt(arguments, "expected_occurrences", 1, 1, 500);

        if (occurrences == 0)
        {
            return AgentToolResult.Fail(
                $"That text does not appear in {Path.GetFileName(fullPath)}. "
                + "Read the file again — whitespace and indentation have to match exactly.");
        }

        if (occurrences != expected)
        {
            // Guessing which one was meant is how an edit silently changes the
            // wrong line. Better to say so and let the agent give more context.
            return AgentToolResult.Fail(
                $"That text appears {occurrences} times in {Path.GetFileName(fullPath)}, not {expected}. "
                + "Include more surrounding lines so the match is unique, "
                + "or set expected_occurrences if you mean to change all of them.");
        }

        var updated = content.Replace(find, replaceWith, StringComparison.Ordinal);

        try
        {
            await File.WriteAllTextAsync(fullPath, updated, cancellationToken);
        }
        catch (Exception exception)
        {
            return AgentToolResult.Fail($"Could not write {fullPath}: {exception.Message}");
        }

        var delta = updated.Length - content.Length;
        var sign = delta >= 0 ? "+" : string.Empty;

        return AgentToolResult.Ok(
            $"Edited {Path.GetFileName(fullPath)}: replaced {occurrences} occurrence(s), {sign}{delta} characters.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return 0;
        }

        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
