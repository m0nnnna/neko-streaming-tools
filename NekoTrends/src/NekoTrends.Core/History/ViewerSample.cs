using NekoTrends.Core.Models;

namespace NekoTrends.Core.History;

/// <summary>A single observation of one channel's concurrent viewers at a point in time.</summary>
public sealed record ViewerSample
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required StreamPlatform Platform { get; init; }
    public required string ChannelId { get; init; }
    public required int ViewerCount { get; init; }

    public string Key => $"{Platform}:{ChannelId}";
}
