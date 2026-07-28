using Lulu.Core.Contracts;
using Lulu.Core.Models;
using Lulu.Windows;

namespace Lulu.Tests;

public sealed class DesktopAutomationServiceTests
{
    [Theory]
    [InlineData(-50, -200, -400, 50)]
    [InlineData(500, 500, -300, 100)]
    [InlineData(1200, 4000, -201, 149)]
    public void TryMapCoordinates_ClampsToCapturedDesktop(
        int normalizedX,
        int normalizedY,
        int expectedX,
        int expectedY)
    {
        var capture = Capture(left: -400, top: 50, width: 200, height: 100);
        var action = new DesktopAction(DesktopActionKind.MovePointer, normalizedX, normalizedY);

        var success = DesktopAutomationService.TryMapCoordinates(
            action,
            capture,
            out var x,
            out var y,
            out var error);

        Assert.True(success, error);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public async Task ExecuteAsync_MovePointer_SendsCursorlessHoverOnly()
    {
        var input = new FakeBackgroundInput();
        var service = new DesktopAutomationService(input, (_, _) => Task.CompletedTask);

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.MovePointer, 1000, 0, Label: "Save"),
            Capture(100, 200, 301, 201));

        Assert.True(result.Success, result.Message);
        Assert.Equal((400, 200), Assert.Single(input.Hovers));
        Assert.Empty(input.Clicks);
        Assert.Contains("cursorless hover", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_LeftClick_UsesCoordinateUiAutomationWithoutPointerInput()
    {
        var input = new FakeBackgroundInput();
        var uiAutomation = new FakeUiAutomationService
        {
            PointResult = new UiAutomationResult(true, "Invoked without moving pointer")
        };
        var service = new DesktopAutomationService(input, (_, _) => Task.CompletedTask, uiAutomation);

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.LeftClick, 500, 500),
            Capture(100, 200, 100, 100));

        Assert.True(result.Success, result.Message);
        Assert.Equal((150, 250), uiAutomation.LastPoint);
        Assert.Empty(input.Hovers);
        Assert.Empty(input.Clicks);
    }

    [Fact]
    public async Task ExecuteAsync_ClickFallsBackToBackgroundMessageWithoutMovingPointer()
    {
        var input = new FakeBackgroundInput();
        var uiAutomation = new FakeUiAutomationService
        {
            PointResult = new UiAutomationResult(false, "No UIA pattern")
        };
        var service = new DesktopAutomationService(input, (_, _) => Task.CompletedTask, uiAutomation);

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.DoubleClick, 500, 500),
            Capture(100, 200, 100, 100));

        Assert.True(result.Success, result.Message);
        var click = Assert.Single(input.Clicks);
        Assert.Equal(DesktopActionKind.DoubleClick, click.Kind);
        Assert.Equal(150, click.X);
        Assert.Equal(250, click.Y);
        Assert.Contains("without moving the Windows pointer", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_UsesAutomationIdForSingleWindowCapture()
    {
        var input = new FakeBackgroundInput();
        var uiAutomation = new FakeUiAutomationService
        {
            IdResult = new UiAutomationResult(true, "Invoked SaveButton", 320, 240)
        };
        var service = new DesktopAutomationService(input, (_, _) => Task.CompletedTask, uiAutomation);
        var action = new DesktopAction(
            DesktopActionKind.LeftClick,
            500,
            500,
            AutomationId: "SaveButton");

        var result = await service.ExecuteAsync(action, Capture(0, 0, 640, 480, windowHandle: 42));

        Assert.True(result.Success, result.Message);
        Assert.Equal("SaveButton", uiAutomation.LastAutomationId);
        Assert.Null(uiAutomation.LastPoint);
        Assert.Empty(input.Clicks);
    }

    [Fact]
    public async Task ExecuteAsync_FullControlUsesPhysicalFallbackWhenUiAutomationCannotInvoke()
    {
        var backgroundInput = new FakeBackgroundInput { ClickResult = false, ClickError = 5 };
        var physicalInput = new FakePhysicalInput();
        var uiAutomation = new FakeUiAutomationService
        {
            PointResult = new UiAutomationResult(false, "No UIA pattern")
        };
        var service = new DesktopAutomationService(
            backgroundInput,
            (_, _) => Task.CompletedTask,
            uiAutomation,
            physicalInput)
        {
            FullControlEnabled = true
        };

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.LeftClick, 500, 500, Label: "Start"),
            Capture(0, 0, 1000, 1000));

        Assert.True(result.Success, result.Message);
        Assert.Equal((DesktopActionKind.LeftClick, 500, 500), Assert.Single(physicalInput.Clicks));
        Assert.Empty(backgroundInput.Clicks);
        Assert.Contains("full Windows control", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_FullControlMovesPhysicalPointerForNativeHover()
    {
        var physicalInput = new FakePhysicalInput();
        var service = new DesktopAutomationService(
            new FakeBackgroundInput(),
            (_, _) => Task.CompletedTask,
            physicalInput: physicalInput)
        {
            FullControlEnabled = true
        };

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.MovePointer, 250, 750),
            Capture(0, 0, 1000, 1000));

        Assert.True(result.Success, result.Message);
        Assert.Equal((250, 749), Assert.Single(physicalInput.Moves));
    }

    [Fact]
    public async Task ExecuteAsync_TypesAndPressesKeysWithoutCoordinateMapping()
    {
        var physicalInput = new FakePhysicalInput();
        var service = new DesktopAutomationService(
            new FakeBackgroundInput(),
            (_, _) => Task.CompletedTask,
            physicalInput: physicalInput)
        {
            FullControlEnabled = true
        };

        var typed = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.TypeText, HasCoordinates: false, Text: "Hello Martin"),
            Capture(0, 0, 100, 100));
        var pressed = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.KeyPress, HasCoordinates: false, Key: "ctrl+l"),
            Capture(0, 0, 100, 100));

        Assert.True(typed.Success, typed.Message);
        Assert.True(pressed.Success, pressed.Message);
        Assert.Equal("Hello Martin", Assert.Single(physicalInput.TypedText));
        Assert.Equal("ctrl+l", Assert.Single(physicalInput.PressedKeys));
    }

    [Fact]
    public async Task ExecuteAsync_Wait_CapsGeneratedDelayWithoutInput()
    {
        var input = new FakeBackgroundInput();
        TimeSpan? observedDelay = null;
        var service = new DesktopAutomationService(input, (delay, _) =>
        {
            observedDelay = delay;
            return Task.CompletedTask;
        });

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.Wait, DelayMilliseconds: 90_000),
            Capture(0, 0, 100, 100));

        Assert.True(result.Success, result.Message);
        Assert.Equal(TimeSpan.FromMilliseconds(DesktopAutomationService.MaximumDelayMilliseconds), observedDelay);
        Assert.Empty(input.Hovers);
        Assert.Empty(input.Clicks);
        Assert.Contains("limited", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_DoesNotSendBackgroundInput()
    {
        var input = new FakeBackgroundInput();
        var service = new DesktopAutomationService(input, (_, token) => Task.Delay(Timeout.Infinite, token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.LeftClick, 500, 500, DelayMilliseconds: 100),
            Capture(0, 0, 100, 100),
            cancellation.Token));

        Assert.Empty(input.Hovers);
        Assert.Empty(input.Clicks);
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundFailure_DoesNotFallBackToPhysicalCursor()
    {
        var input = new FakeBackgroundInput { ClickResult = false, ClickError = 5 };
        var service = new DesktopAutomationService(input, (_, _) => Task.CompletedTask);

        var result = await service.ExecuteAsync(
            new DesktopAction(DesktopActionKind.RightClick, 1000, 1000),
            Capture(0, 0, 100, 100));

        Assert.False(result.Success);
        Assert.Contains("Windows pointer was not moved", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("error 5", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ScreenCapture Capture(
        int left,
        int top,
        int width,
        int height,
        long windowHandle = 0) =>
        new([], "Test desktop", width, height, left, top, width, height, windowHandle);

    private sealed class FakeBackgroundInput : IBackgroundDesktopInput
    {
        public List<(int X, int Y)> Hovers { get; } = [];
        public List<(DesktopActionKind Kind, int X, int Y)> Clicks { get; } = [];
        public bool HoverResult { get; init; } = true;
        public bool ClickResult { get; init; } = true;
        public int HoverError { get; init; }
        public int ClickError { get; init; }

        public bool TryHoverAt(int screenX, int screenY, out int error)
        {
            Hovers.Add((screenX, screenY));
            error = HoverError;
            return HoverResult;
        }

        public bool TryClickAt(DesktopActionKind kind, int screenX, int screenY, out int error)
        {
            Clicks.Add((kind, screenX, screenY));
            error = ClickError;
            return ClickResult;
        }
    }

    private sealed class FakeUiAutomationService : IUiAutomationService
    {
        public UiAutomationResult IdResult { get; init; } = new(false, "No ID result");
        public UiAutomationResult PointResult { get; init; } = new(false, "No point result");
        public string? LastAutomationId { get; private set; }
        public (int X, int Y)? LastPoint { get; private set; }

        public Task<string?> DescribeWindowAsync(
            ScreenCapture capture,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<UiAutomationResult> TryInvokeAsync(
            string automationId,
            ScreenCapture capture,
            CancellationToken cancellationToken = default)
        {
            LastAutomationId = automationId;
            return Task.FromResult(IdResult);
        }

        public Task<UiAutomationResult> TryInvokeAtAsync(
            int screenX,
            int screenY,
            CancellationToken cancellationToken = default)
        {
            LastPoint = (screenX, screenY);
            return Task.FromResult(PointResult);
        }
    }

    private sealed class FakePhysicalInput : IPhysicalDesktopInput
    {
        public List<(int X, int Y)> Moves { get; } = [];
        public List<(DesktopActionKind Kind, int X, int Y)> Clicks { get; } = [];
        public List<string> TypedText { get; } = [];
        public List<string> PressedKeys { get; } = [];
        public bool MoveResult { get; init; } = true;
        public bool ClickResult { get; init; } = true;

        public bool TryMoveAt(int screenX, int screenY, out int error)
        {
            Moves.Add((screenX, screenY));
            error = MoveResult ? 0 : 5;
            return MoveResult;
        }

        public bool TryClickAt(DesktopActionKind kind, int screenX, int screenY, out int error)
        {
            Clicks.Add((kind, screenX, screenY));
            error = ClickResult ? 0 : 5;
            return ClickResult;
        }

        public bool TryTypeText(string text, out int error)
        {
            TypedText.Add(text);
            error = 0;
            return true;
        }

        public bool TryPressKey(string key, out int error)
        {
            PressedKeys.Add(key);
            error = 0;
            return true;
        }

        public bool TryOpenApp(string appName, out int error)
        {
            error = 0;
            return true;
        }

        public bool TryOpenUrl(string url, out int error)
        {
            error = 0;
            return true;
        }
    }
}
