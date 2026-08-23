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

        // Either kind of evidence will do. A step that names its control but
        // omits coordinates used to be discarded, so every such step fell back
        // to one remembered spot and the whole lesson marked the same place.
        return (step.HasTarget || step.HasNamedTarget) && !step.HasDiagram;
    }

    /// <summary>
    /// Whether a step should be drawn as a shape on a blank canvas instead of
    /// marked on the real screen.
    /// </summary>
    /// <remarks>
    /// Drawing on a canvas means abandoning the screen, so it has to be the
    /// narrow case. Three things must all hold: the lesson was asked for as a
    /// subject to illustrate, the step actually describes a shape, and the step
    /// is not also pointing at something real — because a real target is the
    /// answer the user asked for, and an invented shape drawn over their work
    /// is not.
    ///
    /// Without the first condition a stray shape field on an otherwise ordinary
    /// answer replaces a mark on the user's screen with a triangle floating in
    /// the middle of it.
    /// </remarks>
    public static bool ShouldDrawOnCanvas(LessonStep step, bool illustratingASubject)
    {
        ArgumentNullException.ThrowIfNull(step);
        return illustratingASubject && step.HasDiagram && !step.HasTarget;
    }

    /// <summary>
    /// Whether a request should be answered by drawing a subject rather than by
    /// marking the screen. A request that mentions the screen has its answer on
    /// the screen, whatever subject its words happen to touch.
    /// </summary>
    public static bool ShouldIllustrateSubject(bool subjectMatchedFromRequest, bool requestAsksAboutScreen) =>
        subjectMatchedFromRequest && !requestAsksAboutScreen;
}
