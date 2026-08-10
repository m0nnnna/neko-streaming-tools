using NekoTrends.Core.Alerts;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

public class AlertEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static LiveStream Stream(string id, int viewers = 100, bool tracked = true) => new()
    {
        Platform = StreamPlatform.Twitch,
        ChannelId = id,
        DisplayName = id,
        ViewerCount = viewers,
        IsTracked = tracked,
    };

    private static AlertEngine.PreviousState Previous(params LiveStream[] streams) =>
        AlertEngine.PreviousState.From(streams, [], 2.0);

    private static TrendEntry Trend(LiveStream stream, double ratio) => new()
    {
        Stream = stream,
        MomentumRatio = ratio,
        BaselineViewers = (int)(stream.ViewerCount / ratio),
    };

    [Fact]
    public void Went_live_fires_only_on_the_transition()
    {
        var rule = new AlertRule { Name = "live", Trigger = AlertTrigger.WentLive };
        var stream = Stream("vt");

        // Previously offline (but the state is non-empty, so this isn't a cold start).
        var fired = AlertEngine.Evaluate([stream], [], Previous(Stream("other")), [rule], Now);
        Assert.Equal("went live", Assert.Single(fired).Message);

        // Still live on the next refresh — must not fire again.
        var again = AlertEngine.Evaluate([stream], [], Previous(stream), [rule], Now);
        Assert.Empty(again);
    }

    /// <summary>
    /// On a cold start everything looks "newly live". Firing then would bury the user in alerts
    /// for channels that were already streaming.
    /// </summary>
    [Fact]
    public void Went_live_stays_silent_on_a_cold_start()
    {
        var rule = new AlertRule { Name = "live", Trigger = AlertTrigger.WentLive };

        var fired = AlertEngine.Evaluate(
            [Stream("a"), Stream("b")], [], AlertEngine.PreviousState.Empty, [rule], Now);

        Assert.Empty(fired);
    }

    [Fact]
    public void Viewer_threshold_fires_on_the_upward_crossing_only()
    {
        var rule = new AlertRule { Name = "big", Trigger = AlertTrigger.ViewerThreshold, Threshold = 1000 };

        var crossing = AlertEngine.Evaluate(
            [Stream("vt", 1200)], [], Previous(Stream("vt", 800)), [rule], Now);
        Assert.Equal("passed 1,000 viewers", Assert.Single(crossing).Message);

        var stillAbove = AlertEngine.Evaluate(
            [Stream("vt", 1300)], [], Previous(Stream("vt", 1200)), [rule], Now);
        Assert.Empty(stillAbove);
    }

    [Fact]
    public void Viewer_threshold_ignores_streams_below_the_line()
    {
        var rule = new AlertRule { Name = "big", Trigger = AlertTrigger.ViewerThreshold, Threshold = 1000 };

        var fired = AlertEngine.Evaluate([Stream("vt", 400)], [], Previous(Stream("vt", 300)), [rule], Now);

        Assert.Empty(fired);
    }

    [Fact]
    public void Momentum_spike_fires_once_while_the_spike_persists()
    {
        var rule = new AlertRule { Name = "spike", Trigger = AlertTrigger.MomentumSpike, Threshold = 2.0 };
        var stream = Stream("vt", 3000);
        var trending = new[] { Trend(stream, 3.0) };

        var first = AlertEngine.Evaluate([stream], trending, Previous(stream), [rule], Now);
        Assert.Contains("3.0×", Assert.Single(first).Message);

        var previousWithSpike = AlertEngine.PreviousState.From([stream], trending, 2.0);
        var second = AlertEngine.Evaluate([stream], trending, previousWithSpike, [rule], Now);
        Assert.Empty(second);
    }

    [Fact]
    public void Untracked_streams_are_skipped_when_the_rule_is_watchlist_scoped()
    {
        var rule = new AlertRule { Name = "live", Trigger = AlertTrigger.WentLive, TrackedChannelsOnly = true };

        var fired = AlertEngine.Evaluate(
            [Stream("vt", tracked: false)], [], Previous(Stream("other")), [rule], Now);

        Assert.Empty(fired);
    }

    [Fact]
    public void Unscoped_rules_also_see_untracked_streams()
    {
        var rule = new AlertRule { Name = "live", Trigger = AlertTrigger.WentLive, TrackedChannelsOnly = false };

        var fired = AlertEngine.Evaluate(
            [Stream("vt", tracked: false)], [], Previous(Stream("other")), [rule], Now);

        Assert.Single(fired);
    }

    [Fact]
    public void Disabled_rules_never_fire()
    {
        var rule = new AlertRule { Name = "live", Trigger = AlertTrigger.WentLive, Enabled = false };

        Assert.Empty(AlertEngine.Evaluate([Stream("vt")], [], Previous(Stream("other")), [rule], Now));
    }

    [Fact]
    public void Rule_filter_restricts_to_matching_keyword_rules()
    {
        var rule = new AlertRule { Name = "live", Trigger = AlertTrigger.WentLive, RuleFilter = "Hololive" };

        var unmatched = Stream("vt") with { MatchedRules = ["Nijisanji"] };
        Assert.Empty(AlertEngine.Evaluate([unmatched], [], Previous(Stream("x")), [rule], Now));

        var matched = Stream("vt") with { MatchedRules = ["Hololive"] };
        Assert.Single(AlertEngine.Evaluate([matched], [], Previous(Stream("x")), [rule], Now));
    }
}
