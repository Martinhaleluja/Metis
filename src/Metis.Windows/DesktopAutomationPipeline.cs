using System.Threading.Channels;
using Metis.Core.Contracts;
using Metis.Core.Models;

namespace Metis.Windows;

/// <summary>
/// Decouples provider inference from Windows input. A bounded channel prevents
/// unbounded command growth and a single reader preserves cursor action order.
/// </summary>
public sealed class DesktopAutomationPipeline : IDesktopAutomationPipeline
{
    private const int DefaultCapacity = 24;
    private readonly object _gate = new();
    private readonly IDesktopAutomationService _executor;
    private readonly int _capacity;
    private Channel<AutomationWorkItem>? _channel;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _worker;
    private bool _sessionStopped;
    private bool _emergencyStopped;
    private bool _disposed;

    public DesktopAutomationPipeline(IDesktopAutomationService executor, int capacity = DefaultCapacity)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _capacity = Math.Clamp(capacity, 1, 128);
    }

    public bool IsEmergencyStopped
    {
        get
        {
            lock (_gate)
            {
                return _emergencyStopped;
            }
        }
    }

    public void StartSession()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessionStopped && _worker is { IsCompleted: false })
            {
                return;
            }

            _sessionCancellation?.Dispose();
            _sessionCancellation = new CancellationTokenSource();
            _channel = Channel.CreateBounded<AutomationWorkItem>(new BoundedChannelOptions(_capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            _sessionStopped = false;
            _emergencyStopped = false;
            var reader = _channel.Reader;
            var sessionToken = _sessionCancellation.Token;
            _worker = Task.Run(() => ProcessAsync(reader, sessionToken), CancellationToken.None);
        }
    }

    public async Task<DesktopActionResult> EnqueueAsync(
        DesktopAction action,
        ScreenCapture capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(capture);

        ChannelWriter<AutomationWorkItem> writer;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessionStopped)
            {
                throw new OperationCanceledException(
                    _emergencyStopped
                        ? "Desktop automation was stopped with F12. Start a new voice request to resume."
                        : "The desktop automation session was cancelled.",
                    cancellationToken);
            }

            if (_channel is null || _worker is null || _worker.IsCompleted)
            {
                throw new InvalidOperationException("Start the desktop automation session before queuing actions.");
            }

            writer = _channel.Writer;
        }

        var completion = new TaskCompletionSource<DesktopActionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workItem = new AutomationWorkItem(action, capture, completion, cancellationToken);
        await writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void CancelSession() => StopSession(emergency: false);

    public void EmergencyStop() => StopSession(emergency: true);

    private void StopSession(bool emergency)
    {
        Channel<AutomationWorkItem>? channel;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed || _sessionStopped)
            {
                _emergencyStopped |= emergency;
                return;
            }

            _sessionStopped = true;
            _emergencyStopped = emergency;
            channel = _channel;
            cancellation = _sessionCancellation;
            channel?.Writer.TryComplete();
        }

        cancellation?.Cancel();
        if (channel is not null)
        {
            while (channel.Reader.TryRead(out var pending))
            {
                pending.Completion.TrySetCanceled();
            }
        }
    }

    private async Task ProcessAsync(
        ChannelReader<AutomationWorkItem> reader,
        CancellationToken sessionToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(sessionToken).ConfigureAwait(false))
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    sessionToken,
                    item.CancellationToken);
                try
                {
                    var result = await _executor.ExecuteAsync(
                        item.Action,
                        item.Capture,
                        linkedCancellation.Token).ConfigureAwait(false);
                    item.Completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    item.Completion.TrySetCanceled(linkedCancellation.Token);
                }
                catch (Exception exception)
                {
                    item.Completion.TrySetException(exception);
                }
            }
        }
        catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
        {
        }
        finally
        {
            while (reader.TryRead(out var pending))
            {
                pending.Completion.TrySetCanceled(sessionToken);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        EmergencyStopForDispose();
    }

    private void EmergencyStopForDispose()
    {
        Channel<AutomationWorkItem>? channel;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _emergencyStopped = true;
            _sessionStopped = true;
            channel = _channel;
            cancellation = _sessionCancellation;
            channel?.Writer.TryComplete();
        }

        cancellation?.Cancel();
        if (channel is not null)
        {
            while (channel.Reader.TryRead(out var pending))
            {
                pending.Completion.TrySetCanceled();
            }
        }

        cancellation?.Dispose();
    }

    private sealed record AutomationWorkItem(
        DesktopAction Action,
        ScreenCapture Capture,
        TaskCompletionSource<DesktopActionResult> Completion,
        CancellationToken CancellationToken);
}
