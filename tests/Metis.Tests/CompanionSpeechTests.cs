using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// The companion bar sits over the user's work, so what it is allowed to say
/// matters as much as what it says.
/// </summary>
public sealed class CompanionSpeechTests
{
    [Fact]
    public void A_short_remark_is_written_in_the_bar()
    {
        var line = CompanionSpeech.ChooseLine("That's the compressor threshold.", "Press here");

        Assert.Equal("That's the compressor threshold.", line);
    }

    [Fact]
    public void A_long_explanation_falls_back_to_the_pointing_cue()
    {
        const string explanation =
            "A layer mask hides pixels without deleting them, which means you can change the edit later, " +
            "and it is the reason professionals prefer masking to erasing anything permanently.";

        var line = CompanionSpeech.ChooseLine(explanation, "Press here");

        Assert.Equal("Press here", line);
    }

    [Fact]
    public void A_long_explanation_with_no_cue_writes_nothing_at_all()
    {
        const string explanation =
            "A layer mask hides pixels without deleting them, which means you can change the edit later, " +
            "and it is the reason professionals prefer masking to erasing anything permanently.";

        // Silence beats a truncated sentence hanging over the user's work.
        Assert.Null(CompanionSpeech.ChooseLine(explanation, null));
    }

    [Fact]
    public void A_paragraph_of_short_sentences_is_still_too_much()
    {
        var line = CompanionSpeech.ChooseLine("Open it. Then click. Then save. Done.", null);

        Assert.Null(line);
    }

    [Fact]
    public void Two_sentences_still_count_as_one_remark()
    {
        var line = CompanionSpeech.ChooseLine("That's the threshold. Lower it for more compression.", null);

        Assert.Equal("That's the threshold. Lower it for more compression.", line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_written_when_there_is_nothing_to_say(string? spoken) =>
        Assert.Null(CompanionSpeech.ChooseLine(spoken, null));

    [Fact]
    public void Whitespace_is_trimmed_from_what_is_written() =>
        Assert.Equal("Click Export.", CompanionSpeech.ChooseLine("  Click Export.  ", null));

    [Fact]
    public void A_line_at_the_limit_is_allowed_and_one_past_it_is_not()
    {
        var atLimit = new string('a', CompanionSpeech.MaxInlineLength);
        var overLimit = new string('a', CompanionSpeech.MaxInlineLength + 1);

        Assert.True(CompanionSpeech.IsShortEnough(atLimit));
        Assert.False(CompanionSpeech.IsShortEnough(overLimit));
    }

    [Fact]
    public void An_over_long_cue_is_rejected_too() =>
        Assert.Null(CompanionSpeech.ChooseLine(null, new string('b', CompanionSpeech.MaxInlineLength + 1)));

    [Fact]
    public void A_written_reply_keeps_the_whole_answer_unlike_a_spoken_one()
    {
        const string explanation =
            "A layer mask hides pixels without deleting them, which means you can change the edit later, " +
            "and it is the reason professionals prefer masking to erasing anything permanently.";

        // Typed: the text is the entire answer, so none of it is dropped.
        Assert.Equal(explanation, CompanionSpeech.ChooseWrittenLine(explanation, null));

        // Spoken: the voice carries it, so the bar stays out of the way.
        Assert.Null(CompanionSpeech.ChooseLine(explanation, null));
    }

    [Fact]
    public void A_runaway_written_answer_is_capped_rather_than_unbounded()
    {
        var huge = string.Join(' ', Enumerable.Repeat("word", 400));

        var written = CompanionSpeech.ChooseWrittenLine(huge, null)!;

        Assert.True(written.Length <= CompanionSpeech.MaxWrittenLength + 1);
        Assert.EndsWith("…", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_written_reply_falls_back_to_the_cue_when_there_is_no_text() =>
        Assert.Equal("Press here", CompanionSpeech.ChooseWrittenLine("   ", "Press here"));

    [Fact]
    public void Reading_time_grows_with_the_number_of_words()
    {
        var shortLine = CompanionSpeech.ReadingDuration("Click the export button.");
        var longLine = CompanionSpeech.ReadingDuration(string.Join(' ', Enumerable.Repeat("word", 40)));

        Assert.True(longLine > shortLine);
        Assert.True(shortLine >= TimeSpan.FromMilliseconds(700));
        Assert.True(longLine <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Empty_text_takes_no_time_to_read() =>
        Assert.Equal(TimeSpan.Zero, CompanionSpeech.ReadingDuration("  "));

    [Theory]
    [InlineData("one two three", 3)]
    [InlineData("  spaced   out  ", 2)]
    [InlineData("", 0)]
    public void Words_are_counted_for_pacing(string text, int expected) =>
        Assert.Equal(expected, CompanionSpeech.CountWords(text));
}
