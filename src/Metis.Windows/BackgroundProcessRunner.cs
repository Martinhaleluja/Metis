using System.Diagnostics;
using System.IO;
using System.Text;

namespace Metis.Windows;

/// <summary>
/// Execution output from running a background CLI process.
/// </summary>
public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed);

/// <summary>
/// Safe asynchronous process executor for background commands with clean cancellation and process-tree cleanup.
/// </summary>
public static class BackgroundProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
        string executable,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? stdoutHandler = null,
        IProgress<string>? stderrHandler = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(2);

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                stdoutHandler?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                stderrHandler?.Report(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(linkedCts.Token);
            sw.Stop();

            return new ProcessRunResult(
                process.ExitCode,
                stdout.ToString().Trim(),
                stderr.ToString().Trim(),
                sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process already terminated
            }

            sw.Stop();
            throw new TimeoutException($"Process '{executable}' timed out after {effectiveTimeout.TotalSeconds:F0}s or was cancelled.");
        }
    }
}
