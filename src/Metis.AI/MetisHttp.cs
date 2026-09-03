namespace Metis.AI;

/// <summary>
/// The connection pool every provider shares.
///
/// Each provider used to construct a bare <see cref="HttpClient"/> with nothing
/// but a timeout, which meant default connection handling and one setting doing
/// two jobs: the same number bounded both "how long may this host take to
/// accept a connection" and "how long may the whole answer take". A dead
/// network therefore cost the full answer timeout — sixty-five seconds of
/// nothing, before the next provider in the chain was even tried.
///
/// One handler is shared across all of them so sockets are pooled and reused:
/// a second question to the same provider skips the TCP and TLS handshake
/// entirely. The handler is deliberately never disposed. It lives as long as
/// the process, and disposing it while any client still held it would break
/// every provider at once.
/// </summary>
internal static class MetisHttp
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        // Long enough that consecutive turns reuse a warm connection, short
        // enough that DNS changes are picked up within a session.
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

        // Reaching the host is a separate question from the model answering.
        // An unreachable endpoint now fails in seconds instead of spending the
        // whole request budget.
        ConnectTimeout = TimeSpan.FromSeconds(10),
        EnableMultipleHttp2Connections = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    };

    /// <summary>
    /// A client over the shared pool. <paramref name="timeout"/> bounds the
    /// whole exchange for a buffered request; for a streamed one it bounds the
    /// wait for the response headers, because the body is read as it arrives.
    /// </summary>
    internal static HttpClient CreateClient(TimeSpan timeout) =>
        new(SharedHandler, disposeHandler: false)
        {
            Timeout = timeout,

            // The image on a screen question is a few hundred kilobytes of
            // base64. Waiting for a 100-continue before sending it adds a round
            // trip to every turn for no benefit against these APIs.
            DefaultRequestHeaders = { ExpectContinue = false }
        };
}
