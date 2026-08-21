using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// How long a diagram stage stays up before the lesson moves on.
///
/// <see cref="AnnotationDuration"/> sizes its hold by how much of the screen
/// the target covers, which is the right question for a mark over a real
/// control — a small button is read at a glance. It is a meaningless question
/// for an invented triangle, which has no real size at all: the drawing is only
/// on screen for as long as it takes to say the sentence that goes with it.
///
/// So this measures the narration instead. The floor covers the moment the
/// shape spends drawing itself, and the ceiling stops one long-winded stage
/// from stalling the whole walkthrough.
/// </summary>
public static class DiagramStepDuration
{
    /// <summary>
    /// A little under conversational pace. Speech synthesis runs close to this,
    /// and a diagram wants a beat of quiet at the end rather than the next
    /// shape arriving on the final syllable.
    /// </summary>
    public const double WordsPerSecond = 2.6;

    /// <summary>
    /// The shortest a stage can be. The stroke animation alone takes a moment,
    /// so anything less would move on before the shape had finished appearing.
    /// </summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(3);

    /// <summary>The longest, so one wordy stage cannot stall the lesson.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromSeconds(14);

    public static TimeSpan For(LessonStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return For($"{step.Instruction} {step.Why}");
    }

    public static TimeSpan For(string? narration)
    {
        var words = CountWords(narration);
        if (words == 0)
        {
            return Minimum;
        }

        var seconds = words / WordsPerSecond;
        if (seconds <= Minimum.TotalSeconds)
        {
            return Minimum;
        }

        return seconds >= Maximum.TotalSeconds ? Maximum : TimeSpan.FromSeconds(seconds);
    }

    private static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}
