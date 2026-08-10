using NekoTrends.Core.Discovery;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

public class RuleEngineTests
{
    private static LiveStream Stream(
        string name = "chan", string? title = null, string? category = null, params string[] tags) => new()
    {
        Platform = StreamPlatform.Twitch,
        ChannelId = name,
        DisplayName = name,
        Title = title,
        Category = category,
        Tags = tags,
        ViewerCount = 100,
    };

    [Fact]
    public void Stamps_matching_streams_with_the_rule_name()
    {
        var rule = new KeywordRule { Name = "Hololive", Terms = ["hololive"] };

        var result = RuleEngine.Apply([Stream(title: "hololive EN karaoke")], [rule]);

        Assert.Equal(["Hololive"], Assert.Single(result).MatchedRules);
    }

    [Fact]
    public void Leaves_non_matching_streams_untouched()
    {
        var rule = new KeywordRule { Name = "Hololive", Terms = ["hololive"] };

        var result = RuleEngine.Apply([Stream(title: "just chatting")], [rule]);

        Assert.Empty(Assert.Single(result).MatchedRules);
    }

    [Theory]
    [InlineData("minecraft", null, "Minecraft")]
    [InlineData("apex", "playing apex tonight", null)]
    public void Matches_across_title_and_category(string term, string? title, string? category)
    {
        var rule = new KeywordRule { Name = "Games", Terms = [term] };

        var result = RuleEngine.Apply([Stream(title: title, category: category)], [rule]);

        Assert.Single(Assert.Single(result).MatchedRules);
    }

    [Fact]
    public void Matches_tags()
    {
        var rule = new KeywordRule { Name = "JP", Terms = ["japanese"] };

        var result = RuleEngine.Apply([Stream(tags: "Japanese")], [rule]);

        Assert.Single(Assert.Single(result).MatchedRules);
    }

    [Fact]
    public void A_stream_can_match_several_rules()
    {
        var rules = new List<KeywordRule>
        {
            new() { Name = "Hololive", Terms = ["hololive"] },
            new() { Name = "Minecraft", Terms = ["minecraft"] },
        };

        var result = RuleEngine.Apply([Stream(title: "hololive minecraft build")], rules);

        Assert.Equal(["Hololive", "Minecraft"], Assert.Single(result).MatchedRules);
    }

    [Fact]
    public void Disabled_and_empty_rules_are_ignored()
    {
        var rules = new List<KeywordRule>
        {
            new() { Name = "Off", Terms = ["hololive"], Enabled = false },
            new() { Name = "Empty", Terms = [] },
        };

        var result = RuleEngine.Apply([Stream(title: "hololive")], rules);

        Assert.Empty(Assert.Single(result).MatchedRules);
    }

    /// <summary>
    /// Unlike VTuber detection, user-authored terms match as plain substrings — someone typing
    /// "holo" means to catch "hololive".
    /// </summary>
    [Fact]
    public void Matching_is_substring_based_and_case_insensitive()
    {
        var rule = new KeywordRule { Name = "Holo", Terms = ["HOLO"] };

        var result = RuleEngine.Apply([Stream(title: "hololive stream")], [rule]);

        Assert.Single(Assert.Single(result).MatchedRules);
    }

    [Fact]
    public void No_rules_returns_the_input_unchanged()
    {
        var streams = new[] { Stream() };

        Assert.Same(streams, RuleEngine.Apply(streams, []));
    }
}
