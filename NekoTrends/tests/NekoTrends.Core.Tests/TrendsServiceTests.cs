using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

public class TrendsServiceTests
{
    private static LiveStream Stream(string id, int viewers, bool tracked) => new()
    {
        Platform = StreamPlatform.Twitch,
        ChannelId = id,
        DisplayName = id,
        ViewerCount = viewers,
        IsTracked = tracked,
    };

    /// <summary>
    /// A watchlisted channel big enough for discovery arrives from two sources at once. It must
    /// collapse to one card, and must only be recorded once so its own baseline isn't skewed.
    /// </summary>
    [Fact]
    public void Collapses_a_channel_returned_by_both_discovery_and_the_watchlist()
    {
        var result = TrendsService.Deduplicate([
            Stream("vt", 5000, tracked: false),
            Stream("vt", 5000, tracked: true),
        ]);

        var single = Assert.Single(result);
        Assert.True(single.IsTracked);
    }

    [Fact]
    public void Keeps_the_higher_viewer_count_when_sources_disagree()
    {
        var result = TrendsService.Deduplicate([
            Stream("vt", 4000, tracked: false),
            Stream("vt", 5200, tracked: false),
        ]);

        Assert.Equal(5200, Assert.Single(result).ViewerCount);
    }

    [Fact]
    public void Distinct_channels_are_all_preserved()
    {
        var result = TrendsService.Deduplicate([
            Stream("a", 100, tracked: false),
            Stream("b", 200, tracked: true),
        ]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Refresh_interval_is_floored_to_protect_the_youtube_quota()
    {
        var settings = new Config.AppSettings { RefreshIntervalMinutes = 1 };

        Assert.Equal(TrendsService.MinRefreshInterval, TrendsService.ResolveInterval(settings));
    }

    [Fact]
    public void Refresh_interval_above_the_floor_is_respected()
    {
        var settings = new Config.AppSettings { RefreshIntervalMinutes = 30 };

        Assert.Equal(TimeSpan.FromMinutes(30), TrendsService.ResolveInterval(settings));
    }
}
