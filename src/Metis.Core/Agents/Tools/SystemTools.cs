using System.Diagnostics;
using System.IO;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Tool for listing active system processes with sorting and filtering options up to 500 items.
/// </summary>
public sealed class ListProcessesTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "list_processes",
        Description: "Lists currently running system processes and their memory consumption. Supports filtering and sorting up to 500 processes.",
        Category: "system",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("filter_name", "string", "Optional name filter (case-insensitive substring)", Required: false),
            new("top_count", "number", "Number of top processes to return (default 25, max 500)", Required: false, DefaultValue: 25),
            new("sort_by", "string", "Sort order: 'memory', 'name', 'id' (default 'memory')", Required: false, DefaultValue: "memory")
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var filter = arguments.GetValueOrDefault("filter_name")?.ToString();
            var topCount = 25;
            if (arguments.TryGetValue("top_count", out var tcObj) && tcObj is not null && int.TryParse(tcObj.ToString(), out var tc))
            {
                topCount = Math.Max(1, Math.Min(tc, 500));
            }

            var sortBy = arguments.GetValueOrDefault("sort_by")?.ToString()?.Trim().ToLowerInvariant() ?? "memory";

            var procs = Process.GetProcesses();
            var query = procs.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                query = query.Where(p => p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            query = sortBy switch
            {
                "name" => query.OrderBy(p => p.ProcessName),
                "id" => query.OrderBy(p => p.Id),
                _ => query.OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; } catch { return 0L; }
                })
            };

            var sorted = query
                .Take(topCount)
                .Select(p =>
                {
                    try
                    {
                        var memMb = p.WorkingSet64 / (1024.0 * 1024.0);
                        return $"- [{p.Id,6}] {p.ProcessName} ({memMb:F1} MB RAM)";
                    }
                    catch
                    {
                        return $"- [{p.Id,6}] {p.ProcessName}";
                    }
                })
                .ToList();

            var output = $"Active System Processes (Showing {sorted.Count} of {procs.Length} total, Sorted by: '{sortBy}'):\n" +
                         (sorted.Count > 0 ? string.Join("\n", sorted) : "(No matching processes found)");

            return Task.FromResult(AgentToolResult.Ok(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to list processes: {ex.Message}"));
        }
    }
}

/// <summary>
/// Tool for registering a generated file or report as an output artifact for the user.
/// </summary>
public sealed class EmitArtifactTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "emit_artifact",
        Description: "Registers a created file, report, or dataset as an output artifact to present to the user.",
        Category: "system",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("file_path", "string", "Path to the generated artifact file", Required: true),
            new("summary", "string", "Brief description of the artifact and what it contains", Required: true),
            new("mime_type", "string", "Optional MIME type override (e.g. application/json, text/csv)", Required: false)
        ]);

    public Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var rawPath = arguments.GetValueOrDefault("file_path")?.ToString();
        var summary = arguments.GetValueOrDefault("summary")?.ToString() ?? "Generated Artifact";
        var customMime = arguments.GetValueOrDefault("mime_type")?.ToString();

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
            return Task.FromResult(AgentToolResult.Fail($"Artifact file does not exist: {fullPath}"));
        }

        try
        {
            var fi = new FileInfo(fullPath);
            var mimeType = !string.IsNullOrWhiteSpace(customMime)
                ? customMime
                : Path.GetExtension(fullPath).ToLowerInvariant() switch
                {
                    ".md" or ".txt" => "text/markdown",
                    ".json" => "application/json",
                    ".csv" => "text/csv",
                    ".pdf" => "application/pdf",
                    ".html" or ".htm" => "text/html",
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".svg" => "image/svg+xml",
                    ".xml" => "application/xml",
                    ".zip" => "application/zip",
                    _ => "application/octet-stream"
                };

            var artifact = new AgentArtifact(
                Guid.NewGuid().ToString("N"),
                fi.Name,
                fullPath,
                mimeType,
                fi.Length,
                DateTimeOffset.Now,
                summary);

            context.ArtifactEmitter?.Invoke(artifact);
            return Task.FromResult(AgentToolResult.Ok($"Artifact '{fi.Name}' ({fi.Length / 1024.0:F1} KB) registered successfully.", artifact));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AgentToolResult.Fail($"Failed to register artifact: {ex.Message}"));
        }
    }
}
