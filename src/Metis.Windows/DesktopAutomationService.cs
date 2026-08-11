using System.ComponentModel;
using System.Runtime.CompilerServices;
using Metis.Core.Contracts;
using Metis.Core.Models;

[assembly: InternalsVisibleTo("Metis.Tests")]

namespace Metis.Windows;

/// <summary>
/// Executes bounded desktop actions. UI Automation is preferred, background
/// messages preserve the user's pointer, and full-control mode falls back to
/// Windows physical input for surfaces such as the taskbar that reject both.
/// </summary>
public sealed class DesktopAutomationService : IDesktopAutomationService
{
    internal const int MaximumDelayMilliseconds = 30_000;

    private readonly IBackgroundDesktopInput _backgroundInput;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IUiAutomationService? _uiAutomation;
    private readonly IPhysicalDesktopInput _physicalInput;

    public DesktopAutomationService()
        : this(
            new NativeBackgroundDesktopInput(),
            static (duration, token) => Task.Delay(duration, token),
            new FlaUiAutomationService(),
            new NativePhysicalDesktopInput())
    {
        FullControlEnabled = true;
    }

    internal DesktopAutomationService(
        IBackgroundDesktopInput backgroundInput,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IUiAutomationService? uiAutomation = null,
        IPhysicalDesktopInput? physicalInput = null)
    {
        _backgroundInput = backgroundInput ?? throw new ArgumentNullException(nameof(backgroundInput));
        _delay = delay ?? (static (duration, token) => Task.Delay(duration, token));
        _uiAutomation = uiAutomation;
        _physicalInput = physicalInput ?? new NativePhysicalDesktopInput();
    }

    public bool FullControlEnabled { get; set; }

    public bool TryResolveTarget(
        DesktopAction action,
        ScreenCapture capture,
        out int screenX,
        out int screenY,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(capture);
        if (action.Kind == DesktopActionKind.Wait)
        {
            screenX = 0;
            screenY = 0;
            error = "Wait actions do not have a screen target.";
            return false;
        }

        if (!action.HasCoordinates)
        {
            screenX = 0;
            screenY = 0;
            error = "This action has no coordinate target for Metis's companion.";
            return false;
        }

        return TryMapCoordinates(action, capture, out screenX, out screenY, out error);
    }

    public async Task<DesktopActionResult> ExecuteAsync(
        DesktopAction action,
        ScreenCapture capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();

        var requestedDelay = Math.Max(0, action.DelayMilliseconds);
        var effectiveDelay = Math.Min(requestedDelay, MaximumDelayMilliseconds);
        if (effectiveDelay > 0)
        {
            await _delay(TimeSpan.FromMilliseconds(effectiveDelay), cancellationToken).ConfigureAwait(false);
        }

        if (action.Kind == DesktopActionKind.Wait)
        {
            var limited = requestedDelay > MaximumDelayMilliseconds
                ? $" (limited from {requestedDelay} ms)"
                : string.Empty;
            return new DesktopActionResult(action, true, $"Waited {effectiveDelay} ms{limited}.");
        }

        if (action.Kind is DesktopActionKind.TypeText or DesktopActionKind.KeyPress or
            DesktopActionKind.OpenApp or DesktopActionKind.OpenUrl)
        {
            return ExecuteKeyboardOrLaunchAction(action);
        }

        if (!TryResolveTarget(action, capture, out var screenX, out var screenY, out var mappingError))
        {
            return new DesktopActionResult(action, false, mappingError);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action.Kind == DesktopActionKind.MovePointer)
            {
                var physicalHoverError = 0;
                if (FullControlEnabled && _physicalInput.TryMoveAt(screenX, screenY, out physicalHoverError))
                {
                    return new DesktopActionResult(
                        action,
                        true,
                        FormatSuccess(action, "Moved Metis with full Windows hover control", screenX, screenY),
                        screenX,
                        screenY);
                }

                var hoverSent = _backgroundInput.TryHoverAt(screenX, screenY, out var hoverError);
                var message = hoverSent
                    ? FormatSuccess(action, "Moved Metis and sent a cursorless hover", screenX, screenY)
                    : FullControlEnabled
                        ? $"Windows rejected both full-control and cursorless hover at ({screenX}, {screenY}). " +
                          $"Full control: {FormatWindowsError(physicalHoverError)} Cursorless: {FormatWindowsError(hoverError)}"
                        : $"Moved Metis to ({screenX}, {screenY}), but the application rejected cursorless hover messages " +
                          $"({FormatWindowsError(hoverError)}).";
                return new DesktopActionResult(action, hoverSent, message, screenX, screenY);
            }

            if (_uiAutomation is not null &&
                action.Kind == DesktopActionKind.LeftClick)
            {
                UiAutomationResult automationResult;
                if (capture.WindowHandle != 0 && !string.IsNullOrWhiteSpace(action.AutomationId))
                {
                    automationResult = await _uiAutomation.TryInvokeAsync(
                        action.AutomationId,
                        capture,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    automationResult = await _uiAutomation.TryInvokeAtAsync(
                        screenX,
                        screenY,
                        cancellationToken).ConfigureAwait(false);
                }

                if (automationResult.Success)
                {
                    return new DesktopActionResult(
                        action,
                        true,
                        automationResult.Message,
                        screenX,
                        screenY);
                }
            }

            var physicalInputError = 0;
            if (FullControlEnabled &&
                _physicalInput.TryClickAt(action.Kind, screenX, screenY, out physicalInputError))
            {
                var fullControlVerb = action.Kind switch
                {
                    DesktopActionKind.LeftClick => "Clicked with full Windows control",
                    DesktopActionKind.DoubleClick => "Double-clicked with full Windows control",
                    DesktopActionKind.RightClick => "Right-clicked with full Windows control",
                    _ => "Activated with full Windows control"
                };
                return new DesktopActionResult(
                    action,
                    true,
                    FormatSuccess(action, fullControlVerb, screenX, screenY),
                    screenX,
                    screenY);
            }

            if (!_backgroundInput.TryClickAt(action.Kind, screenX, screenY, out var inputError))
            {
                return new DesktopActionResult(
                    action,
                    false,
                    FullControlEnabled
                        ? $"Windows rejected Metis's full-control and cursorless {Describe(action.Kind)} at ({screenX}, {screenY}). " +
                          $"Full control: {FormatWindowsError(physicalInputError)} Cursorless: {FormatWindowsError(inputError)}"
                        : $"The application at ({screenX}, {screenY}) rejected Metis's cursorless {Describe(action.Kind)}. " +
                          $"The Windows pointer was not moved. {FormatWindowsError(inputError)}",
                    screenX,
                    screenY);
            }

            var verb = action.Kind switch
            {
                DesktopActionKind.LeftClick => "Clicked without moving the Windows pointer",
                DesktopActionKind.DoubleClick => "Double-clicked without moving the Windows pointer",
                DesktopActionKind.RightClick => "Right-clicked without moving the Windows pointer",
                _ => "Activated"
            };
            return new DesktopActionResult(
                action,
                true,
                FormatSuccess(action, verb, screenX, screenY),
                screenX,
                screenY);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DesktopActionResult(
                action,
                false,
                $"Windows could not perform cursorless {Describe(action.Kind)}: {exception.Message}",
                screenX,
                screenY);
        }
    }

    private DesktopActionResult ExecuteKeyboardOrLaunchAction(DesktopAction action)
    {
        if (!FullControlEnabled)
        {
            return new DesktopActionResult(
                action,
                false,
                "Typing and navigation require Full desktop control in Metis Setup.");
        }

        var success = action.Kind switch
        {
            DesktopActionKind.TypeText =>
                _physicalInput.TryTypeText(action.Text ?? string.Empty, out _),
            DesktopActionKind.KeyPress =>
                _physicalInput.TryPressKey(action.Key ?? string.Empty, out _),
            DesktopActionKind.OpenApp =>
                _physicalInput.TryOpenApp(action.Text ?? string.Empty, out _),
            DesktopActionKind.OpenUrl =>
                _physicalInput.TryOpenUrl(action.Text ?? string.Empty, out _),
            _ => false
        };

        if (!success)
        {
            return new DesktopActionResult(
                action,
                false,
                action.Kind switch
                {
                    DesktopActionKind.TypeText => "Windows rejected Metis's generated typing input.",
                    DesktopActionKind.KeyPress => $"Windows rejected the key command '{action.Key ?? "unknown"}'.",
                    DesktopActionKind.OpenApp => $"Windows could not open '{ShortLabel(action.Text)}'.",
                    DesktopActionKind.OpenUrl => "Windows rejected the URL or could not open the default browser.",
                    _ => "Windows rejected the navigation command."
                });
        }

        return new DesktopActionResult(
            action,
            true,
            action.Kind switch
            {
                DesktopActionKind.TypeText => $"Typed {action.Text?.Length ?? 0} character(s).",
                DesktopActionKind.KeyPress => $"Pressed {action.Key}.",
                DesktopActionKind.OpenApp => $"Opened {ShortLabel(action.Text)} through Windows Search.",
                DesktopActionKind.OpenUrl => "Opened the requested web address in the default browser.",
                _ => "Completed the navigation command."
            });
    }

    private static string ShortLabel(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "the requested target" : value.Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80] + "…";
    }

    internal static bool TryMapCoordinates(
        DesktopAction action,
        ScreenCapture capture,
        out int screenX,
        out int screenY,
        out string error)
    {
        var sourceWidth = capture.SourceWidth > 0 ? capture.SourceWidth : capture.Width;
        var sourceHeight = capture.SourceHeight > 0 ? capture.SourceHeight : capture.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            screenX = 0;
            screenY = 0;
            error = "The captured desktop has invalid bounds, so Metis cannot safely position the companion.";
            return false;
        }

        var normalizedX = Math.Clamp(action.NormalizedX, 0, 1000);
        var normalizedY = Math.Clamp(action.NormalizedY, 0, 1000);
        var xOffset = (int)Math.Round(
            normalizedX / 1000d * (sourceWidth - 1),
            MidpointRounding.AwayFromZero);
        var yOffset = (int)Math.Round(
            normalizedY / 1000d * (sourceHeight - 1),
            MidpointRounding.AwayFromZero);

        var mappedX = (long)capture.ScreenLeft + xOffset;
        var mappedY = (long)capture.ScreenTop + yOffset;
        if (mappedX is < int.MinValue or > int.MaxValue || mappedY is < int.MinValue or > int.MaxValue)
        {
            screenX = 0;
            screenY = 0;
            error = "The captured desktop bounds are outside supported Windows coordinates.";
            return false;
        }

        screenX = (int)mappedX;
        screenY = (int)mappedY;
        error = string.Empty;
        return true;
    }

    private static string FormatSuccess(DesktopAction action, string verb, int x, int y)
    {
        var target = string.IsNullOrWhiteSpace(action.Label) ? string.Empty : $" '{action.Label.Trim()}'";
        return $"{verb}{target} at ({x}, {y}).";
    }

    private static string FormatWindowsError(int error)
    {
        var detail = error > 0 ? new Win32Exception(error).Message : "Windows rejected the message.";
        return $"{detail} (error {error}).";
    }

    private static string Describe(DesktopActionKind kind) => kind switch
    {
        DesktopActionKind.LeftClick => "left click",
        DesktopActionKind.DoubleClick => "double click",
        DesktopActionKind.RightClick => "right click",
        DesktopActionKind.TypeText => "typing",
        DesktopActionKind.KeyPress => "key press",
        DesktopActionKind.OpenApp => "open app",
        DesktopActionKind.OpenUrl => "open URL",
        DesktopActionKind.MovePointer => "hover",
        DesktopActionKind.Wait => "wait",
        _ => "desktop action"
    };
}
