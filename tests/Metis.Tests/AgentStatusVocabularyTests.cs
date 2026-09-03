using Metis.Core.Agents;

namespace Metis.Tests;

/// <summary>
/// The words the agent drawer uses.
///
/// These exist because the labels used to be produced by
/// <c>Status.ToString().ToUpperInvariant()</c>, which is the kind of code that
/// works, never throws, and quietly puts "AWAITINGAPPROVAL" in front of a user
/// beside a neatly-written "✓ VERIFIED". Nothing failed; it just read badly, in
/// the one place where the interface is asking permission to do something
/// risky. A test is what turns "reads badly" into something that breaks.
/// </summary>
public sealed class AgentStatusVocabularyTests
{
    [Fact]
    public void The_status_that_needs_a_person_says_so_plainly()
    {
        var label = AgentStatusVocabulary.ForTask(AgentTaskStatus.AwaitingApproval);

        Assert.Equal("⚠ NEEDS YOU", label);
        Assert.DoesNotContain("APPROVAL", label);
    }

    /// <summary>
    /// The regression, stated directly: no label may be the enum name.
    /// </summary>
    [Fact]
    public void No_label_is_a_raw_enum_name()
    {
        foreach (var status in Enum.GetValues<AgentTaskStatus>())
        {
            Assert.NotEqual(status.ToString().ToUpperInvariant(),
                AgentStatusVocabulary.ForTask(status));
        }

        foreach (var status in Enum.GetValues<AgentStepStatus>())
        {
            Assert.NotEqual(status.ToString().ToUpperInvariant(),
                AgentStatusVocabulary.ForStep(status));
        }
    }

    /// <summary>
    /// Every status has words of its own. A new one added to the enum without a
    /// label would otherwise silently fall through to "⋯ PENDING" and a running
    /// agent would sit there claiming not to have started.
    /// </summary>
    [Fact]
    public void Every_task_status_has_its_own_label()
    {
        var labels = Enum.GetValues<AgentTaskStatus>()
            .Select(AgentStatusVocabulary.ForTask)
            .ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void Every_step_status_has_its_own_label()
    {
        var labels = Enum.GetValues<AgentStepStatus>()
            .Select(AgentStatusVocabulary.ForStep)
            .ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    /// <summary>
    /// One shape for every badge — a glyph, a space, then capitals — so a column
    /// of them reads as a column rather than as a list of unrelated strings.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryLabel))]
    public void Labels_share_one_shape(string label)
    {
        Assert.Contains(' ', label);

        var glyph = label[..label.IndexOf(' ')];
        Assert.Single(glyph);
        Assert.False(char.IsLetterOrDigit(glyph[0]));

        var words = label[(label.IndexOf(' ') + 1)..];
        Assert.Equal(words.ToUpperInvariant(), words);
        Assert.NotEmpty(words);
    }

    /// <summary>
    /// Every distinct label, once.
    ///
    /// Deduplicated because the task and step vocabularies deliberately share
    /// wording where they mean the same thing — a failed task and a failed step
    /// are both "\u2715 FAILED" — and xUnit identifies a theory case by its
    /// arguments. Two cases with the same string are one case as far as the
    /// runner is concerned, and it skips the second with a warning rather than
    /// running it. That warning is easy to scroll past, and a suite that
    /// silently drops cases is worse than one that never had them.
    /// </summary>
    public static TheoryData<string> EveryLabel()
    {
        var labels = Enum.GetValues<AgentTaskStatus>().Select(AgentStatusVocabulary.ForTask)
            .Concat(Enum.GetValues<AgentStepStatus>().Select(AgentStatusVocabulary.ForStep))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal);

        var data = new TheoryData<string>();
        foreach (var label in labels)
        {
            data.Add(label);
        }

        return data;
    }

    /// <summary>
    /// The scope line is the one piece of text on the card that describes what
    /// the agent is allowed to do to the user's files, so the dangerous reading
    /// has to be the one that stands out — not a missing reassurance.
    /// </summary>
    [Fact]
    public void An_unconfined_agent_is_the_one_that_is_flagged()
    {
        var confined = AgentStatusVocabulary.ForScope(allowOutsideWorkspace: false);
        var loose = AgentStatusVocabulary.ForScope(allowOutsideWorkspace: true);

        Assert.NotEqual(confined, loose);
        Assert.StartsWith("⚠", loose);
        Assert.StartsWith("✓", confined);
        Assert.Contains("outside", loose, StringComparison.OrdinalIgnoreCase);
    }
}
