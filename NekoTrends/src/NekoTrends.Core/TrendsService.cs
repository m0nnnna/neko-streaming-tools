using NekoTrends.Core.Config;
using NekoTrends.Core.Discovery;
using NekoTrends.Core.History;
using NekoTrends.Core.Models;

namespace NekoTrends.Core;

/// <summary>One completed refresh across every configured platform.</summary>
public sealed record TrendsSnapshot
{
    public required DateTimeOffset FetchedAtUtc { get; init; }

    /// <summary>Everything live right now, biggest first — backs the "Top Now" tab.</summary>
    public required IReadOnlyList<LiveStream> TopNow { get; init; }

    /// <summary>Only what's moving unusually — backs the "Trending" tab.</summary>
    public required IReadOnlyList<TrendEntry> Trending { get; init; }

    /// <summary>Per-platform failure messages. A platform that errored is absent from the lists above.</summary>
    public required IReadOnlyDictionary<StreamPlatform, string> Errors { get; init; }

    /// <summary>Platforms that actually returned data this cycle.</summary>
    public required IReadOnlyList<StreamPlatform> HealthyPlatforms { get; init; }
}

/// <summary>
/// Fans out to every configured platform, records the results into history, and derives the
/// trending ranking.
///
/// Platform failures are captured rather than thrown: one expired Twitch token or exhausted
/// YouTube quota should degrade that column only, never blank the whole app.
/// </summary>
public sealed class TrendsService
{
    /// <summary>
    /// Hard floor on refresh rate. YouTube's default quota is 10,000 units/day and a refresh
    /// costs ~201, so anything under this can exhaust a day's quota before the day is out.
    /// </summary>
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly IReadOnlyList<IDiscoverySource> _sources;
    private readonly SnapshotStore _history;
    private readonly TrendOptions _trendOptions;

    public TrendsService(
        IReadOnlyList<IDiscoverySource> sources,
        SnapshotStore? history = null,
        TrendOptions? trendOptions = null)
    {
        _sources = sources;
        _history = history ?? new SnapshotStore();
        _trendOptions = trendOptions ?? new TrendOptions();
    }

    public static TimeSpan ResolveInterval(AppSettings settings)
    {
        var requested = TimeSpan.FromMinutes(settings.RefreshIntervalMinutes);
        return requested < MinRefreshInterval ? MinRefreshInterval : requested;
    }

    public async Task<TrendsSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var errors = new Dictionary<StreamPlatform, string>();
        var healthy = new List<StreamPlatform>();
        var all = new List<LiveStream>();

        var results = await Task.WhenAll(_sources.Select(source => RunSourceAsync(source, ct)));

        foreach (var (platform, streams, error) in results)
        {
            if (error is not null)
            {
                errors[platform] = error;
                continue;
            }

            healthy.Add(platform);
            all.AddRange(streams);
        }

        var deduped = Deduplicate(all);

        // Record before analysing: this cycle's numbers become the next cycle's baseline.
        // Only healthy platforms are written, so an outage can't be mistaken for everyone going offline.
        _history.Append(deduped, now);

        var history = _history.Load(now);
        var trending = TrendAnalyzer.Analyze(deduped, history, _trendOptions, now);

        return new TrendsSnapshot
        {
            FetchedAtUtc = now,
            TopNow = deduped.OrderByDescending(s => s.ViewerCount).ToList(),
            Trending = trending,
            Errors = errors,
            HealthyPlatforms = healthy,
        };
    }

    /// <summary>
    /// A watchlisted channel that also ranks high enough for discovery arrives twice. Collapse to
    /// one card, keeping the tracked flag so watchlist-scoped alert rules still see it, and
    /// recording it only once in history so its baseline isn't skewed by double counting.
    /// </summary>
    internal static IReadOnlyList<LiveStream> Deduplicate(IEnumerable<LiveStream> streams) =>
        streams
            .GroupBy(s => s.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var best = group.OrderByDescending(s => s.ViewerCount).First();
                return group.Any(s => s.IsTracked) ? best with { IsTracked = true } : best;
            })
            .ToList();

    private static async Task<(StreamPlatform Platform, IReadOnlyList<LiveStream> Streams, string? Error)> RunSourceAsync(
        IDiscoverySource source, CancellationToken ct)
    {
        try
        {
            var streams = await source.DiscoverAsync(ct);
            return (source.Platform, streams, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (source.Platform, [], Describe(source.Platform, ex));
        }
    }

    /// <summary>Turns raw API exceptions into something a streamer can act on without reading a stack trace.</summary>
    private static string Describe(StreamPlatform platform, Exception ex)
    {
        var message = ex.Message;

        if (platform == StreamPlatform.YouTube && message.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return "YouTube daily quota exhausted — resets at midnight Pacific. Try a longer refresh interval.";

        if (platform == StreamPlatform.Twitch && (message.Contains("401") || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)))
            return "Twitch token rejected — reconnect on the Sources tab.";

        return $"{platform} refresh failed: {message}";
    }
}
