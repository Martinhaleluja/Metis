using Metis.Core.Models;

namespace Metis.Core.Services;

/// <summary>
/// What a model has been used for, against what it allows.
/// </summary>
public sealed record ModelUsage(
    string ModelId,
    int LastHour,
    int Today,
    int ThisWeek,
    int RequestsPerMinute,
    int RequestsPerDay)
{
    /// <summary>
    /// How much of the day's allowance is gone, 0 to 1, or null when the
    /// provider publishes no daily limit. Null is not zero: an unknown limit
    /// must not be drawn as an empty bar, which reads as "plenty left".
    /// </summary>
    public double? DailyFraction =>
        RequestsPerDay > 0 ? Math.Clamp(Today / (double)RequestsPerDay, 0, 1) : null;

    /// <summary>
    /// A line for the picker. Says what is known and stays quiet about what is
    /// not, rather than showing a limit of zero.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>();

        parts.Add(RequestsPerDay > 0
            ? $"{Today} of {RequestsPerDay} today"
            : $"{Today} today");

        if (RequestsPerMinute > 0)
        {
            parts.Add($"{LastHour} this hour, {RequestsPerMinute}/min allowed");
        }
        else if (LastHour > 0)
        {
            parts.Add($"{LastHour} this hour");
        }

        parts.Add($"{ThisWeek} this week");
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Counts requests per model, on this machine.
///
/// Counted locally on purpose. Most Metis requests go straight from the desktop
/// to the provider on the user's own key and never touch a Metis server, so a
/// server-side count would show zero for exactly the people who most want to
/// know how much of a free allowance they have left. The provider's real
/// remaining quota is authoritative and this is not — it is an honest local
/// tally of what Metis itself has sent.
///
/// Pure and time-injectable so the rollovers can be tested without waiting a
/// week for one.
/// </summary>
public sealed class ModelUsageLedger
{
    /// <summary>
    /// Timestamps of requests, newest last, per model. Bounded because a ledger
    /// that grows forever on a machine left running is a slow leak.
    /// </summary>
    private readonly Dictionary<string, List<DateTimeOffset>> _events = new(StringComparer.OrdinalIgnoreCase);

    private const int MaximumRetainedPerModel = 5000;

    /// <summary>How long a request stays counted. A week, because that is the longest window shown.</summary>
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);

    public void Record(string modelId, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        if (!_events.TryGetValue(modelId, out var times))
        {
            times = [];
            _events[modelId] = times;
        }

        times.Add(at);
        Prune(times, at);
    }

    /// <summary>
    /// Usage for one model, against the limits the catalogue records for it.
    /// </summary>
    public ModelUsage For(ModelOption model, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);

        var times = _events.TryGetValue(model.Id, out var recorded) ? recorded : [];

        // A rolling window rather than a calendar boundary: "in the last hour"
        // is what a rate limit actually means, and a calendar hour would report
        // nothing used one minute after the clock ticks over.
        var hourAgo = now - TimeSpan.FromHours(1);
        var dayAgo = now - TimeSpan.FromDays(1);
        var weekAgo = now - RetentionWindow;

        return new ModelUsage(
            model.Id,
            times.Count(time => time > hourAgo),
            times.Count(time => time > dayAgo),
            times.Count(time => time > weekAgo),
            model.RequestsPerMinute,
            model.RequestsPerDay);
    }

    /// <summary>The models used at all in the retained window, busiest first.</summary>
    public IReadOnlyList<string> UsedModels(DateTimeOffset now) =>
        _events
            .Where(entry => entry.Value.Any(time => time > now - RetentionWindow))
            .OrderByDescending(entry => entry.Value.Count)
            .Select(entry => entry.Key)
            .ToArray();

    /// <summary>
    /// The whole ledger, for saving. Kept as plain values so the store does not
    /// need to know anything about this class.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> Snapshot() =>
        _events.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<DateTimeOffset>)entry.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

    public void Restore(IReadOnlyDictionary<string, IReadOnlyList<DateTimeOffset>> snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _events.Clear();

        foreach (var (modelId, times) in snapshot)
        {
            var kept = times.Where(time => time > now - RetentionWindow).OrderBy(time => time).ToList();
            if (kept.Count > 0)
            {
                Prune(kept, now);
                _events[modelId] = kept;
            }
        }
    }

    private static void Prune(List<DateTimeOffset> times, DateTimeOffset now)
    {
        var cutoff = now - RetentionWindow;
        times.RemoveAll(time => time <= cutoff);

        if (times.Count > MaximumRetainedPerModel)
        {
            times.RemoveRange(0, times.Count - MaximumRetainedPerModel);
        }
    }
}
