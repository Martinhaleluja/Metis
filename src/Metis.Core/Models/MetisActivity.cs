namespace Metis.Core.Models;

/// <summary>
/// What Metis is doing right now, phrased for the notch. This is separate from
/// <see cref="AssistantState"/>, which drives the companion's colour: the notch
/// needs the narrative ("running step 2 of 4") rather than the mood.
/// </summary>
public enum MetisActivityKind
{
    Idle,
    Listening,
    Capturing,
    Thinking,
    Acting,
    Verifying,
    Speaking,
    Complete,
    Error,
    Stopped
}

/// <summary>
/// One line of activity for the notch. <paramref name="StepNumber"/> and
/// <paramref name="StepCount"/> are set while a plan is running so the notch can
/// show progress through a multi-step task instead of a spinner.
/// </summary>
public sealed record MetisActivity(
    MetisActivityKind Kind,
    string Text,
    int StepNumber = 0,
    int StepCount = 0)
{
    public static MetisActivity Idle { get; } = new(MetisActivityKind.Idle, string.Empty);

    public bool HasSteps => StepCount > 0 && StepNumber > 0;
}
