using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Tests.Models;

public class DestinationTargetTests
{
    [Fact]
    public void FullUrl_JoinsServerAndKey()
    {
        var target = new DestinationTarget("Twitch", "rtmp://live.twitch.tv/app", "abc123");

        Assert.Equal("rtmp://live.twitch.tv/app/abc123", target.FullUrl);
    }

    [Fact]
    public void FullUrl_TrimsTrailingSlashOnServer()
    {
        var target = new DestinationTarget("Twitch", "rtmp://live.twitch.tv/app/", "abc123");

        Assert.Equal("rtmp://live.twitch.tv/app/abc123", target.FullUrl);
    }
}
