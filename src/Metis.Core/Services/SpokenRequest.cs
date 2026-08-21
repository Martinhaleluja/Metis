namespace Metis.Core.Services;

/// <summary>
/// The stand-in Metis sends while a spoken request is still only a recording.
///
/// Until the words are transcribed there is nothing to read, so Metis sends
/// this sentence in their place and the model reports back what it actually
/// heard. Naming it in one spot keeps anything from mistaking Metis's own
/// placeholder for something the user said.
/// </summary>
public static class SpokenRequest
{
    public const string Placeholder =
        "Listen to the attached recording and answer the user's request directly.";

    public static bool IsPlaceholder(string? text) =>
        string.Equals((text ?? string.Empty).Trim(), Placeholder, StringComparison.OrdinalIgnoreCase);
}
