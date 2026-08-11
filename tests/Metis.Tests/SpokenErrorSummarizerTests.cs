using Metis.Core.Services;

namespace Metis.Tests;

public sealed class SpokenErrorSummarizerTests
{
    [Fact]
    public void Only_the_first_sentence_is_spoken()
    {
        var spoken = SpokenErrorSummarizer.Summarize(
            "Metis could not start the microphone. Check that another application is not using it, then try again.");

        Assert.Equal("Metis could not start the microphone.", spoken);
    }

    [Fact]
    public void A_windows_path_is_replaced_with_something_speakable()
    {
        var spoken = SpokenErrorSummarizer.Summarize(
            @"The Piper executable was not found at 'C:\Users\halel\tools\piper\piper.exe'.");

        Assert.DoesNotContain(@"C:\", spoken, StringComparison.Ordinal);
        Assert.Contains("the saved path", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void A_url_is_replaced_with_something_speakable()
    {
        var spoken = SpokenErrorSummarizer.Summarize("Metis could not reach https://api.anthropic.com/v1/messages now.");

        Assert.DoesNotContain("https://", spoken, StringComparison.Ordinal);
        Assert.Contains("the address", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_single_sentence_is_cut_at_a_word_boundary()
    {
        var spoken = SpokenErrorSummarizer.Summarize(
            "Anthropic rejected the request because the configured model name is not available to this API key and " +
            "the account has no remaining credit for the current billing period whatsoever");

        Assert.True(spoken.Length <= 121, $"spoken error was {spoken.Length} characters");
        Assert.EndsWith(".", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decimal_point_does_not_end_the_sentence()
    {
        var spoken = SpokenErrorSummarizer.Summarize("Ollama 0.5.1 refused the request.");

        Assert.Equal("Ollama 0.5.1 refused the request.", spoken);
    }

    [Fact]
    public void Newlines_are_collapsed_so_the_voice_gets_one_line()
    {
        var spoken = SpokenErrorSummarizer.Summarize("Metis could not\r\n  capture the screen.");

        Assert.Equal("Metis could not capture the screen.", spoken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_spoken_for_an_empty_message(string? message) =>
        Assert.Equal(string.Empty, SpokenErrorSummarizer.Summarize(message));

    [Fact]
    public void A_real_provider_failure_speaks_the_diagnosis_not_the_lead_in()
    {
        // Taken verbatim from a live run: the useful part is the provider's own
        // first sentence, which is why ReportException passes the detail rather
        // than the whole "Metis could not get an answer. ..." string.
        const string detail =
            "Claude rejected Metis's request or model settings. Your credit balance is too low to access the " +
            "Anthropic API. Please go to Plans & Billing to upgrade or purchase credits.";

        var spoken = SpokenErrorSummarizer.Summarize(detail);

        Assert.Equal("Claude rejected Metis's request or model settings.", spoken);
        Assert.DoesNotContain("could not get an answer", spoken, StringComparison.OrdinalIgnoreCase);
    }
}
