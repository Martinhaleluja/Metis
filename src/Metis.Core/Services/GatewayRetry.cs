namespace Metis.Core.Services;

/// <summary>
/// What to do when the gateway is asleep rather than broken.
///
/// Metis's gateway runs on Render's free tier. The container is stopped after
/// about fifteen minutes with no traffic, and the next request pays for a new
/// one being built: fifty to sixty seconds, during which Render's edge either
/// holds the request or answers it with a 502, 503 or 504 of its own. None of
/// that reaches the application, so the request that was refused was never
/// served — which is the whole reason a second attempt is worth making at all.
///
/// The rules live here, in one place, rather than as three slightly different
/// loops at the three call sites that need them. They are deliberately narrow:
///
/// <list type="bullet">
///   <item>Only the three "the service is not up yet" statuses are repeated. A
///   401, a 403 or a 429 is an answer, and asking again changes nothing except
///   how long the user waits to be told the same thing.</item>
///   <item>Only requests that are safe to send twice may use this. A GET of
///   <c>/v1/me</c> reads a snapshot and can be repeated all day; a turn, an
///   agent step, a checkout or a connection write all change something, and a
///   status code cannot prove which side of the edge that change happened
///   on.</item>
/// </list>
/// </summary>
public static class GatewayRetry
{
    /// <summary>
    /// How many times an idempotent call may be sent, including the first.
    ///
    /// Three, with the waits below, spans about six seconds of backoff on top
    /// of three full timeouts — comfortably past a cold start. A fourth would
    /// only lengthen the wait before Metis falls back to what it already knows,
    /// which is the correct behaviour once the service has failed to appear.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Whether this status means "the service is not up yet" rather than "no".
    ///
    /// 502, 503 and 504 are what a platform edge returns while it has nothing
    /// healthy behind it. Everything else — including every 4xx — is the
    /// application answering, and an answer is not retried.
    /// </summary>
    public static bool IsWaking(int statusCode) => statusCode is 502 or 503 or 504;

    /// <summary>
    /// How long to wait before <paramref name="attempt"/>, counting the first
    /// attempt as 1. Doubling: two seconds before the second try, four before
    /// the third. Zero for the first, which is not a retry.
    /// </summary>
    public static TimeSpan BackoffBefore(int attempt) =>
        attempt <= 1 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    /// <summary>
    /// How long a gateway call may go unanswered before the user is told why.
    ///
    /// Three seconds is past every warm request and nowhere near a cold start,
    /// so the notice appears when — and only when — there is genuinely a wait to
    /// explain.
    /// </summary>
    public static TimeSpan NoticeAfter { get; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// What the notch says while that wait is happening. Phrased as a thing
    /// that is happening and how long it takes, not as a failure: nothing has
    /// gone wrong, a server is starting.
    /// </summary>
    public const string Notice = "Waking Metis up — about 30 seconds";
}
