using NekoTrends.Core.History;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

public class SnapshotCacheTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nekotrends-cache-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var path in new[] { _path, _path + ".tmp" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static LiveStream Stream(string id, int viewers, bool tracked = false) => new()
    {
        Platform = StreamPlatform.Twitch,
        ChannelId = id,
        DisplayName = id,
        ViewerCount = viewers,
        IsTracked = tracked,
        Title = "a title",
    };

    private static TrendEntry Trend(LiveStream stream, double ratio) => new()
    {
        Stream = stream,
        MomentumRatio = ratio,
    };

    [Fact]
    public void Round_trips_the_feed_for_instant_display()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new SnapshotCache(_path);

        cache.Save([Stream("a", 500, tracked: true)], [], 2.0, now);

        var (streams, fetchedAt, _) = cache.Load();

        var restored = Assert.Single(streams);
        Assert.Equal("a", restored.ChannelId);
        Assert.Equal(500, restored.ViewerCount);
        Assert.True(restored.IsTracked);
        Assert.Equal("a title", restored.Title);
        Assert.Equal(now.ToUnixTimeSeconds(), fetchedAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Cold_start_returns_empty_state()
    {
        var cache = new SnapshotCache(_path);

        var (streams, fetchedAt, previous) = cache.Load();

        Assert.Empty(streams);
        Assert.Null(fetchedAt);
        Assert.True(previous.IsEmpty);
    }

    /// <summary>
    /// The cache is what lets alerts stay correct across restarts — a channel live before the app
    /// closed must not read as newly live afterwards.
    /// </summary>
    [Fact]
    public void Restores_previous_state_for_alert_edge_detection()
    {
        var cache = new SnapshotCache(_path);
        var spiking = Stream("spiker", 3000);

        cache.Save([Stream("a", 100), spiking], [Trend(spiking, 3.0)], 2.0, DateTimeOffset.UtcNow);

        var (_, _, previous) = cache.Load();

        Assert.Contains("Twitch:a", previous.LiveKeys);
        Assert.Equal(3000, previous.ViewersByKey["Twitch:spiker"]);
        Assert.Contains("Twitch:spiker", previous.SpikingKeys);
        Assert.DoesNotContain("Twitch:a", previous.SpikingKeys);
    }

    [Fact]
    public void Corrupt_cache_degrades_to_a_cold_start()
    {
        File.WriteAllText(_path, "{ this is not json");

        var (streams, fetchedAt, previous) = new SnapshotCache(_path).Load();

        Assert.Empty(streams);
        Assert.Null(fetchedAt);
        Assert.True(previous.IsEmpty);
    }

    [Fact]
    public void Saving_replaces_the_previous_entry_and_leaves_no_temp_file()
    {
        var cache = new SnapshotCache(_path);

        cache.Save([Stream("old", 1)], [], 2.0, DateTimeOffset.UtcNow);
        cache.Save([Stream("new", 2)], [], 2.0, DateTimeOffset.UtcNow);

        var (streams, _, _) = cache.Load();

        Assert.Equal("new", Assert.Single(streams).ChannelId);
        Assert.False(File.Exists(_path + ".tmp"));
    }
}
