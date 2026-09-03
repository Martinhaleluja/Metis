namespace Metis.Api;

/// <summary>
/// How an upstream AI provider's refusal is described to the person waiting for
/// an answer.
///
/// Every non-2xx from Google used to become the same eleven words: "The AI
/// provider refused the request." True, and useless. It is the sentence a user
/// sees when the gateway's own key has expired, when the month's Google quota
/// is spent, when the model has been withdrawn, and when Google is simply down
/// — four completely different situations, three of which the user can do
/// nothing about and one of which means "wait a moment and try again".
///
/// What stays withheld is the provider's own error *body*, which names the
/// Google Cloud project the key belongs to and sometimes the key's own prefix.
/// That goes to the gateway's log, where an operator can read it; the caller
/// gets the classification instead.
///
/// Lifted out of Program.cs so it can be tested. It is the difference between a
/// user who retries and a user who files a bug, and it is exactly the kind of
/// mapping that rots quietly when nothing checks it.
/// </summary>
public static class ProviderFailures
{
    public static (string Kind, string Message) Describe(int status) => status switch
    {
        401 or 403 => ("provider_key",
            "Metis's own AI key was refused. This is a fault on Metis's side, not yours — "
            + "please report it, and use your own AI key under Setup in the meantime."),

        404 => ("provider_model",
            "The AI model Metis asked for is no longer available. This needs fixing on Metis's "
            + "side — please report it."),

        429 => ("provider_busy",
            "Metis's AI is busy right now — too many requests at once. Wait a moment and ask again."),

        >= 500 => ("provider_down",
            "Metis's AI provider is having trouble at the moment. Try again shortly."),

        400 => ("provider_request",
            "The AI provider rejected the shape of that request. This is a fault on Metis's side — "
            + "please report it, with what you asked."),

        _ => ("provider",
            $"Metis's AI provider refused the request (error {status}). Try again shortly.")
    };

    /// <summary>
    /// Splits "provider_429|{body}" back into its two halves.
    ///
    /// The status and the body travel together from the upstream call so that
    /// one can be classified for the user and the other logged for an operator.
    /// Both used to be discarded at the point of failure, which is why the
    /// sentence that reached the user could not say anything.
    /// </summary>
    public static (string Status, string Body) Split(string failure)
    {
        var separator = failure.IndexOf('|');
        return separator < 0
            ? (failure, string.Empty)
            : (failure[..separator], failure[(separator + 1)..]);
    }

    /// <summary>Reads the numeric status back out of a "provider_429" marker.</summary>
    public static int StatusCode(string status) =>
        status.StartsWith("provider_", StringComparison.Ordinal)
        && int.TryParse(status.AsSpan("provider_".Length), out var code)
            ? code
            : 0;

    /// <summary>
    /// Caps a provider's error body before it reaches a log line. These run to
    /// kilobytes of JSON, and only the start of one has ever been useful.
    /// </summary>
    public static string Truncate(string value, int limit) =>
        string.IsNullOrEmpty(value) || value.Length <= limit ? value : value[..limit] + "\u2026";
}
