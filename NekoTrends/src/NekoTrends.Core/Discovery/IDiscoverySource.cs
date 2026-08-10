using NekoTrends.Core.Models;

namespace NekoTrends.Core.Discovery;

/// <summary>A platform's "who is live right now" feed, already filtered down to VTubers.</summary>
public interface IDiscoverySource
{
    StreamPlatform Platform { get; }

    /// <summary>
    /// Returns currently-live VTuber streams, highest viewer count first. Implementations
    /// must not throw for an unconfigured platform — return an empty list instead, so one
    /// missing API key never takes down the rest of the feed.
    /// </summary>
    Task<IReadOnlyList<LiveStream>> DiscoverAsync(CancellationToken ct = default);
}
