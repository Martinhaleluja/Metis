namespace Metis.Core.Models;

/// <summary>
/// The moments Metis marks with a sound. Each one is a distinct thing the user
/// needs to know happened without looking away from their work: that it heard
/// them, that it is working, that it finished, or that it stopped.
/// </summary>
public enum MetisSound
{
    /// <summary>Metis finished starting and is listening for its shortcuts.</summary>
    AppStarted,

    /// <summary>The microphone opened for an ordinary request.</summary>
    RecordingStarted,

    /// <summary>The inspect chord went down, so the pointer target matters.</summary>
    InspectPressed,

    /// <summary>The inspect chord came up.</summary>
    InspectReleased,

    /// <summary>The request left for the reasoning provider.</summary>
    RequestSent,

    /// <summary>A turn finished successfully.</summary>
    TaskComplete,

    /// <summary>Setup was saved.</summary>
    SettingsSaved,

    /// <summary>The emergency stop fired or the turn was cancelled.</summary>
    Stopped,

    /// <summary>Something failed. Several variants may exist for this one.</summary>
    Error,

    /// <summary>
    /// The plan on this account changed \u2014 an upgrade took effect, or a
    /// subscription ended.
    ///
    /// Worth a sound because it is the one change that happens *to* the user
    /// rather than because of something they just did in Metis: the webhook
    /// arrives while they are working in another window, and the notch quietly
    /// starts allowing something it did not a moment ago.
    /// </summary>
    PlanChanged,

    /// <summary>
    /// This month's included AI ran out, or the plan does not cover what was
    /// asked for.
    ///
    /// Deliberately not the error cue. Nothing went wrong \u2014 an allowance was
    /// spent, which is an ordinary and expected thing \u2014 and an error tone would
    /// send people looking for a fault to fix.
    /// </summary>
    LimitReached
}
