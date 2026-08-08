using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Tests.Models;

public class PlatformPresetsTests
{
    [Theory]
    [InlineData(StreamingPlatform.Twitch, "rtmp://live.twitch.tv/app")]
    [InlineData(StreamingPlatform.YouTube, "rtmp://a.rtmp.youtube.com/live2")]
    public void DefaultServerUrl_ReturnsKnownIngestUrl(StreamingPlatform platform, string expected)
    {
        Assert.Equal(expected, PlatformPresets.DefaultServerUrl(platform));
    }

    [Theory]
    [InlineData(StreamingPlatform.Kick)]
    [InlineData(StreamingPlatform.X)]
    [InlineData(StreamingPlatform.Custom)]
    public void DefaultServerUrl_ReturnsNullWhenNoFixedIngestExists(StreamingPlatform platform)
    {
        Assert.Null(PlatformPresets.DefaultServerUrl(platform));
    }

    [Theory]
    [InlineData(StreamingPlatform.Kick)]
    [InlineData(StreamingPlatform.X)]
    public void SetupHint_ProvidedForAccountSpecificPlatforms(StreamingPlatform platform)
    {
        Assert.False(string.IsNullOrWhiteSpace(PlatformPresets.SetupHint(platform)));
    }

    [Theory]
    [InlineData(StreamingPlatform.Twitch)]
    [InlineData(StreamingPlatform.YouTube)]
    [InlineData(StreamingPlatform.Custom)]
    public void SetupHint_NullWhenNoGuidanceNeeded(StreamingPlatform platform)
    {
        Assert.Null(PlatformPresets.SetupHint(platform));
    }
}
