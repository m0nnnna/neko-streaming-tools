using NekoTrends.Core.History;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

public class TrendAnalyzerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static LiveStream Stream(string id, int viewers, StreamPlatform platform = StreamPlatform.Twitch) => new()
    {
        Platform = platform,
        ChannelId = id,
        DisplayName = id,
        ViewerCount = viewers,
    };

    /// <summary>Samples spaced a day apart, all safely outside the self-exclusion window.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<ViewerSample>> History(
        string key, StreamPlatform platform, string channelId, params int[] viewerCounts)
    {
        var samples = viewerCounts
            .Select((v, i) => new ViewerSample
            {
                TimestampUtc = Now.AddDays(-(viewerCounts.Length - i)),
                Platform = platform,
                ChannelId = channelId,
                ViewerCount = v,
            })
            .ToList();

        return new Dictionary<string, IReadOnlyList<ViewerSample>> { [key] = samples };
    }

    [Fact]
    public void Surging_channel_is_ranked_with_its_multiple()
    {
        var history = History("Twitch:vt", StreamPlatform.Twitch, "vt", 1000, 1000, 1000, 1000);

        var result = TrendAnalyzer.Analyze([Stream("vt", 3000)], history, nowUtc: Now);

        var entry = Assert.Single(result);
        Assert.Equal(1000, entry.BaselineViewers);
        Assert.Equal(3.0, entry.MomentumRatio!.Value, 3);
        Assert.False(entry.IsNewcomer);
        Assert.Equal("3.0× their usual", entry.Headline);
    }

    [Fact]
    public void Channel_at_its_normal_size_is_not_trending()
    {
        var history = History("Twitch:vt", StreamPlatform.Twitch, "vt", 1000, 1050, 980, 1010);

        var result = TrendAnalyzer.Analyze([Stream("vt", 1020)], history, nowUtc: Now);

        Assert.Empty(result);
    }

    [Fact]
    public void Channel_with_no_history_is_flagged_as_a_newcomer()
    {
        var result = TrendAnalyzer.Analyze(
            [Stream("debut", 2000)],
            new Dictionary<string, IReadOnlyList<ViewerSample>>(),
            nowUtc: Now);

        var entry = Assert.Single(result);
        Assert.True(entry.IsNewcomer);
        Assert.Null(entry.BaselineViewers);
        Assert.Equal("New to the feed", entry.Headline);
    }

    [Fact]
    public void Streams_below_the_viewer_floor_are_excluded()
    {
        var history = History("Twitch:tiny", StreamPlatform.Twitch, "tiny", 2, 2, 2, 2);

        var result = TrendAnalyzer.Analyze(
            [Stream("tiny", 40)],
            history,
            new TrendOptions { MinViewers = 50 },
            Now);

        Assert.Empty(result);
    }

    /// <summary>
    /// The self-exclusion window is what stops a long-running spike from quietly becoming its own
    /// baseline. Without it this channel would look normal instead of 4x up.
    /// </summary>
    [Fact]
    public void Recent_samples_do_not_inflate_the_baseline()
    {
        var samples = new List<ViewerSample>();
        foreach (var (offset, viewers) in new[]
                 {
                     (TimeSpan.FromDays(3), 500),
                     (TimeSpan.FromDays(2), 500),
                     (TimeSpan.FromDays(1), 500),
                     (TimeSpan.FromMinutes(30), 2000),
                     (TimeSpan.FromMinutes(15), 2000),
                 })
        {
            samples.Add(new ViewerSample
            {
                TimestampUtc = Now - offset,
                Platform = StreamPlatform.Twitch,
                ChannelId = "vt",
                ViewerCount = viewers,
            });
        }

        var history = new Dictionary<string, IReadOnlyList<ViewerSample>> { ["Twitch:vt"] = samples };

        var entry = Assert.Single(TrendAnalyzer.Analyze([Stream("vt", 2000)], history, nowUtc: Now));

        Assert.Equal(500, entry.BaselineViewers);
        Assert.Equal(4.0, entry.MomentumRatio!.Value, 3);
    }

    /// <summary>
    /// Ranking multiplies momentum by audience size on purpose — a 3x on a large channel is more
    /// newsworthy than the same 3x on a tiny one, and a pure-ratio sort would invert this.
    /// </summary>
    [Fact]
    public void Larger_audience_outranks_equal_momentum_on_a_small_one()
    {
        var history = new Dictionary<string, IReadOnlyList<ViewerSample>>
        {
            ["Twitch:big"] = History("Twitch:big", StreamPlatform.Twitch, "big", 4000, 4000, 4000)["Twitch:big"],
            ["Twitch:small"] = History("Twitch:small", StreamPlatform.Twitch, "small", 100, 100, 100)["Twitch:small"],
        };

        var result = TrendAnalyzer.Analyze(
            [Stream("small", 300), Stream("big", 12000)],
            history,
            nowUtc: Now);

        Assert.Equal("big", result[0].Stream.ChannelId);
        Assert.Equal("small", result[1].Stream.ChannelId);
    }

    [Fact]
    public void Too_few_samples_counts_as_a_newcomer_rather_than_a_wild_ratio()
    {
        var history = History("Twitch:vt", StreamPlatform.Twitch, "vt", 10);

        var entry = Assert.Single(TrendAnalyzer.Analyze([Stream("vt", 5000)], history, nowUtc: Now));

        Assert.True(entry.IsNewcomer);
        Assert.Null(entry.MomentumRatio);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 }, 2)]
    [InlineData(new[] { 1, 2, 3, 4 }, 3)]
    [InlineData(new[] { 5 }, 5)]
    public void Median_handles_odd_and_even_counts(int[] values, int expected) =>
        Assert.Equal(expected, TrendAnalyzer.Median(values));

    [Fact]
    public void Platforms_are_kept_distinct_by_key()
    {
        // Same channel id on two platforms must not share a baseline.
        var history = History("Twitch:name", StreamPlatform.Twitch, "name", 1000, 1000, 1000);

        var result = TrendAnalyzer.Analyze(
            [Stream("name", 3000), Stream("name", 3000, StreamPlatform.Kick)],
            history,
            nowUtc: Now);

        Assert.Equal(2, result.Count);
        Assert.Single(result, e => e.Stream.Platform == StreamPlatform.Twitch && !e.IsNewcomer);
        Assert.Single(result, e => e.Stream.Platform == StreamPlatform.Kick && e.IsNewcomer);
    }
}
