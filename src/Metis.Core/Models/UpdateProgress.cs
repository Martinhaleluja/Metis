namespace Metis.Core.Models;

/// <summary>What an update is doing right now.</summary>
public enum UpdatePhase
{
    /// <summary>Asking GitHub what the newest release is.</summary>
    Checking,

    /// <summary>Fetching the installer.</summary>
    Downloading,

    /// <summary>
    /// Hashing what was fetched, before running it.
    ///
    /// Worth being its own phase rather than folded into the download. On a
    /// sixty-megabyte installer this takes well over a second, and it happens
    /// *after* the download bar would have reached the end — so without a phase
    /// of its own it looks exactly like the application hanging at 100%.
    /// </summary>
    Verifying,

    /// <summary>Handing off to the installer, which will close Metis.</summary>
    Starting,

    Failed
}

/// <summary>
/// How far along an update is, for a progress indicator to draw.
///
/// This exists because the download had no feedback of any kind. It used
/// <c>CopyToAsync</c>, which reports nothing, never read the content length, and
/// left the user looking at a dimmed button for however long the transfer took.
/// The interface rules this project follows require feedback for anything over
/// three hundred milliseconds; a sixty-megabyte download on a slow connection is
/// several minutes of a screen that is indistinguishable from a hang.
///
/// <paramref name="TotalBytes"/> is null when the server did not send a content
/// length. That is a real case and not an error: the indicator shows an
/// indeterminate state rather than a percentage it would have to invent.
/// </summary>
public sealed record UpdateProgress(
    UpdatePhase Phase,
    long BytesRead = 0,
    long? TotalBytes = null,
    string? Detail = null)
{
    /// <summary>
    /// How far along, 0 to 1, or null when that cannot be known.
    /// </summary>
    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesRead / TotalBytes.Value, 0, 1)
        : null;

    /// <summary>
    /// A line to show beside the bar, in the units a person reads.
    ///
    /// Megabytes to one decimal place rather than bytes: "23.4 MB of 61.8 MB"
    /// tells someone how long they are waiting, and "24,536,192 bytes" does not.
    /// </summary>
    public string Caption => Phase switch
    {
        UpdatePhase.Checking => "Looking for a newer version…",

        UpdatePhase.Downloading when TotalBytes is > 0 =>
            $"Downloading {Megabytes(BytesRead)} of {Megabytes(TotalBytes.Value)}",
        UpdatePhase.Downloading when BytesRead > 0 => $"Downloading {Megabytes(BytesRead)}",
        UpdatePhase.Downloading => "Starting the download…",

        UpdatePhase.Verifying => "Checking the download…",
        UpdatePhase.Starting => "Restarting Metis…",
        UpdatePhase.Failed => Detail ?? "The update could not be downloaded.",
        _ => string.Empty
    };

    private static string Megabytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " MB";
}
