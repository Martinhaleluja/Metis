using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// Which of the two teaching paths a lesson step belongs to.
///
/// This exists as a named rule rather than an inline condition so it can be
/// tested directly. The guarantee it carries is the one that keeps academic
/// lessons honest: a step that draws a diagram must never be handed to the
/// annotation resolver, because that resolver's job is to find something real
/// on the screen, and it will succeed at that job even when the coordinates it
/// was given describe an invented triangle — marking whatever control happens
/// to lie underneath. A wrong mark on the user's own window is worse than no
/// mark, and it is the failure this rule prevents.
/// </summary>
public static class LessonStepRouting
{
    /// <summary>
    /// True only for steps that point at something really on screen. A step
    /// carrying a diagram is excluded whatever else it carries, so a model that
    /// fills in both vocabularies at once still cannot reach the resolver.
    /// </summary>
    public static bool RequiresRealScreenAnnotation(LessonStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.HasTarget && !step.HasDiagram;
    }
}
