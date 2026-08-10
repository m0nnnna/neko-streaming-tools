using NekoTrends.Core.Discovery;

namespace NekoTrends.Core.Tests;

public class VTuberClassifierTests
{
    [Theory]
    [InlineData("VTuber")]
    [InlineData("vtuber")]
    [InlineData("V-Tuber")]
    [InlineData("Virtual YouTuber")]
    [InlineData("virtual streamer")]
    [InlineData("Live2D")]
    [InlineData("バーチャルYouTuber")]
    [InlineData("버튜버")]
    public void Recognises_vtuber_tags(string tag) =>
        Assert.True(VTuberClassifier.HasVTuberTag([tag]));

    [Theory]
    [InlineData("Speedrun")]
    [InlineData("English")]
    [InlineData("Just Chatting")]
    public void Ignores_unrelated_tags(string tag) =>
        Assert.False(VTuberClassifier.HasVTuberTag([tag]));

    /// <summary>
    /// Kick's search is a bare substring match, so these are real results it returns for VTuber
    /// queries. Word-boundary matching is what keeps them out of the roster suggestions.
    /// </summary>
    [Theory]
    [InlineData("live2dance4ever")]
    [InlineData("vtubercypress")]
    [InlineData("virtuallifechannel")]
    public void Rejects_substring_false_positives_from_kick_search(string channelName) =>
        Assert.False(VTuberClassifier.LooksLikeVTuber(channelName));

    [Theory]
    [InlineData("kai vtuber")]
    [InlineData("Misha | VTuber")]
    [InlineData("your local virtual youtuber")]
    public void Accepts_names_where_the_term_stands_alone(string channelName) =>
        Assert.True(VTuberClassifier.LooksLikeVTuber(channelName));

    /// <summary>
    /// Gluing the term onto a preceding word is a very common channel-naming style, so only the
    /// trailing boundary is enforced. These are real slugs returned by Kick's search.
    /// </summary>
    [Theory]
    [InlineData("starvtuber")]
    [InlineData("tsubakisoulvtuber")]
    [InlineData("eu_vtuber")]
    [InlineData("babumiruu-vtuber")]
    public void Accepts_the_term_as_a_name_suffix(string channelName) =>
        Assert.True(VTuberClassifier.LooksLikeVTuber(channelName));

    [Fact]
    public void Handles_null_and_empty_input()
    {
        Assert.False(VTuberClassifier.HasVTuberTag(null));
        Assert.False(VTuberClassifier.HasVTuberTag([]));
        Assert.False(VTuberClassifier.LooksLikeVTuber(null, "", "   "));
    }

    [Fact]
    public void Matches_across_any_supplied_field() =>
        Assert.True(VTuberClassifier.LooksLikeVTuber("plainname", null, "indie vtuber streaming tonight"));
}
