namespace Metis.App.Windows;

/// <summary>
/// What the companion is doing with its position. Only one of these can be
/// true at a time, which is the point: the previous arrangement of independent
/// flags could describe a companion that was simultaneously following the
/// cursor and holding at a mark, and the resolution depended on the order the
/// checks happened to be written in.
/// </summary>
public enum CompanionMotion
{
    /// <summary>Trailing the user's pointer, which is the resting behaviour.</summary>
    FollowingCursor,

    /// <summary>
    /// Crossing the screen toward something it has been asked to point out.
    /// Uninterruptible: the user asked to be shown where something is, and a
    /// companion that abandons the trip halfway has answered nothing.
    /// </summary>
    FlyingToTarget,

    /// <summary>
    /// Arrived, and holding beside the control with its label up.
    /// </summary>
    PointingAtTarget,

    /// <summary>
    /// On its way home to the pointer. This is the one leg the user may
    /// interrupt, because by now they have seen what they asked to see and
    /// reaching for the mouse means they want to get on with it.
    /// </summary>
    ReturningToCursor
}
