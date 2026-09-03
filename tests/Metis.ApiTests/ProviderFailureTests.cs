using Metis.Api;

namespace Metis.ApiTests;

/// <summary>
/// What Metis says when the AI provider refuses a turn.
///
/// This existed as one sentence for every possible cause — "The AI provider
/// refused the request." — which is the report that started this: a user on the
/// Free plan, entitled to answers, being told nothing at all about why they
/// were not getting any. These check that the four distinguishable causes stay
/// distinguishable, and that the provider's own error text never becomes part
/// of what the user is shown.
/// </summary>
public sealed class ProviderFailureTests
{
    [Theory]
    [InlineData(401, "provider_key")]
    [InlineData(403, "provider_key")]
    [InlineData(404, "provider_model")]
    [InlineData(429, "provider_busy")]
    [InlineData(400, "provider_request")]
    [InlineData(500, "provider_down")]
    [InlineData(503, "provider_down")]
    public void Each_upstream_status_is_classified(int status, string expected) =>
        Assert.Equal(expected, ProviderFailures.Describe(status).Kind);

    /// <summary>
    /// A busy provider and a rejected key must never read the same. One is
    /// "wait a moment", the other is "this will not work until someone fixes
    /// it", and a user who cannot tell them apart either gives up on a
    /// temporary fault or waits forever on a permanent one.
    /// </summary>
    [Fact]
    public void A_temporary_refusal_does_not_read_like_a_permanent_one()
    {
        var busy = ProviderFailures.Describe(429).Message;
        var key = ProviderFailures.Describe(401).Message;

        Assert.NotEqual(busy, key);
        Assert.Contains("Wait a moment", busy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every message has to be sayable to a user: no status codes standing
    /// alone, no provider jargon, and something to do next.
    /// </summary>
    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(400)]
    public void Every_message_says_what_to_do(int status)
    {
        var message = ProviderFailures.Describe(status).Message;

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.EndsWith(".", message.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain("x-goog", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unrecognised status still says something, and names the code so a
    /// report about it can be acted on.
    /// </summary>
    [Fact]
    public void An_unknown_status_still_names_itself()
    {
        var (kind, message) = ProviderFailures.Describe(418);

        Assert.Equal("provider", kind);
        Assert.Contains("418", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_and_the_provider_body_travel_separately()
    {
        var (status, body) = ProviderFailures.Split("provider_429|{\"error\":\"quota\"}");

        Assert.Equal("provider_429", status);
        Assert.Equal("{\"error\":\"quota\"}", body);
        Assert.Equal(429, ProviderFailures.StatusCode(status));
    }

    /// <summary>
    /// A failure marker with no body — the streaming path produces these — must
    /// still split cleanly rather than losing the status.
    /// </summary>
    [Fact]
    public void A_failure_with_no_body_still_splits()
    {
        var (status, body) = ProviderFailures.Split("provider_500");

        Assert.Equal("provider_500", status);
        Assert.Empty(body);
        Assert.Equal(500, ProviderFailures.StatusCode(status));
    }

    [Fact]
    public void An_unparseable_marker_yields_the_catch_all()
    {
        Assert.Equal(0, ProviderFailures.StatusCode("degraded"));
        Assert.Equal("provider", ProviderFailures.Describe(0).Kind);
    }

    [Fact]
    public void A_long_provider_body_is_capped_before_it_reaches_a_log()
    {
        var truncated = ProviderFailures.Truncate(new string('x', 5_000), 600);

        Assert.Equal(601, truncated.Length);
        Assert.EndsWith("\u2026", truncated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_provider_body_is_left_alone()
    {
        Assert.Equal("nope", ProviderFailures.Truncate("nope", 600));
        Assert.Equal(string.Empty, ProviderFailures.Truncate(string.Empty, 600));
    }
}
