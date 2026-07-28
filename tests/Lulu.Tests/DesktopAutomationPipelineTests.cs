using Lulu.Core.Contracts;
using Lulu.Core.Models;
using Lulu.Windows;

namespace Lulu.Tests;

public sealed class DesktopAutomationPipelineTests
{
    [Fact]
    public async Task Pipeline_executes_actions_in_order_with_one_reader()
    {
        var executor = new SequencedExecutor();
        using var pipeline = new DesktopAutomationPipeline(executor, capacity: 4);
        pipeline.StartSession();
        var capture = Capture();

        var first = pipeline.EnqueueAsync(new DesktopAction(DesktopActionKind.LeftClick, Label: "first"), capture);
        await executor.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = pipeline.EnqueueAsync(new DesktopAction(DesktopActionKind.LeftClick, Label: "second"), capture);

        Assert.Equal(["first"], executor.StartedLabels);
        executor.ReleaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["first", "second"], executor.StartedLabels);
        Assert.Equal(1, executor.MaximumConcurrency);
    }

    [Fact]
    public async Task Emergency_stop_cancels_active_and_queued_actions()
    {
        var executor = new BlockingExecutor();
        using var pipeline = new DesktopAutomationPipeline(executor, capacity: 4);
        pipeline.StartSession();
        var capture = Capture();

        var active = pipeline.EnqueueAsync(new DesktopAction(DesktopActionKind.LeftClick), capture);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = pipeline.EnqueueAsync(new DesktopAction(DesktopActionKind.RightClick), capture);

        pipeline.EmergencyStop();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.True(pipeline.IsEmergencyStopped);
    }

    private static ScreenCapture Capture() => new([], "Test", 100, 100, SourceWidth: 100, SourceHeight: 100);

    private sealed class SequencedExecutor : IDesktopAutomationService
    {
        public bool FullControlEnabled { get; set; }

        private int _concurrency;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> StartedLabels { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public bool TryResolveTarget(
            DesktopAction action,
            ScreenCapture capture,
            out int screenX,
            out int screenY,
            out string error)
        {
            screenX = 0;
            screenY = 0;
            error = string.Empty;
            return true;
        }

        public async Task<DesktopActionResult> ExecuteAsync(
            DesktopAction action,
            ScreenCapture capture,
            CancellationToken cancellationToken = default)
        {
            var concurrency = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            StartedLabels.Add(action.Label ?? string.Empty);
            try
            {
                if (action.Label == "first")
                {
                    FirstStarted.TrySetResult();
                    await ReleaseFirst.Task.WaitAsync(cancellationToken);
                }

                return new DesktopActionResult(action, true, "done");
            }
            finally
            {
                Interlocked.Decrement(ref _concurrency);
            }
        }
    }

    private sealed class BlockingExecutor : IDesktopAutomationService
    {
        public bool FullControlEnabled { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryResolveTarget(
            DesktopAction action,
            ScreenCapture capture,
            out int screenX,
            out int screenY,
            out string error)
        {
            screenX = 0;
            screenY = 0;
            error = string.Empty;
            return true;
        }

        public async Task<DesktopActionResult> ExecuteAsync(
            DesktopAction action,
            ScreenCapture capture,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new DesktopActionResult(action, true, "unreachable");
        }
    }
}
