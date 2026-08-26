using System.IO;
using System.Text;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Tool for reading text file contents with line-range pagination and size clamping.
/// </summary>
public sealed class ReadFileTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "read_file",
        Description: "Reads the content of a text file from the filesystem. Supports line range pagination and clamping up to 64KB.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("file_path", "string", "Absolute or relative path to the file to read", Required: true),
            new("start_line", "number", "1-indexed line number to start reading from (default 1)", Required: false, DefaultValue: 1),
            new("max_lines", "number", "Maximum number of lines to read (default 500, max 5000)", Required: false, DefaultValue: 500),
            new("show_line_numbers", "boolean", "Whether to prepend 1-indexed line numbers to output (default false)", Required: false, DefaultValue: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPath = arguments.GetValueOrDefault("file_path")?.ToString();
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'file_path' is required."));
        }

        var decision = context.ResolvePath(rawPath);
        if (!decision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
        }

        var fullPath = decision.FullPath!;
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(AgentToolResult.Fail($"File not found: {fullPath}"));
        }

        try
        {
            var startLine = 1;
            if (arguments.TryGetValue("start_line", out var slObj) && slObj is not null && int.TryParse(slObj.ToString(), out var sl))
            {
                startLine = Math.Max(1, sl);
            }

            var maxLines = 500;
            if (arguments.TryGetValue("max_lines", out var mlObj) && mlObj is not null && int.TryParse(mlObj.ToString(), out var ml))
            {
                maxLines = Math.Max(1, Math.Min(ml, 5000));
            }

            var showLineNumbers = arguments.TryGetValue("show_line_numbers", out var lnObj) && lnObj is not null &&
                                  bool.TryParse(lnObj.ToString(), out var showLn) && showLn;

            var allLines = File.ReadLines(fullPath, Encoding.UTF8);
            var selectedLines = allLines.Skip(startLine - 1).Take(maxLines).ToList();

            var sb = new StringBuilder();
            var lineIndex = startLine;
            foreach (var line in selectedLines)
            {
                if (showLineNumbers)
                {
                    sb.AppendLine($"{lineIndex,5}: {line}");
                }
                else
                {
                    sb.AppendLine(line);
                }
                lineIndex++;
            }

            var content = sb.ToString();
            const int maxBytes = 65536; // 64KB clamp
            if (content.Length > maxBytes)
            {
                content = content[..maxBytes] + Environment.NewLine + "... [Truncated: content exceeded 64KB]";
            }

            return Task.FromResult(AgentToolResult.Ok(content.TrimEnd()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to read file: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool for creating, appending, or overwriting text files.
/// </summary>
public sealed class WriteFileTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "write_file",
        Description: "Writes text content to a file. Creates parent directories automatically if needed.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("file_path", "string", "Absolute or relative path to the file", Required: true),
            new("content", "string", "The text content to write to the file", Required: true),
            new("append", "boolean", "Whether to append rather than overwrite (default false)", Required: false, DefaultValue: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPath = arguments.GetValueOrDefault("file_path")?.ToString();
        var content = arguments.GetValueOrDefault("content")?.ToString() ?? string.Empty;
        var append = arguments.TryGetValue("append", out var aObj) && aObj is not null && bool.TryParse(aObj.ToString(), out var b) && b;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'file_path' is required."));
        }

        var decision = context.ResolvePath(rawPath);
        if (!decision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
        }

        var fullPath = decision.FullPath!;

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var utf8WithoutBom = new UTF8Encoding(false);
            if (append)
            {
                File.AppendAllText(fullPath, content, utf8WithoutBom);
            }
            else
            {
                File.WriteAllText(fullPath, content, utf8WithoutBom);
            }

            var fileInfo = new FileInfo(fullPath);
            var mimeType = Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".md" or ".txt" => "text/markdown",
                ".json" => "application/json",
                ".csv" => "text/csv",
                ".html" => "text/html",
                ".xml" => "application/xml",
                _ => "text/plain"
            };

            var artifact = new AgentArtifact(
                Guid.NewGuid().ToString("N"),
                fileInfo.Name,
                fullPath,
                mimeType,
                fileInfo.Length,
                DateTimeOffset.Now,
                $"File {(append ? "appended" : "created/updated")}: {fileInfo.Name} ({fileInfo.Length} bytes)");

            context.ArtifactEmitter?.Invoke(artifact);
            return Task.FromResult(AgentToolResult.Ok($"Successfully {(append ? "appended" : "wrote")} {content.Length} characters ({fileInfo.Length} total bytes) to {fullPath}", artifact));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to write file: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool for listing files and subdirectories with recursive filtering support up to 500 items.
/// </summary>
public sealed class ListDirectoryTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "list_directory",
        Description: "Lists files and subdirectories within a specified directory. Supports recursive traversal and filtering up to 500 items.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("directory_path", "string", "Absolute or relative path to the directory (default '.')", Required: false, DefaultValue: "."),
            new("max_items", "number", "Maximum number of items to return (default 100, max 500)", Required: false, DefaultValue: 100),
            new("recursive", "boolean", "Whether to list contents recursively through subdirectories (default false)", Required: false, DefaultValue: false),
            new("search_pattern", "string", "Optional wildcard pattern filter (e.g. *.txt, *test*)", Required: false, DefaultValue: "*")
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPath = arguments.GetValueOrDefault("directory_path")?.ToString() ?? ".";
        var decision = context.ResolvePath(rawPath);
        if (!decision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
        }

        var fullPath = decision.FullPath!;

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(AgentToolResult.Fail($"Directory not found: {fullPath}"));
        }

        try
        {
            var maxItems = 100;
            if (arguments.TryGetValue("max_items", out var miObj) && miObj is not null && int.TryParse(miObj.ToString(), out var mi))
            {
                maxItems = Math.Max(1, Math.Min(mi, 500));
            }

            var recursive = arguments.TryGetValue("recursive", out var rObj) && rObj is not null &&
                            bool.TryParse(rObj.ToString(), out var rec) && rec;

            var pattern = arguments.GetValueOrDefault("search_pattern")?.ToString();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                pattern = "*";
            }

            var entries = new List<string>();
            var totalDirs = 0;
            var totalFiles = 0;

            if (!recursive)
            {
                var dirInfo = new DirectoryInfo(fullPath);

                foreach (var subDir in dirInfo.EnumerateDirectories(pattern))
                {
                    totalDirs++;
                    if (entries.Count < maxItems)
                    {
                        entries.Add($"[DIR]  {subDir.Name}/");
                    }
                }

                foreach (var file in dirInfo.EnumerateFiles(pattern))
                {
                    totalFiles++;
                    if (entries.Count < maxItems)
                    {
                        entries.Add($"[FILE] {file.Name} ({file.Length / 1024.0:F1} KB, Modified {file.LastWriteTime:yyyy-MM-dd HH:mm})");
                    }
                }
            }
            else
            {
                // Safe recursive traversal handling permission-denied directories
                var dirQueue = new Queue<string>();
                dirQueue.Enqueue(fullPath);

                while (dirQueue.Count > 0 && entries.Count < maxItems && !cancellationToken.IsCancellationRequested)
                {
                    var currentDir = dirQueue.Dequeue();

                    try
                    {
                        var subDirs = Directory.GetDirectories(currentDir);
                        foreach (var subDir in subDirs)
                        {
                            dirQueue.Enqueue(subDir);
                            var relPath = Path.GetRelativePath(fullPath, subDir);
                            totalDirs++;
                            if (entries.Count < maxItems)
                            {
                                entries.Add($"[DIR]  {relPath.Replace('\\', '/')}/");
                            }
                        }
                    }
                    catch (UnauthorizedAccessException) { /* skip inaccessible directory */ }
                    catch (Exception) { /* skip */ }

                    try
                    {
                        var files = Directory.GetFiles(currentDir, pattern);
                        foreach (var file in files)
                        {
                            totalFiles++;
                            if (entries.Count < maxItems)
                            {
                                var fi = new FileInfo(file);
                                var relPath = Path.GetRelativePath(fullPath, file);
                                entries.Add($"[FILE] {relPath.Replace('\\', '/')} ({fi.Length / 1024.0:F1} KB, Modified {fi.LastWriteTime:yyyy-MM-dd HH:mm})");
                            }
                        }
                    }
                    catch (UnauthorizedAccessException) { /* skip inaccessible directory */ }
                    catch (Exception) { /* skip */ }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Directory contents for '{fullPath}' (Pattern: '{pattern}', Recursive: {recursive}):");
            sb.AppendLine($"Showing {entries.Count} items (Total scanned: {totalDirs} directories, {totalFiles} files)" +
                          (entries.Count >= maxItems ? $" [Capped at {maxItems} items]" : ""));
            sb.AppendLine();

            if (entries.Count == 0)
            {
                sb.AppendLine("(No matching files or directories found)");
            }
            else
            {
                foreach (var entry in entries)
                {
                    sb.AppendLine(entry);
                }
            }

            return Task.FromResult(AgentToolResult.Ok(sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to list directory: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool for searching files by glob pattern with recursive traversal and metadata filtering up to 500 items.
/// </summary>
public sealed class SearchFilesTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "search_files",
        Description: "Searches for files matching a pattern (e.g. *.pdf, *invoice*, *.cs) across directories with recursive filters up to 500 results.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("search_pattern", "string", "Glob search pattern (e.g. *.csv, *.pdf, *invoice*)", Required: true),
            new("directory_path", "string", "Directory path to search in (default is working directory '.')", Required: false, DefaultValue: "."),
            new("recursive", "boolean", "Whether to search recursively through subdirectories (default true)", Required: false, DefaultValue: true),
            new("max_results", "number", "Maximum number of search results to return (default 100, max 500)", Required: false, DefaultValue: 100),
            new("extension_filter", "string", "Optional semicolon-separated file extensions to filter by (e.g. '.cs;.json;.txt')", Required: false),
            new("min_size_bytes", "number", "Optional minimum file size in bytes", Required: false),
            new("max_size_bytes", "number", "Optional maximum file size in bytes", Required: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var pattern = arguments.GetValueOrDefault("search_pattern")?.ToString();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'search_pattern' is required."));
        }

        var rawPath = arguments.GetValueOrDefault("directory_path")?.ToString() ?? ".";
        var decision = context.ResolvePath(rawPath);
        if (!decision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
        }

        var fullPath = decision.FullPath!;
        var recursive = !arguments.TryGetValue("recursive", out var rObj) || rObj is null || !bool.TryParse(rObj.ToString(), out var r) || r;

        var maxResults = 100;
        if (arguments.TryGetValue("max_results", out var mrObj) && mrObj is not null && int.TryParse(mrObj.ToString(), out var mr))
        {
            maxResults = Math.Max(1, Math.Min(mr, 500));
        }

        HashSet<string>? allowedExts = null;
        var extFilter = arguments.GetValueOrDefault("extension_filter")?.ToString();
        if (!string.IsNullOrWhiteSpace(extFilter))
        {
            allowedExts = extFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        long? minSizeBytes = null;
        if (arguments.TryGetValue("min_size_bytes", out var minObj) && minObj is not null && long.TryParse(minObj.ToString(), out var minS))
        {
            minSizeBytes = minS;
        }

        long? maxSizeBytes = null;
        if (arguments.TryGetValue("max_size_bytes", out var maxObj) && maxObj is not null && long.TryParse(maxObj.ToString(), out var maxS))
        {
            maxSizeBytes = maxS;
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult(AgentToolResult.Fail($"Directory not found: {fullPath}"));
        }

        try
        {
            var matchedFiles = new List<FileInfo>();
            var scannedCount = 0;

            if (!recursive)
            {
                var dirInfo = new DirectoryInfo(fullPath);
                foreach (var fi in dirInfo.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    scannedCount++;
                    if (allowedExts is not null && !allowedExts.Contains(fi.Extension)) continue;
                    if (minSizeBytes.HasValue && fi.Length < minSizeBytes.Value) continue;
                    if (maxSizeBytes.HasValue && fi.Length > maxSizeBytes.Value) continue;

                    matchedFiles.Add(fi);
                    if (matchedFiles.Count >= maxResults) break;
                }
            }
            else
            {
                // Safe breadth-first traversal with exception handling per directory
                var queue = new Queue<string>();
                queue.Enqueue(fullPath);

                while (queue.Count > 0 && matchedFiles.Count < maxResults && !cancellationToken.IsCancellationRequested)
                {
                    var current = queue.Dequeue();

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(current))
                        {
                            queue.Enqueue(sub);
                        }
                    }
                    catch (UnauthorizedAccessException) { /* skip protected folder */ }
                    catch (Exception) { /* skip */ }

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(current, pattern))
                        {
                            scannedCount++;
                            var fi = new FileInfo(file);
                            if (allowedExts is not null && !allowedExts.Contains(fi.Extension)) continue;
                            if (minSizeBytes.HasValue && fi.Length < minSizeBytes.Value) continue;
                            if (maxSizeBytes.HasValue && fi.Length > maxSizeBytes.Value) continue;

                            matchedFiles.Add(fi);
                            if (matchedFiles.Count >= maxResults) break;
                        }
                    }
                    catch (UnauthorizedAccessException) { /* skip protected files */ }
                    catch (Exception) { /* skip */ }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {matchedFiles.Count} matching files (Pattern: '{pattern}', Path: '{fullPath}')" +
                          (matchedFiles.Count >= maxResults ? $" [Capped at {maxResults} results]" : "") + ":");

            foreach (var fi in matchedFiles)
            {
                var relPath = Path.GetRelativePath(fullPath, fi.FullName).Replace('\\', '/');
                sb.AppendLine($"- {relPath} ({fi.Length / 1024.0:F1} KB, Modified {fi.LastWriteTime:yyyy-MM-dd HH:mm}) -> {fi.FullName}");
            }

            return Task.FromResult(AgentToolResult.Ok(sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Search failed: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool for moving or renaming files or directories.
/// </summary>
public sealed class MoveFileTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "move_file",
        Description: "Moves or renames a file or folder from source to destination path.",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("source_path", "string", "Current path to the file or directory", Required: true),
            new("destination_path", "string", "New destination path", Required: true),
            new("overwrite", "boolean", "Whether to overwrite existing destination file (default false)", Required: false, DefaultValue: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var src = arguments.GetValueOrDefault("source_path")?.ToString();
        var dst = arguments.GetValueOrDefault("destination_path")?.ToString();
        var overwrite = arguments.TryGetValue("overwrite", out var oObj) && oObj is not null && bool.TryParse(oObj.ToString(), out var o) && o;

        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
        {
            return Task.FromResult(AgentToolResult.Fail("Both 'source_path' and 'destination_path' are required."));
        }

        // Both ends are checked. Moving a file out of the workspace is just as
        // much of an escape as writing one there.
        var srcDecision = context.ResolvePath(src);
        if (!srcDecision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(srcDecision.DenialReason!));
        }

        var dstDecision = context.ResolvePath(dst);
        if (!dstDecision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(dstDecision.DenialReason!));
        }

        var fullSrc = srcDecision.FullPath!;
        var fullDst = dstDecision.FullPath!;

        if (!File.Exists(fullSrc) && !Directory.Exists(fullSrc))
        {
            return Task.FromResult(AgentToolResult.Fail($"Source does not exist: {fullSrc}"));
        }

        try
        {
            var dstDir = Path.GetDirectoryName(fullDst);
            if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
            {
                Directory.CreateDirectory(dstDir);
            }

            if (File.Exists(fullSrc))
            {
                File.Move(fullSrc, fullDst, overwrite);
            }
            else
            {
                Directory.Move(fullSrc, fullDst);
            }

            return Task.FromResult(AgentToolResult.Ok($"Successfully moved '{fullSrc}' -> '{fullDst}'"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to move: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool for deleting files or directories. Gated with High Risk.
/// </summary>
public sealed class DeleteFileTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "delete_file",
        Description: "Permanently deletes a file or directory. Requires user approval (High Risk).",
        Category: "filesystem",
        RiskLevel: AgentRiskLevel.High,
        Parameters:
        [
            new("path", "string", "Path to the file or directory to delete", Required: true),
            new("recursive", "boolean", "Whether to delete directory recursively (default false)", Required: false, DefaultValue: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPath = arguments.GetValueOrDefault("path")?.ToString();
        var recursive = arguments.TryGetValue("recursive", out var rObj) && rObj is not null && bool.TryParse(rObj.ToString(), out var r) && r;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Task.FromResult(AgentToolResult.Fail("Parameter 'path' is required."));
        }

        var decision = context.ResolvePath(rawPath);
        if (!decision.Allowed)
        {
            return Task.FromResult(AgentToolResult.Fail(decision.DenialReason!));
        }

        var fullPath = decision.FullPath!;

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Task.FromResult(AgentToolResult.Ok($"Deleted file: {fullPath}", null));
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive);
                return Task.FromResult(AgentToolResult.Ok($"Deleted directory: {fullPath}", null));
            }
            else
            {
                return Task.FromResult(AgentToolResult.Fail($"Target not found: {fullPath}"));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to delete: {ex.Message}"));
        }
    }
}
