using System.Text;

namespace Metis.Core.Agents;

/// <summary>
/// Cuts a long tool result down to what an agent needs to read.
///
/// The old rule kept the first 65% and the last of whatever fitted. For a
/// directory listing that is fine. For the output of a failing build it is
/// close to the worst possible choice: compilers print a banner, then the
/// errors, then a summary — so head-and-tail keeps the banner and the summary
/// and discards the errors, which are the only lines that say what to fix. An
/// agent debugging a build was being shown everything except the problem.
///
/// So lines that look like a diagnostic are kept first, and ordinary output
/// fills whatever room is left. This is a heuristic and makes no attempt to
/// understand any particular compiler; it only has to beat cutting the middle
/// out blindly, which is a low bar.
/// </summary>
public static class ToolOutputDigest
{
    /// <summary>
    /// Reduces <paramref name="output"/> to at most <paramref name="maxChars"/>,
    /// keeping diagnostics in preference to everything else.
    /// </summary>
    public static string Summarize(string? output, int maxChars)
    {
        if (string.IsNullOrEmpty(output) || output.Length <= maxChars || maxChars <= 0)
        {
            return output ?? string.Empty;
        }

        var lines = output.Split('\n');
        var problems = new List<string>();
        var ordinary = new List<string>();

        foreach (var line in lines)
        {
            if (LooksLikeProblem(line))
            {
                problems.Add(line.TrimEnd('\r'));
            }
            else
            {
                ordinary.Add(line.TrimEnd('\r'));
            }
        }

        // Nothing diagnostic in here, so this is ordinary output and the old
        // head-and-tail behaviour is the right shape for it.
        if (problems.Count == 0)
        {
            return HeadAndTail(output, maxChars);
        }

        var builder = new StringBuilder();
        var problemBudget = (int)(maxChars * 0.7);
        var keptProblems = 0;

        foreach (var problem in problems)
        {
            if (builder.Length + problem.Length + 1 > problemBudget)
            {
                break;
            }

            builder.Append(problem).Append('\n');
            keptProblems++;
        }

        if (keptProblems < problems.Count)
        {
            builder.Append($"... and {problems.Count - keptProblems} more problem lines ...\n");
        }

        // Whatever room is left goes to the tail of the ordinary output, which
        // is where the summary line and the exit status usually sit.
        var remaining = maxChars - builder.Length - 40;
        if (remaining > 80 && ordinary.Count > 0)
        {
            var context = string.Join('\n', ordinary);
            builder.Append("--- other output ---\n");
            builder.Append(context.Length <= remaining ? context : context[^remaining..]);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Whether a line reads like a compiler or runtime complaint.
    ///
    /// Matching on the word alone is enough here and deliberately generous:
    /// keeping a few extra lines costs a little of the budget, whereas missing
    /// the one line naming the real fault costs the agent the whole debugging
    /// turn.
    /// </summary>
    public static bool LooksLikeProblem(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        foreach (var marker in Markers)
        {
            if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] Markers =
    [
        "error", "warning", "failed", "failure", "exception",
        "traceback", "cannot find", "not found", "unresolved",
        "denied", "refused", "fatal", "panic:", "npm err"
    ];

    private static string HeadAndTail(string output, int maxChars)
    {
        var headLength = (int)(maxChars * 0.65);
        var tailLength = maxChars - headLength - 60;
        if (tailLength < 100)
        {
            tailLength = 100;
        }

        var omitted = output.Length - (headLength + tailLength);
        if (omitted <= 0)
        {
            return output;
        }

        return $"{output[..headLength]}\n... [truncated {omitted} characters] ...\n{output[^tailLength..]}";
    }
}
