namespace Metis.Core.Services;

/// <summary>
/// One companion form: the silhouette it is drawn from, whether it carries an
/// outline, and where its status indicators belong.
/// </summary>
/// <param name="Name">Stored in settings and shown in the picker.</param>
/// <param name="Description">One line explaining what the form is for.</param>
/// <param name="Geometry">
/// Path data in a 72x72 design box. Rendered with uniform stretch, so a
/// silhouette does not have to fill the box — it is fitted and centred.
/// </param>
/// <param name="Outlined">
/// Whether the form takes a dark outline. The cursor forms do: they sit over
/// whatever the user happens to have on screen, and a pale fill alone
/// disappears against a pale window. The blob does not, because it is the
/// established Metis mark and its glow already separates it.
/// </param>
/// <param name="IndicatorX">
/// Horizontal offset for the speaking bars and thinking ring, in rendered
/// units from the centre of the box.
/// </param>
/// <param name="IndicatorY">
/// Vertical offset for the same. A hand's solid area is its fist, well below
/// the middle of its bounding box, and an indicator left at the centre would
/// hang off the finger.
/// </param>
/// <param name="IndicatorScale">
/// Size of those indicators relative to the blob's. Narrow forms get smaller
/// ones so they still sit inside the silhouette.
/// </param>
public sealed record CompanionShapeOption(
    string Name,
    string Description,
    string Geometry,
    bool Outlined,
    double IndicatorX,
    double IndicatorY,
    double IndicatorScale);

/// <summary>
/// The forms a user can give their companion. Defined once here, in the same
/// shape as <see cref="CompanionPalette"/>, so the picker and the companion
/// itself cannot disagree about what exists.
///
/// A form only changes the silhouette. Colour, glow, the resting drift, the
/// speaking and thinking states, the error flash and the flight across the
/// screen are all driven by the same code whichever form is chosen — the
/// companion is one object wearing a different outline, not two companions.
/// </summary>
public static class CompanionShapes
{
    // A pointer by default: Metis teaches by pointing things out, and a cursor
    // reads as a second hand on the screen guiding you, the way a person would.
    // The blob is still there for anyone who prefers it.
    public const string DefaultName = "Cursor";

    public static IReadOnlyList<CompanionShapeOption> All { get; } =
    [
        new(
            "Blob",
            "The original Metis mark.",
            "M34,8 C42,-1 56,-1 64,7 C72,15 72,27 66,35 C72,43 72,55 64,63 " +
            "C56,71 44,71 35,65 C27,71 15,71 7,63 C-1,55 -1,43 5,35 " +
            "C-1,27 -1,15 7,7 C15,-1 27,-1 34,8 Z",
            Outlined: false,
            IndicatorX: 0,
            IndicatorY: 0,
            IndicatorScale: 1.0),

        new(
            "Cursor",
            "A classic pointer, for following along like a second cursor.",
            "M 14,4 L 14,56 L 27,43 L 35,62 L 45,58 L 37,39 L 54,39 Z",
            Outlined: true,
            IndicatorX: -9,
            IndicatorY: -8,
            IndicatorScale: 0.72)
    ];

    /// <summary>
    /// Resolves a stored name to its form, falling back to the default rather
    /// than throwing: a settings file naming a form that no longer exists
    /// should leave the companion visible, not break startup.
    /// </summary>
    public static CompanionShapeOption Resolve(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return All.FirstOrDefault(option =>
                   string.Equals(option.Name, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? All.First(option => option.Name == DefaultName);
    }

    public static string Normalize(string? name) => Resolve(name).Name;
}
