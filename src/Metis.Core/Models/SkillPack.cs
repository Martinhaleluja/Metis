namespace Metis.Core.Models;

/// <summary>
/// A piece of knowledge the user has taught Metis about a particular piece of
/// software: where things live, what the jargon means, how a workflow goes.
///
/// This is how Metis becomes competent in software nobody anticipated. The
/// model already knows how to read a screen; what it lacks for an unfamiliar or
/// in-house application is the vocabulary and the conventions, and that is
/// exactly what a skill supplies.
/// </summary>
/// <summary>
/// What kind of knowledge a skill carries, and therefore how Metis should
/// teach from it.
///
/// This is what decides whether a lesson marks the user's real screen or draws
/// on a canvas of its own. Keeping it on the skill file rather than inferring
/// it from the request means a new subject is a new file, not a new branch in
/// a keyword list that has to be edited every time.
/// </summary>
public enum SkillDomain
{
    /// <summary>Knowledge about a program the user runs. The default.</summary>
    Software,

    /// <summary>
    /// A subject Metis explains rather than operates — maths, physics, biology.
    /// Lessons drawn from these describe shapes on a canvas instead of controls
    /// on screen.
    /// </summary>
    Academic
}

public static class SkillDomains
{
    public static SkillDomain Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "academic" or "subject" or "education" or "teaching" => SkillDomain.Academic,
        _ => SkillDomain.Software
    };
}

public sealed record SkillPack(
    string Name,
    string Description,
    string Content,
    IReadOnlyList<string> AppliesTo,
    string? SourcePath = null,

    /// <summary>
    /// What kind of knowledge this is. Defaults to software, so every skill
    /// written before this existed keeps behaving exactly as it did.
    /// </summary>
    SkillDomain Domain = SkillDomain.Software)
{
    /// <summary>
    /// Whether this skill is worth loading for the current context. Matching is
    /// deliberately generous — a skill the user wrote for "Blender" should fire
    /// on a window titled "untitled.blend - Blender 4.2" — because a skill that
    /// silently fails to load is worse than one that loads unnecessarily.
    /// </summary>
    public bool Matches(string? application, string? request)
    {
        var haystack = $"{application} {request}".ToLowerInvariant();
        if (haystack.Trim().Length == 0)
        {
            return false;
        }

        return AppliesTo.Any(term =>
                   term.Length > 0 && haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
               haystack.Contains(Name, StringComparison.OrdinalIgnoreCase);
    }
}
