using System.Text.RegularExpressions;

namespace Metis.Core.Agents;

/// <summary>
/// The typed commands that start a background agent without asking the model.
///
/// This used to try to recognise agent requests in ordinary English. It no
/// longer does, and the class is smaller for it: interpreting what someone
/// meant is the model's job, and it has the conversation to do it with.
/// </summary>
public static class AgentIntentDetector
{

    /// <summary>
    /// The unambiguous form: <c>/spawn &lt;goal&gt;</c> or <c>/agent &lt;goal&gt;</c>.
    ///
    /// These skip the model entirely, which is the point of them — the user has
    /// typed both the command and the goal, so there is nothing to interpret and
    /// no reason to wait for a round trip. Every other phrasing now goes to the
    /// model instead of being pattern-matched here, because a regex cannot
    /// follow "spawn an agent" / "to do what?" / "tidy my downloads", and every
    /// attempt to widen it caught things nobody meant as a command.
    /// </summary>
    public static bool TryExtractExplicitCommand(string prompt, out string? goal)
    {
        goal = null;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var trimmed = prompt.Trim().TrimEnd('.', '!', '?');

        foreach (var pattern in SlashCommands)
        {
            var match = Regex.Match(trimmed, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                continue;
            }

            var extracted = match.Groups["goal"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                goal = extracted;
                return true;
            }
        }

        return false;
    }

    private static readonly string[] SlashCommands =
    [
        @"^/spawn\s+(?<goal>.+)$",
        @"^/agent\s+(?<goal>.+)$"
    ];

    /// <summary>
    /// Tidies a goal dictated straight into the agent shortcut.
    ///
    /// Intent is not in question here: the user held the agent chord, so
    /// whatever they said is the goal. But people naturally say "spawn an agent
    /// to tidy my downloads" rather than "tidy my downloads", and handing the
    /// first version to a worker gives it an instruction about instructing
    /// itself. This strips that run-up and nothing else -- if there is no
    /// prefix, the sentence comes back untouched.
    /// </summary>
    public static string StripSpokenSpawnPrefix(string spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken))
        {
            return string.Empty;
        }

        var trimmed = spoken.Trim().TrimEnd('.', '!', '?');
        var match = Regex.Match(trimmed, SpokenPrefix, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var goal = match.Success ? match.Groups["goal"].Value.Trim() : trimmed;
        return goal.Length > 0 ? goal : trimmed;
    }

    private const string SpokenPrefix =
        @"^(?:please\s+)?(?:can\s+you\s+)?(?:spawn|create|start|launch|run|use|have)\s+"
        + @"(?:an?\s+|another\s+|one\s+more\s+)?(?:\w+\s+)?"
        // The connector is optional: people say "have an agent tidy my
        // downloads" as readily as "...agent to tidy...", and requiring the
        // linking word left the whole sentence in place as the goal.
        + @"(?:autonomous\s+|background\s+)?agents?\s+(?:(?:to|that|which|and)\s+)?(?<goal>.+)$";

    // TryExtractSpawnGoals and TryExtractSpawnGoal lived here: a pair of
    // regexes that tried to recognise every English phrasing of "start an
    // agent" before the model was consulted. They are gone rather than kept
    // for safety, because they were not safe -- when they missed, and they
    // missed on "use an agent to...", "have an agent...", "I need an agent
    // to...", and on every two-turn exchange, the request fell through to a
    // teaching prompt that forbids claiming to act, and Metis described the
    // agent instead of starting it. The model reads the whole conversation and
    // decides now. What remains here is the one form no interpretation can
    // improve on: the user typed the command and the goal together.
}
