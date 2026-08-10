using NekoTrends.Core.History;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nekotrends-test-{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static LiveStream Stream(string id, int viewers, StreamPlatform platform = StreamPlatform.Twitch) => new()
    {
        Platform = platform,
        ChannelId = id,
        DisplayName = id,
        ViewerCount = viewers,
    };

    [Fact]
    public void Round_trips_samples()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new SnapshotStore(_path, TimeSpan.FromDays(14));

        store.Append([Stream("a", 100), Stream("b", 200, StreamPlatform.Kick)], now);

        var loaded = store.Load(now.AddSeconds(1));

        Assert.Equal(2, loaded.Count);
        Assert.Equal(100, loaded["Twitch:a"].Single().ViewerCount);
        Assert.Equal(200, loaded["Kick:b"].Single().ViewerCount);
    }

    [Fact]
    public void Appending_accumulates_across_calls()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        var store = new SnapshotStore(_path, TimeSpan.FromDays(14));

        store.Append([Stream("a", 100)], start);
        store.Append([Stream("a", 150)], start.AddHours(1));
        store.Append([Stream("a", 120)], start.AddHours(2));

        var samples = store.Load(DateTimeOffset.UtcNow)["Twitch:a"];

        Assert.Equal(3, samples.Count);
        Assert.Equal([100, 150, 120], samples.Select(s => s.ViewerCount));
    }

    [Fact]
    public void Load_drops_samples_outside_the_retention_window()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new SnapshotStore(_path, TimeSpan.FromDays(7));

        store.Append([Stream("old", 100)], now.AddDays(-30));
        store.Append([Stream("recent", 200)], now.AddDays(-1));

        var loaded = store.Load(now);

        Assert.False(loaded.ContainsKey("Twitch:old"));
        Assert.True(loaded.ContainsKey("Twitch:recent"));
    }

    /// <summary>A power loss mid-append leaves a partial final line; it must not poison the file.</summary>
    [Fact]
    public void Torn_lines_are_skipped_rather_than_throwing()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new SnapshotStore(_path, TimeSpan.FromDays(14));

        store.Append([Stream("good", 100)], now);
        File.AppendAllText(_path, "{\"t\":17280000, \"p\":0, \"c\":\"tor");

        var loaded = store.Load(now.AddSeconds(1));

        Assert.Single(loaded);
        Assert.Equal(100, loaded["Twitch:good"].Single().ViewerCount);
    }

    [Fact]
    public void Prune_rewrites_the_file_without_expired_samples()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new SnapshotStore(_path, TimeSpan.FromDays(7));

        store.Append([Stream("old", 100)], now.AddDays(-30));
        store.Append([Stream("recent", 200)], now.AddHours(-1));

        store.Prune(now);

        var remaining = File.ReadAllLines(_path).Where(l => l.Length > 0).ToList();
        Assert.Single(remaining);
        Assert.Contains("recent", remaining[0]);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Loading_a_missing_file_returns_empty()
    {
        var store = new SnapshotStore(_path, TimeSpan.FromDays(14));

        Assert.Empty(store.Load());
        Assert.Equal("no history yet", store.DescribeSize());
    }

    [Fact]
    public void Prune_on_a_missing_file_is_a_no_op()
    {
        var store = new SnapshotStore(_path, TimeSpan.FromDays(14));

        store.Prune();

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Samples_are_ordered_oldest_first_regardless_of_write_order()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new SnapshotStore(_path, TimeSpan.FromDays(14));

        store.Append([Stream("a", 300)], now.AddHours(-1));
        store.Append([Stream("a", 100)], now.AddHours(-5));

        var samples = store.Load(now)["Twitch:a"];

        Assert.Equal([100, 300], samples.Select(s => s.ViewerCount));
    }
}
