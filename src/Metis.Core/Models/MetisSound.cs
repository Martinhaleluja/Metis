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
    Error
}
