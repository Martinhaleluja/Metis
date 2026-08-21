namespace Metis.Core.Models;

/// <summary>
/// Versioning for the welcome, so a materially changed onboarding is shown
/// again rather than only ever appearing on a fresh install.
/// </summary>
public static class OnboardingVersions
{
    /// <summary>
    /// Raised whenever onboarding teaches something new or corrects something
    /// it used to say.
    ///
    /// 1  the original welcome
    /// 2  two modes instead of four, Ctrl+Space listening with a wake word,
    ///    working in the background, and choosing a model per conversation
    /// 3  no modes at all. Metis teaches and never operates the computer, so
    ///    everyone who was shown the old welcome was told something that is no
    ///    longer true and needs telling again.
    /// </summary>
    public const int Current = 3;

    /// <summary>
    /// Whether this user should be shown the welcome. True on a fresh install,
    /// and true again for anyone who last saw an older version of it.
    /// </summary>
    public static bool ShouldShow(bool completed, int seenVersion) =>
        !completed || seenVersion < Current;
}
