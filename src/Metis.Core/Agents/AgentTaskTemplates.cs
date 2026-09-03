namespace Metis.Core.Agents;

/// <summary>
/// The presets offered on the spawn panel, and the extra guidance each one
/// gives the agent that runs it.
///
/// This exists because the template id was being passed straight into the
/// agent's system prompt as its "special instructions". Choosing the Organise
/// Downloads preset therefore told the agent, in full:
///
///     ### SPECIAL INSTRUCTIONS:
///     organize_downloads
///
/// which is a slug rather than guidance, and which the model had to guess the
/// meaning of. The <see cref="AgentTaskTemplate.SystemPromptExtra"/> field this
/// resolves to had been declared for exactly this purpose and never used.
///
/// The guidance below is deliberately about how to go about the job rather than
/// what the job is — the goal text already says what. What a preset adds is the
/// care a person would take doing that particular kind of work: not moving
/// files without looking inside them, not trusting one source, not reading a
/// single crash as a pattern.
/// </summary>
public static class AgentTaskTemplates
{
    public static IReadOnlyList<AgentTaskTemplate> All { get; } =
    [
        new(
            Id: "organize_downloads",
            Name: "Organise downloads",
            Description: "Sort a cluttered folder into sensible subfolders and log what moved.",
            Icon: "\U0001F4C1",
            EnabledToolCategories: ["file_system", "verification"],
            SystemPromptExtra: """
                You are tidying a folder someone actually uses, so the standard is that they can find
                everything afterwards and undo anything they disagree with.
                List the folder and read what is there before moving a single file. Sort by what a file is,
                not by its extension alone -- an .exe in Downloads may be an installer worth keeping.
                Never delete anything: move it. If two files would collide, rename rather than overwrite.
                Leave a log naming every move you made, so the whole thing can be reversed by hand.
                """),

        new(
            Id: "web_research",
            Name: "Research a topic",
            Description: "Search, read, and write up findings with sources.",
            Icon: "\U0001F50D",
            EnabledToolCategories: ["web", "file_system"],
            SystemPromptExtra: """
                Search more than once and from more than one angle before you conclude anything. A single
                page agreeing with your first search is not corroboration.
                Attribute every claim to where you found it, and say plainly when sources disagree rather
                than picking the tidier answer. Note the date of what you read: on a fast-moving subject a
                confident page from three years ago is worse than no page.
                Write for someone who has not done the reading, and keep your own inferences visibly
                separate from what the sources actually said.
                """),

        new(
            Id: "system_logs",
            Name: "Audit error logs",
            Description: "Read Windows event logs and summarise what keeps failing.",
            Icon: "\U0001F4CB",
            EnabledToolCategories: ["process", "file_system", "verification"],
            SystemPromptExtra: """
                Read logs; do not change the system. Nothing here should install, repair, or reconfigure
                anything, whatever the logs suggest.
                Count before you conclude. One crash is an event, the same crash forty times is a pattern,
                and only the second is worth reporting as a cause. Group by what failed rather than by when.
                Report timestamps and identifiers exactly as written, and where the evidence only supports a
                guess, say that it is one.
                """),

        new(
            Id: "find_extract",
            Name: "Find and extract",
            Description: "Search files for figures and pull them into one place.",
            Icon: "\U0001F4CA",
            EnabledToolCategories: ["file_system", "verification"],
            SystemPromptExtra: """
                Read the files rather than pattern-matching filenames, and quote enough surrounding text
                that any number you extract can be traced back to where it came from.
                Where a figure is ambiguous, missing, or contradicted elsewhere, record that instead of
                choosing. A gap you have marked is useful; a gap you have quietly filled is not.
                Check your totals against the source before you finish.
                """)
    ];

    /// <summary>
    /// The extra guidance for a template id, or null when there is none.
    ///
    /// Null rather than the id itself, which is what the bug this replaces was
    /// doing. An unknown template should add nothing to the prompt, not add
    /// noise to it.
    /// </summary>
    public static string? PromptExtraFor(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        foreach (var template in All)
        {
            if (string.Equals(template.Id, templateId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return template.SystemPromptExtra;
            }
        }

        return null;
    }
}
