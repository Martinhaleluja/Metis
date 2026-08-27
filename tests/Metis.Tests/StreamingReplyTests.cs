using System.Text.Json;
using Metis.AI;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// A reply is shown while it is still arriving, which means reading a sentence
/// out of a JSON object that is not finished yet. Two properties make that safe
/// to put on screen: what has been shown never has to be taken back, and no
/// character is shown before it is certain what it is.
/// </summary>
public sealed class StreamingReplyTests
{
    private const string Reply =
        """{"screen_observed":true,"spoken_text":"Click the blue Save button.","bubble_cue":"Press here"}""";

    [Fact]
    public void The_sentence_only_ever_grows_as_the_reply_arrives()
    {
        var shown = string.Empty;

        for (var length = 1; length <= Reply.Length; length++)
        {
            if (!AssistantPlanParser.TryReadSpokenTextPrefix(Reply[..length], out var spoken, out _))
            {
                continue;
            }

            Assert.StartsWith(shown, spoken, StringComparison.Ordinal);
            shown = spoken;
        }

        Assert.Equal("Click the blue Save button.", shown);
    }

    [Fact]
    public void The_finished_sentence_is_reported_as_finished()
    {
        Assert.True(AssistantPlanParser.TryReadSpokenTextPrefix(Reply, out var spoken, out var complete));

        Assert.True(complete);
        Assert.Equal("Click the blue Save button.", spoken);

        // Cut off before the closing quote, the same words are readable but the
        // sentence is not yet finished, so more may still be appended to it.
        var partial = Reply[..Reply.IndexOf("button", StringComparison.Ordinal)];
        Assert.True(AssistantPlanParser.TryReadSpokenTextPrefix(partial, out var half, out var halfComplete));
        Assert.False(halfComplete);
        Assert.Equal("Click the blue Save", half);
    }

    /// <summary>
    /// A backslash escape can be split across two frames. The old salvage path
    /// took whatever it had literally, which is tolerable when rescuing a
    /// truncated reply and quite wrong when those characters are being typed
    /// onto the screen: a half-arrived é would show as a stray "u00e".
    /// </summary>
    [Theory]
    [InlineData("""{"spoken_text":"Caf\""", "Caf")]
    [InlineData("""{"spoken_text":"Caf\u""", "Caf")]
    [InlineData("""{"spoken_text":"Caf\u00e""", "Caf")]
    [InlineData("""{"spoken_text":"Café""", "Café")]
    public void A_half_arrived_escape_is_never_shown(string arrived, string expected)
    {
        Assert.True(AssistantPlanParser.TryReadSpokenTextPrefix(arrived, out var spoken, out var complete));

        Assert.Equal(expected, spoken);
        Assert.False(complete);
    }

    [Fact]
    public void Newlines_and_quotes_survive_the_reveal()
    {
        const string escaped = """{"spoken_text":"Open \"File\",\nthen Save."}""";

        Assert.True(AssistantPlanParser.TryReadSpokenTextPrefix(escaped, out var spoken, out var complete));

        Assert.True(complete);
        Assert.Equal("Open \"File\",\nthen Save.", spoken);
    }

    [Fact]
    public void Nothing_is_readable_before_the_sentence_starts()
    {
        Assert.False(AssistantPlanParser.TryReadSpokenTextPrefix("""{"screen_observed":true,""", out _, out _));
    }

    /// <summary>
    /// The whole benefit of streaming depends on the sentence being written
    /// early. If the model emits the twelve-step lesson array first, the reply
    /// still arrives in fragments but the user sees nothing until the end of it.
    /// </summary>
    [Fact]
    public void The_schema_asks_for_the_sentence_before_the_lesson()
    {
        var json = GeminiRequestBuilder.BuildGenerateContentJson(
            new GeminiRequest("What is this?"),
            "gemini-3.5-flash");

        using var document = JsonDocument.Parse(json);
        var schema = document.RootElement
            .GetProperty("generationConfig")
            .GetProperty("responseJsonSchema");

        var ordering = schema.GetProperty("propertyOrdering")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .ToArray();

        Assert.Equal("screen_observed", ordering[0]);
        Assert.Equal("spoken_text", ordering[1]);
        Assert.Equal("steps", ordering[^1]);

        // Providers that do not read propertyOrdering follow the order the
        // properties are declared in, so that has to agree with it.
        var declared = schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal("screen_observed", declared[0]);
        Assert.Equal("spoken_text", declared[1]);
    }
}
