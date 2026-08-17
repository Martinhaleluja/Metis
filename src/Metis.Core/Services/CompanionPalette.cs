namespace Metis.Core.Services;

/// <summary>
/// One companion colour: the body fill and the glow that surrounds it. The glow
/// is a lighter, more saturated relative of the fill rather than a second hue,
/// so the companion reads as one lit object instead of two stacked shapes.
/// </summary>
public sealed record CompanionColorOption(string Name, string Fill, string Glow);

/// <summary>
/// The colours a user can give their companion. Defined once here so the Setup
/// picker and the companion itself cannot drift apart. The resting colour is a
/// user choice, while the state colours (listening, thinking, error) keep their
/// fixed meanings — those carry information and are not decoration.
/// </summary>
public static class CompanionPalette
{
    public const string DefaultName = "Sky";

    public static IReadOnlyList<CompanionColorOption> All { get; } =
    [
        new("Sky", "#8ED8FF", "#56CCFF"),
        new("Ocean", "#4C8DFF", "#2F6BFF"),
        new("Violet", "#B18CFF", "#8A5CFF"),
        new("Orchid", "#E58CE0", "#D45FCE"),
        new("Rose", "#FF9AB5", "#FF6F97"),
        new("Ember", "#FF9A6B", "#FF6F3D"),
        new("Amber", "#FFCE6B", "#FFB020"),
        new("Mint", "#7BE8B8", "#3FD69A"),
        new("Lime", "#C3E88D", "#A4D65E"),
        new("Graphite", "#B9C2CC", "#8C97A3")
    ];

    /// <summary>
    /// Resolves a stored name to its option, falling back to the default rather
    /// than throwing: a settings file naming a colour that no longer exists
    /// should leave the companion visible, not break startup.
    /// </summary>
    public static CompanionColorOption Resolve(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return All.FirstOrDefault(option =>
                   string.Equals(option.Name, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? All.First(option => option.Name == DefaultName);
    }

    public static string Normalize(string? name) => Resolve(name).Name;
}
