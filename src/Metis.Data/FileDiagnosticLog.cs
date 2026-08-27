using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Metis.Core.Contracts;

namespace Metis.Data;

/// <summary>
/// Appends diagnostics to a rolling file, off the thread that logged them.
///
/// Every call used to do <c>Directory.CreateDirectory</c>, a
/// <c>FileInfo.Length</c> stat and a <c>File.AppendAllText</c> — three syscalls
/// and a file handle open/close — inside a lock, on whichever thread happened
/// to be logging. On the response path that thread is the UI thread, and the
/// turn logs several times, so the app paid for its own diagnostics in dropped
/// frames. Callers now hand over a timestamped record and return immediately;
/// one background writer redacts, formats and appends.
/// </summary>
public sealed partial class FileDiagnosticLog : IDiagnosticLog, IDisposable
{
    private const long MaxLogBytes = 2 * 1024 * 1024;

    private readonly BlockingCollection<Entry> _queue = new(new ConcurrentQueue<Entry>());
    private readonly Thread _writer;

    /// <summary>
    /// Bytes in the current file, tracked as they are written so rotation does
    /// not need to stat the file on every line. -1 until the first write reads
    /// the size once.
    /// </summary>
    private long _bytesWritten = -1;
    private bool _disposed;

    public FileDiagnosticLog(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Metis",
            "logs");
        LogPath = Path.Combine(directory, "metis.log");

        _writer = new Thread(DrainQueue)
        {
            Name = "Metis diagnostics",
            IsBackground = true
        };
        _writer.Start();
    }

    public string LogPath { get; }

    public void Info(string message) => Enqueue("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Enqueue("ERROR", message, exception);

    /// <summary>
    /// Blocks until everything logged so far has reached the file. Only for
    /// shutdown and for tests that read the file back; the response path must
    /// never call this.
    /// </summary>
    public void Flush(TimeSpan? timeout = null)
    {
        if (_disposed)
        {
            return;
        }

        using var written = new ManualResetEventSlim(false);
        try
        {
            _queue.Add(new Entry(DateTimeOffset.Now, null, string.Empty, null, written));
        }
        catch (InvalidOperationException)
        {
            // Adding was completed by Dispose; nothing left to wait for.
            return;
        }

        written.Wait(timeout ?? TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private void Enqueue(string level, string message, Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // The timestamp is taken here rather than on the writer so the log
            // still records when something happened, not when it was flushed.
            _queue.Add(new Entry(DateTimeOffset.Now, level, message, exception, null));
        }
        catch (InvalidOperationException)
        {
            // Adding was completed during shutdown. Diagnostics must never make
            // the companion fail.
        }
    }

    private void DrainQueue()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            if (entry.Level is not null)
            {
                Write(entry);
            }

            entry.Written?.Set();
        }
    }

    private void Write(Entry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(directory);

            var cleanMessage = Redact(entry.Message);
            var cleanException = entry.Exception is null ? null : Redact(entry.Exception.ToString());
            var line = new StringBuilder()
                .Append(entry.Timestamp.ToString("O"))
                .Append(" [")
                .Append(entry.Level)
                .Append("] ")
                .Append(cleanMessage);
            if (!string.IsNullOrWhiteSpace(cleanException))
            {
                line.Append(Environment.NewLine).Append(cleanException);
            }

            line.Append(Environment.NewLine);
            var text = line.ToString();

            RotateIfNeeded(text.Length);
            File.AppendAllText(LogPath, text);
            if (_bytesWritten >= 0)
            {
                _bytesWritten += Encoding.UTF8.GetByteCount(text);
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

    private void RotateIfNeeded(int incomingLength)
    {
        if (_bytesWritten < 0)
        {
            // First write of the session: read the size once, then track it.
            _bytesWritten = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
        }

        if (_bytesWritten + incomingLength < MaxLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(Path.GetDirectoryName(LogPath)!, "metis.previous.log");
        File.Move(LogPath, previousPath, true);
        _bytesWritten = 0;
    }

    /// <summary>
    /// Removes anything key-shaped before it reaches the file.
    ///
    /// The log is the one artefact a user is most likely to send someone when
    /// something goes wrong, which makes it the most likely way for a secret to
    /// leave the machine. Provider errors quote the request back, and Metis
    /// holds keys for six services, so every shape they use is stripped here
    /// rather than trusting each provider not to echo one.
    /// </summary>
    private static string Redact(string value)
    {
        var redacted = KeyQueryRegex().Replace(value, "$1[redacted]");
        redacted = KeyHeaderRegex().Replace(redacted, "$1[redacted]");
        redacted = BearerRegex().Replace(redacted, "$1[redacted]");
        redacted = AqTokenRegex().Replace(redacted, "[redacted-token]");
        redacted = AizaTokenRegex().Replace(redacted, "[redacted-token]");
        redacted = AnthropicKeyRegex().Replace(redacted, "[redacted-key]");
        redacted = OpenAiKeyRegex().Replace(redacted, "[redacted-key]");
        redacted = JwtRegex().Replace(redacted, "[redacted-token]");
        return redacted;
    }

    /// <summary>
    /// One queued line. A null <see cref="Level"/> marks a flush barrier that
    /// carries no text and only signals <see cref="Written"/>.
    /// </summary>
    private readonly record struct Entry(
        DateTimeOffset Timestamp,
        string? Level,
        string Message,
        Exception? Exception,
        ManualResetEventSlim? Written);

    [GeneratedRegex("([?&]key=)[^&\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyQueryRegex();

    [GeneratedRegex("(x-goog-api-key\\s*[:=]\\s*)[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyHeaderRegex();

    [GeneratedRegex("AQ\\.[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex AqTokenRegex();

    [GeneratedRegex("AIza[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex AizaTokenRegex();

    // Anthropic. Matched before the OpenAI shape below, which would otherwise
    // claim the "sk-" prefix and leave "ant-..." in the file.
    [GeneratedRegex("sk-ant-[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex AnthropicKeyRegex();

    // OpenAI, and OpenRouter, which uses the same shape.
    [GeneratedRegex("sk-(?:proj-|or-)?[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiKeyRegex();

    // Supabase session tokens, and any other JWT that finds its way in.
    [GeneratedRegex("eyJ[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    // AssemblyAI and ElevenLabs send their key in a plain header rather than a
    // recognisable prefix, so the header name is what identifies it.
    [GeneratedRegex(
        "((?:authorization|xi-api-key|x-api-key)\\s*[:=]\\s*(?:Bearer\\s+)?)[^\\s,;\"]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();
}
