using System.Globalization;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Alerts;

/// <summary>
/// Evaluates alert rules against a refresh, using the previous refresh to detect transitions.
///
/// Everything here is edge-triggered rather than level-triggered: "went live" fires once when a
/// channel appears that wasn't there before, and a viewer threshold fires on the crossing, not on
/// every refresh while the channel sits above the line. Without that the alert feed would refill
/// with the same entries every cycle.
/// </summary>
public static class AlertEngine
{
    public static IReadOnlyList<FiredAlert> Evaluate(
        IReadOnlyList<LiveStream> current,
        IReadOnlyList<TrendEntry> trending,
        PreviousState previous,
        IReadOnlyList<AlertRule> rules,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var fired = new List<FiredAlert>();

        var momentumByKey = trending
            .Where(t => t.MomentumRatio is not null)
            .ToDictionary(t => t.Stream.Key, t => t.MomentumRatio!.Value);

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            foreach (var stream in current)
            {
                if (rule.TrackedChannelsOnly && !stream.IsTracked)
                    continue;

                if (rule.RuleFilter is { Length: > 0 } filter
                    && !stream.MatchedRules.Contains(filter, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var message = rule.Trigger switch
                {
                    AlertTrigger.WentLive => EvaluateWentLive(stream, previous),
                    AlertTrigger.ViewerThreshold => EvaluateThreshold(stream, previous, rule.Threshold),
                    AlertTrigger.MomentumSpike => EvaluateSpike(stream, previous, momentumByKey, rule.Threshold),
                    _ => null,
                };

                if (message is null)
                    continue;

                fired.Add(new FiredAlert
                {
                    FiredAtUtc = now,
                    RuleName = rule.Name,
                    ChannelName = stream.DisplayName,
                    Platform = stream.Platform,
                    Message = message,
                    Url = stream.Url,
                });
            }
        }

        return fired;
    }

    private static string? EvaluateWentLive(LiveStream stream, PreviousState previous)
    {
        // An empty previous state means a cold start — treat nothing as newly live, otherwise the
        // first refresh after launch would alert for every channel at once.
        if (previous.IsEmpty || previous.LiveKeys.Contains(stream.Key))
            return null;

        return "went live";
    }

    private static string? EvaluateThreshold(LiveStream stream, PreviousState previous, double threshold)
    {
        var limit = (int)threshold;
        if (stream.ViewerCount < limit)
            return null;

        // Only fire on the upward crossing.
        if (previous.ViewersByKey.TryGetValue(stream.Key, out var before) && before >= limit)
            return null;

        return $"passed {limit:N0} viewers";
    }

    private static string? EvaluateSpike(
        LiveStream stream,
        PreviousState previous,
        IReadOnlyDictionary<string, double> momentumByKey,
        double threshold)
    {
        if (!momentumByKey.TryGetValue(stream.Key, out var ratio) || ratio < threshold)
            return null;

        if (previous.SpikingKeys.Contains(stream.Key))
            return null;

        return $"spiking at {ratio.ToString(ratio >= 10 ? "0" : "0.0", CultureInfo.InvariantCulture)}× their usual";
    }

    /// <summary>What the previous refresh looked like — the reference point for every edge trigger.</summary>
    public sealed record PreviousState
    {
        public IReadOnlySet<string> LiveKeys { get; init; } = new HashSet<string>();
        public IReadOnlyDictionary<string, int> ViewersByKey { get; init; } = new Dictionary<string, int>();
        public IReadOnlySet<string> SpikingKeys { get; init; } = new HashSet<string>();

        public bool IsEmpty => LiveKeys.Count == 0;

        public static PreviousState From(IReadOnlyList<LiveStream> streams, IReadOnlyList<TrendEntry> trending, double spikeThreshold)
        {
            return new PreviousState
            {
                LiveKeys = streams.Select(s => s.Key).ToHashSet(StringComparer.Ordinal),
                ViewersByKey = streams
                    .GroupBy(s => s.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Max(s => s.ViewerCount), StringComparer.Ordinal),
                SpikingKeys = trending
                    .Where(t => t.MomentumRatio >= spikeThreshold)
                    .Select(t => t.Stream.Key)
                    .ToHashSet(StringComparer.Ordinal),
            };
        }

        public static PreviousState Empty => new();
    }
}
