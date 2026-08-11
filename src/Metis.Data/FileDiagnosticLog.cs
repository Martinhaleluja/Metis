using System.Text.RegularExpressions;
using Metis.Core.Contracts;

namespace Metis.Data;

public sealed partial class FileDiagnosticLog : IDiagnosticLog
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private readonly object _gate = new();

    public FileDiagnosticLog(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Metis",
            "logs");
        LogPath = Path.Combine(directory, "metis.log");
    }

    public string LogPath { get; }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                RotateIfNeeded();

                var cleanMessage = Redact(message);
                var cleanException = exception is null ? null : Redact(exception.ToString());
                var line = $"{DateTimeOffset.Now:O} [{level}] {cleanMessage}";
                if (!string.IsNullOrWhiteSpace(cleanException))
                {
                    line += Environment.NewLine + cleanException;
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Diagnostics must never make the companion fail.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must never make the companion fail.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(Path.GetDirectoryName(LogPath)!, "metis.previous.log");
        File.Move(LogPath, previousPath, true);
    }

    private static string Redact(string value)
    {
        var redacted = KeyQueryRegex().Replace(value, "$1[redacted]");
        redacted = KeyHeaderRegex().Replace(redacted, "$1[redacted]");
        redacted = AqTokenRegex().Replace(redacted, "[redacted-token]");
        redacted = AizaTokenRegex().Replace(redacted, "[redacted-token]");
        return redacted;
    }

    [GeneratedRegex("([?&]key=)[^&\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyQueryRegex();

    [GeneratedRegex("(x-goog-api-key\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyHeaderRegex();

    [GeneratedRegex("AQ\\.[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex AqTokenRegex();

    [GeneratedRegex("AIza[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex AizaTokenRegex();
}
