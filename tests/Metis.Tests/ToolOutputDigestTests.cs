using Metis.Core.Agents;

namespace Metis.Tests;

/// <summary>
/// An agent debugging a build only ever sees this function's output. The old
/// rule kept the head and the tail, which for a compiler means the banner and
/// the summary — everything except the errors. These pin the fix.
/// </summary>
public sealed class ToolOutputDigestTests
{
    [Fact]
    public void Short_output_is_returned_untouched()
    {
        const string output = "Build succeeded.";

        Assert.Equal(output, ToolOutputDigest.Summarize(output, 2000));
    }

    [Fact]
    public void The_errors_survive_even_when_buried_in_the_middle()
    {
        // The exact shape that defeated head-and-tail: banner, then a lot of
        // ordinary progress, then the errors, then a summary.
        var lines = new List<string> { "MSBuild version 17.11" };
        for (var i = 0; i < 400; i++)
        {
            lines.Add($"  Restored package {i} of 400");
        }

        lines.Add("Program.cs(42,17): error CS0103: The name 'foo' does not exist");
        lines.Add("Program.cs(51,9): error CS1002: ; expected");

        for (var i = 0; i < 400; i++)
        {
            lines.Add($"  Copying file {i}");
        }

        lines.Add("Build FAILED. 2 Error(s)");

        var digest = ToolOutputDigest.Summarize(string.Join('\n', lines), 800);

        Assert.Contains("CS0103", digest);
        Assert.Contains("CS1002", digest);
        Assert.True(digest.Length <= 900, $"Digest was {digest.Length} chars.");
    }

    [Fact]
    public void Output_with_no_problems_still_gets_a_head_and_a_tail()
    {
        var listing = string.Join('\n', Enumerable.Range(0, 500).Select(i => $"file-{i}.txt"));

        var digest = ToolOutputDigest.Summarize(listing, 400);

        Assert.Contains("file-0.txt", digest);
        Assert.Contains("file-499.txt", digest);
        Assert.Contains("truncated", digest);
    }

    [Fact]
    public void When_there_are_too_many_problems_the_count_is_stated()
    {
        var lines = Enumerable.Range(0, 300)
            .Select(i => $"src/thing{i}.ts(1,1): error TS2304: Cannot find name 'x{i}'");

        var digest = ToolOutputDigest.Summarize(string.Join('\n', lines), 500);

        Assert.Contains("more problem lines", digest);
    }

    [Theory]
    [InlineData("Program.cs(42,17): error CS0103: missing", true)]
    [InlineData("npm ERR! code ELIFECYCLE", true)]
    [InlineData("Traceback (most recent call last):", true)]
    [InlineData("Access to the path is denied", true)]
    [InlineData("warning CS0168: variable declared but never used", true)]
    [InlineData("  Restored 42 packages", false)]
    [InlineData("Build succeeded.", false)]
    [InlineData("", false)]
    public void Diagnostic_lines_are_recognised(string line, bool expected)
    {
        Assert.Equal(expected, ToolOutputDigest.LooksLikeProblem(line));
    }

    [Fact]
    public void A_zero_budget_does_not_throw()
    {
        Assert.Equal("anything", ToolOutputDigest.Summarize("anything", 0));
    }

    [Fact]
    public void Null_output_comes_back_empty_rather_than_null()
    {
        Assert.Equal(string.Empty, ToolOutputDigest.Summarize(null, 100));
    }
}
