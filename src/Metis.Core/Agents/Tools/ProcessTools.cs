using System.Diagnostics;
using System.IO;
using System.Text;

namespace Metis.Core.Agents.Tools;

/// <summary>
/// Tool for executing PowerShell commands, batch processes, or scripts with streaming progress and up to 600s timeouts.
/// </summary>
public sealed class ExecutePowerShellTool : IAgentTool
{
    public AgentToolDeclaration Declaration { get; } = new(
        Name: "execute_powershell",
        Description: "Executes a PowerShell script or multi-command batch in the background. Captures stdout/stderr, streams progress, and supports timeouts up to 600s.",
        Category: "process",
        RiskLevel: AgentRiskLevel.Medium,
        Parameters:
        [
            new("command", "string", "The PowerShell command or script block to execute", Required: true),
            new("timeout_seconds", "number", "Maximum execution time in seconds (default 60, max 600)", Required: false, DefaultValue: 60),
            new("working_directory", "string", "Working directory for execution (default is agent task directory)", Required: false)
        ]);

    public async Task<AgentToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var command = arguments.GetValueOrDefault("command")?.ToString();
        if (string.IsNullOrWhiteSpace(command))
        {
            return AgentToolResult.Fail("Parameter 'command' is required.");
        }

        var timeoutSec = 60;
        if (arguments.TryGetValue("timeout_seconds", out var tsObj) && tsObj is not null && int.TryParse(tsObj.ToString(), out var ts))
        {
            timeoutSec = Math.Max(1, Math.Min(ts, 600));
        }

        // Where the command starts. A custom directory is checked like any
        // other path, because "run this in my Documents folder" is the same
        // escape as writing a file there.
        //
        // Being honest about the limit of this: PowerShell can change directory
        // wherever it likes once it is running, and no argument check prevents
        // that. What this stops is the easy, accidental version -- an agent
        // that simply asks to work somewhere else. The deliberate version is
        // left to the approval gate, which is what execute_powershell being
        // Medium risk is for.
        var customWorkDir = arguments.GetValueOrDefault("working_directory")?.ToString();
        string workingDir;
        if (!string.IsNullOrWhiteSpace(customWorkDir))
        {
            var decision = context.ResolvePath(customWorkDir);
            if (!decision.Allowed)
            {
                return AgentToolResult.Fail(decision.DenialReason!);
            }

            workingDir = Directory.Exists(decision.FullPath) ? decision.FullPath! : context.WorkingDirectory;
        }
        else
        {
            workingDir = Directory.Exists(context.WorkingDirectory)
                ? context.WorkingDirectory
                : Environment.CurrentDirectory;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        string? tempScriptPath = null;
        Process? process = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // If script is multi-line or contains complex syntax, save to a temporary .ps1 script file
            // to avoid shell command-line quoting/escaping corruption.
            if (command.Contains('\n') || command.Contains('\r') || command.Length > 200 || command.Contains('"'))
            {
                tempScriptPath = Path.Combine(Path.GetTempPath(), $"metis_agent_{Guid.NewGuid():N}.ps1");
                await File.WriteAllTextAsync(tempScriptPath, command, new UTF8Encoding(false), linkedCts.Token);
                startInfo.Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"";
            }
            else
            {
                startInfo.Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"";
            }

            process = new Process { StartInfo = startInfo };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            const int maxOutputChars = 524288; // 512KB output limit

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    if (stdout.Length < maxOutputChars)
                    {
                        stdout.AppendLine(e.Data);
                    }
                    context.ProgressReporter?.Report(e.Data);
                    context.Logger?.Invoke(new AgentLogEntry(DateTimeOffset.Now, "INFO", e.Data));
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    if (stderr.Length < maxOutputChars)
                    {
                        stderr.AppendLine(e.Data);
                    }
                    context.Logger?.Invoke(new AgentLogEntry(DateTimeOffset.Now, "WARN", e.Data));
                }
            };

            var stopwatch = Stopwatch.StartNew();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(linkedCts.Token);
            stopwatch.Stop();

            var exitCode = process.ExitCode;
            var output = stdout.ToString().Trim();
            var error = stderr.ToString().Trim();

            if (exitCode == 0)
            {
                var resultText = string.IsNullOrWhiteSpace(output)
                    ? $"(Command completed successfully in {stopwatch.ElapsedMilliseconds} ms with no output)"
                    : output;

                return AgentToolResult.Ok(resultText);
            }
            else
            {
                var failureMessage = $"Process exited with code {exitCode} in {stopwatch.ElapsedMilliseconds} ms." +
                                     (!string.IsNullOrWhiteSpace(error) ? $"\nStderr: {error}" : "") +
                                     (!string.IsNullOrWhiteSpace(output) ? $"\nStdout: {output}" : "");
                return AgentToolResult.Fail(failureMessage);
            }
        }
        catch (OperationCanceledException)
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AgentToolResult.Fail("Process execution was cancelled.");
            }

            return AgentToolResult.Fail($"Execution timed out after {timeoutSec} seconds.");
        }
        catch (Exception ex)
        {
            return AgentToolResult.Fail($"Process execution failed: {ex.Message}");
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                process.Dispose();
            }

            if (tempScriptPath is not null && File.Exists(tempScriptPath))
            {
                try { File.Delete(tempScriptPath); } catch { }
            }
        }
    }
}
