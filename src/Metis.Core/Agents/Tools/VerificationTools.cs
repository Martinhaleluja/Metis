using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Dedicated tool for verifying task outputs, created artifacts, file content, process exit codes, and JSON validity.
/// </summary>
public sealed class VerifyTaskOutputTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "verify_task_output",
        Description: "Explicitly verifies task outputs (file existence, regex/substring contents, JSON validity, exit codes, line counts, directory contents) to ensure correctness.",
        Category: "verification",
        RiskLevel: AgentRiskLevel.Low,
        Parameters:
        [
            new("check_type", "string", "Verification type: 'file_exists', 'file_not_exists', 'file_contains', 'file_regex', 'file_min_lines', 'file_min_size', 'directory_not_empty', 'json_valid', 'exit_code', 'command_success'", Required: true),
            new("target_path", "string", "Target file or directory path to inspect", Required: false),
            new("expected_text", "string", "Expected substring, regex pattern, or JSON property name", Required: false),
            new("min_size_bytes", "number", "Minimum file size in bytes (for 'file_min_size' or 'file_exists')", Required: false),
            new("min_count", "number", "Minimum line count or directory entry count (default 1)", Required: false, DefaultValue: 1),
            new("expected_exit_code", "number", "Expected process exit code (default 0)", Required: false, DefaultValue: 0),
            new("actual_exit_code", "number", "Actual exit code to verify for 'exit_code' check", Required: false),
            new("command", "string", "PowerShell verification command to execute for 'command_success' check", Required: false),
            new("case_sensitive", "boolean", "Whether substring/regex checks are case-sensitive (default false)", Required: false, DefaultValue: false)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var checkType = arguments.GetValueOrDefault("check_type")?.ToString()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(checkType))
        {
            return AgentToolResult.Fail("Parameter 'check_type' is required. Valid options: 'file_exists', 'file_not_exists', 'file_contains', 'file_regex', 'file_min_lines', 'file_min_size', 'directory_not_empty', 'json_valid', 'exit_code', 'command_success'.");
        }

        // A verification that reads outside the workspace would be a way to
        // learn what is on the machine without ever calling read_file, so this
        // is checked like any other path.
        var rawPath = arguments.GetValueOrDefault("target_path")?.ToString();
        string? fullPath = null;
        if (!string.IsNullOrWhiteSpace(rawPath))
        {
            var decision = context.ResolvePath(rawPath);
            if (!decision.Allowed)
            {
                return AgentToolResult.Fail(decision.DenialReason!);
            }

            fullPath = decision.FullPath;
        }

        var expectedText = arguments.GetValueOrDefault("expected_text")?.ToString();
        var caseSensitive = arguments.TryGetValue("case_sensitive", out var csObj) && csObj is not null &&
                             bool.TryParse(csObj.ToString(), out var cs) && cs;

        long? minSizeBytes = null;
        if (arguments.TryGetValue("min_size_bytes", out var msObj) && msObj is not null && long.TryParse(msObj.ToString(), out var msVal))
        {
            minSizeBytes = msVal;
        }

        var minCount = 1;
        if (arguments.TryGetValue("min_count", out var mcObj) && mcObj is not null && int.TryParse(mcObj.ToString(), out var mcVal))
        {
            minCount = Math.Max(1, mcVal);
        }

        var expectedExitCode = 0;
        if (arguments.TryGetValue("expected_exit_code", out var eecObj) && eecObj is not null && int.TryParse(eecObj.ToString(), out var eecVal))
        {
            expectedExitCode = eecVal;
        }

        switch (checkType)
        {
            case "file_exists":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'file_exists' check.");

                if (!File.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File does not exist: {fullPath}");

                var fi = new FileInfo(fullPath);
                if (minSizeBytes.HasValue && fi.Length < minSizeBytes.Value)
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File '{fullPath}' exists but size ({fi.Length} bytes) is less than required minimum ({minSizeBytes.Value} bytes).");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] File '{fullPath}' exists ({fi.Length} bytes, modified {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}).");
            }

            case "file_not_exists":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'file_not_exists' check.");

                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] Target still exists: {fullPath}");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] Target does not exist as expected: {fullPath}");
            }

            case "file_contains":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'file_contains' check.");
                if (string.IsNullOrEmpty(expectedText))
                    return AgentToolResult.Fail("'expected_text' is required for 'file_contains' check.");

                if (!File.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File does not exist: {fullPath}");

                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                if (!content.Contains(expectedText, comparison))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File '{fullPath}' does not contain expected text: '{expectedText}'.");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] File '{fullPath}' contains expected text: '{expectedText}'.");
            }

            case "file_regex":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'file_regex' check.");
                if (string.IsNullOrEmpty(expectedText))
                    return AgentToolResult.Fail("'expected_text' (regex pattern) is required for 'file_regex' check.");

                if (!File.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File does not exist: {fullPath}");

                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var options = RegexOptions.Multiline | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                var match = Regex.Match(content, expectedText, options);
                if (!match.Success)
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File '{fullPath}' content did not match regex: /{expectedText}/.");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] File '{fullPath}' matched regex /{expectedText}/ (Found: '{match.Value}').");
            }

            case "file_min_lines":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'file_min_lines' check.");

                if (!File.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File does not exist: {fullPath}");

                var lineCount = File.ReadLines(fullPath).Take(minCount + 1).Count();
                if (lineCount < minCount)
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File '{fullPath}' has fewer than {minCount} lines (actual count: {lineCount}).");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] File '{fullPath}' has at least {minCount} lines.");
            }

            case "file_min_size":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'file_min_size' check.");

                if (!File.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File does not exist: {fullPath}");

                var targetMin = minSizeBytes ?? 1;
                var fi = new FileInfo(fullPath);
                if (fi.Length < targetMin)
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] File '{fullPath}' size ({fi.Length} bytes) is less than required minimum ({targetMin} bytes).");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] File '{fullPath}' size is {fi.Length} bytes (>= {targetMin} bytes).");
            }

            case "directory_not_empty":
            {
                if (string.IsNullOrWhiteSpace(fullPath))
                    return AgentToolResult.Fail("'target_path' is required for 'directory_not_empty' check.");

                if (!Directory.Exists(fullPath))
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] Directory does not exist: {fullPath}");

                var pattern = string.IsNullOrWhiteSpace(expectedText) ? "*" : expectedText;
                var entriesCount = Directory.EnumerateFileSystemEntries(fullPath, pattern).Take(minCount + 1).Count();
                if (entriesCount < minCount)
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] Directory '{fullPath}' has fewer than {minCount} matching entries (Pattern: '{pattern}', Count: {entriesCount}).");

                return AgentToolResult.Ok($"[VERIFICATION PASSED] Directory '{fullPath}' contains at least {minCount} matching entries (Pattern: '{pattern}').");
            }

            case "json_valid":
            {
                string jsonString;
                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                {
                    jsonString = await File.ReadAllTextAsync(fullPath, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(expectedText))
                {
                    jsonString = expectedText;
                }
                else
                {
                    return AgentToolResult.Fail("Provide either 'target_path' pointing to a JSON file or 'expected_text' with JSON string.");
                }

                try
                {
                    using var doc = JsonDocument.Parse(jsonString);
                    if (!string.IsNullOrWhiteSpace(expectedText) && !string.IsNullOrWhiteSpace(fullPath))
                    {
                        if (doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.TryGetProperty(expectedText, out _))
                        {
                            return AgentToolResult.Fail($"[VERIFICATION FAILED] JSON in '{fullPath}' is valid, but missing expected property '{expectedText}'.");
                        }
                    }

                    return AgentToolResult.Ok($"[VERIFICATION PASSED] JSON is well-formed." + (!string.IsNullOrWhiteSpace(fullPath) ? $" File: '{fullPath}'." : ""));
                }
                catch (JsonException jEx)
                {
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] Invalid JSON: {jEx.Message}");
                }
            }

            case "exit_code":
            {
                if (!arguments.TryGetValue("actual_exit_code", out var aecObj) || aecObj is null || !int.TryParse(aecObj.ToString(), out var actualCode))
                {
                    return AgentToolResult.Fail("'actual_exit_code' parameter is required for 'exit_code' check.");
                }

                if (actualCode != expectedExitCode)
                {
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] Expected exit code {expectedExitCode}, but received {actualCode}.");
                }

                return AgentToolResult.Ok($"[VERIFICATION PASSED] Exit code matches expected: {actualCode}.");
            }

            case "command_success":
            {
                var cmd = arguments.GetValueOrDefault("command")?.ToString();
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    return AgentToolResult.Fail("'command' parameter is required for 'command_success' check.");
                }

                var workingDir = !string.IsNullOrWhiteSpace(fullPath) && Directory.Exists(fullPath)
                    ? fullPath
                    : (Directory.Exists(context.WorkingDirectory) ? context.WorkingDirectory : Environment.CurrentDirectory);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{cmd.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = startInfo };
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await proc.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return AgentToolResult.Fail("[VERIFICATION FAILED] Verification command timed out after 30 seconds.");
                }

                if (proc.ExitCode != 0)
                {
                    return AgentToolResult.Fail($"[VERIFICATION FAILED] Command exited with code {proc.ExitCode}.\nStderr: {stderr.ToString().Trim()}\nStdout: {stdout.ToString().Trim()}");
                }

                var outStr = stdout.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(expectedText))
                {
                    var comp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    if (!outStr.Contains(expectedText, comp))
                    {
                        return AgentToolResult.Fail($"[VERIFICATION FAILED] Command succeeded (exit 0) but output did not contain expected text '{expectedText}'. Output: {outStr}");
                    }
                }

                return AgentToolResult.Ok($"[VERIFICATION PASSED] Command succeeded with exit code 0." + (!string.IsNullOrWhiteSpace(outStr) ? $"\nOutput: {outStr}" : ""));
            }

            default:
                return AgentToolResult.Fail($"Unknown check_type '{checkType}'. Supported: 'file_exists', 'file_not_exists', 'file_contains', 'file_regex', 'file_min_lines', 'file_min_size', 'directory_not_empty', 'json_valid', 'exit_code', 'command_success'.");
        }
    }
}
