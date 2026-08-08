using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Tests.Models;

public class SegmentInfoTests
{
    [Fact]
    public void ReleaseAtUtc_AddsDelayToDiscoveryTime()
    {
        var discoveredAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var segment = new SegmentInfo("seg_001.ts", discoveredAt);

        var releaseAt = segment.ReleaseAtUtc(TimeSpan.FromSeconds(30));

        Assert.Equal(discoveredAt + TimeSpan.FromSeconds(30), releaseAt);
    }
}
