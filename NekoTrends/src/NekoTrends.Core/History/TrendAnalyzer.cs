using System.Globalization;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.History;

public sealed record TrendOptions
{
    /// <summary>Streams below this are ignored entirely — a channel going 20 → 60 viewers is not news.</summary>
    public int MinViewers { get; init; } = 50;

    /// <summary>
    /// Samples newer than this are excluded from the baseline. Without it, a streamer who has
    /// been spiking for hours quietly raises their own baseline and stops looking like news.
    /// </summary>
    public TimeSpan SelfExclusionWindow { get; init; } = TimeSpan.FromHours(2);

    /// <summary>Below this many usable samples we can't claim to know a channel's normal size.</summary>
    public int MinSamplesForBaseline { get; init; } = 3;

    /// <summary>How far above baseline a channel must be to count as surging.</summary>
    public double MinMomentumRatio { get; init; } = 1.25;

    /// <summary>
    /// Relative weight of never-before-seen channels. Debuts and sudden arrivals are genuinely
    /// newsworthy, but without history there's no ratio to earn a rank, so they get a flat handicap.
    /// </summary>
    public double NewcomerWeight { get; init; } = 0.6;
}

/// <summary>
/// Turns raw live streams plus stored history into the ranked "what's blowing up" feed.
///
/// Ranking deliberately multiplies momentum by audience size: a channel tripling from 40 to 120
/// viewers is statistically noisier and less newsworthy than one tripling from 4,000 to 12,000,
/// and a pure-ratio sort would bury the second under the first.
/// </summary>
public static class TrendAnalyzer
{
    public static IReadOnlyList<TrendEntry> Analyze(
        IEnumerable<LiveStream> currentStreams,
        IReadOnlyDictionary<string, IReadOnlyList<ViewerSample>> history,
        TrendOptions? options = null,
        DateTimeOffset? nowUtc = null)
    {
        var opts = options ?? new TrendOptions();
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var baselineCutoff = now - opts.SelfExclusionWindow;

        var results = new List<TrendEntry>();

        foreach (var stream in currentStreams)
        {
            if (stream.ViewerCount < opts.MinViewers)
                continue;

            history.TryGetValue(stream.Key, out var samples);
            var usable = samples?.Where(s => s.TimestampUtc <= baselineCutoff).ToList() ?? [];

            if (usable.Count < opts.MinSamplesForBaseline)
            {
                results.Add(new TrendEntry
                {
                    Stream = stream,
                    IsNewcomer = true,
                    SampleCount = usable.Count,
                    Score = Magnitude(stream.ViewerCount) * opts.NewcomerWeight,
                    Headline = usable.Count == 0
                        ? "New to the feed"
                        : "Still learning their usual numbers",
                });
                continue;
            }

            var baseline = Median(usable.Select(s => s.ViewerCount).ToList());
            var ratio = stream.ViewerCount / (double)Math.Max(baseline, 1);

            if (ratio < opts.MinMomentumRatio)
                continue;

            results.Add(new TrendEntry
            {
                Stream = stream,
                BaselineViewers = baseline,
                MomentumRatio = ratio,
                SampleCount = usable.Count,
                Score = (ratio - 1.0) * Magnitude(stream.ViewerCount),
                Headline = FormatHeadline(ratio),
            });
        }

        return results
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.Stream.ViewerCount)
            .ToList();
    }

    /// <summary>
    /// Audience-size weight. Log-scaled so a 100x bigger channel counts as roughly twice as
    /// newsworthy rather than 100 times, which keeps mid-size VTubers on the board at all.
    /// </summary>
    private static double Magnitude(int viewers) => Math.Log10(Math.Max(viewers, 10));

    internal static int Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (int)Math.Round((sorted[mid - 1] + sorted[mid]) / 2.0, MidpointRounding.AwayFromZero);
    }

    private static string FormatHeadline(double ratio)
    {
        var multiple = ratio.ToString(ratio >= 10 ? "0" : "0.0", CultureInfo.InvariantCulture);
        return $"{multiple}× their usual";
    }
}
