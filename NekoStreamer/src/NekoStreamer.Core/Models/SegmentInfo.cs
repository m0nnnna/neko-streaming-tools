namespace NekoStreamer.Core.Models;

public sealed record SegmentInfo(string FilePath, DateTimeOffset DiscoveredAtUtc)
{
    public DateTimeOffset ReleaseAtUtc(TimeSpan delay) => DiscoveredAtUtc + delay;
}
