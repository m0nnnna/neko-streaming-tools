using System.Text.Json;
using NekoTrends.Core.Kick;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Tests;

/// <summary>
/// Payloads here mirror the real shape returned by kick.com/api/v2/channels/{slug}, including its
/// quirks: livestream is null (not absent) when offline, and start_time carries no timezone.
/// </summary>
public class KickDiscoverySourceTests
{
    private static LiveStream? Parse(string json, string slug = "somevtuber")
    {
        using var doc = JsonDocument.Parse(json);
        return KickDiscoverySource.ParseChannel(doc.RootElement, slug);
    }

    [Fact]
    public void Parses_a_live_channel()
    {
        var stream = Parse("""
        {
          "slug": "somevtuber",
          "followers_count": 1234,
          "user": { "username": "SomeVTuber", "bio": "indie vtuber" },
          "livestream": {
            "is_live": true,
            "viewer_count": 4200,
            "session_title": "singing stream",
            "start_time": "2026-08-10 15:00:30",
            "lang_iso": "en",
            "categories": [ { "name": "Just Chatting" } ]
          }
        }
        """);

        Assert.NotNull(stream);
        Assert.Equal(StreamPlatform.Kick, stream.Platform);
        Assert.Equal("somevtuber", stream.ChannelId);
        Assert.Equal("SomeVTuber", stream.DisplayName);
        Assert.Equal(4200, stream.ViewerCount);
        Assert.Equal("singing stream", stream.Title);
        Assert.Equal("Just Chatting", stream.Category);
        Assert.Equal("en", stream.Language);
        Assert.Equal("https://kick.com/somevtuber", stream.Url);
    }

    [Fact]
    public void Treats_kick_timestamps_as_utc()
    {
        var stream = Parse("""
        {
          "user": { "username": "X" },
          "livestream": { "is_live": true, "viewer_count": 10, "start_time": "2026-08-10 15:00:30" }
        }
        """);

        Assert.Equal(new DateTimeOffset(2026, 8, 10, 15, 0, 30, TimeSpan.Zero), stream!.StartedAtUtc);
    }

    [Fact]
    public void Offline_channel_yields_nothing() =>
        Assert.Null(Parse("""{ "slug": "x", "user": { "username": "X" }, "livestream": null }"""));

    [Fact]
    public void Explicitly_not_live_yields_nothing() =>
        Assert.Null(Parse("""{ "user": { "username": "X" }, "livestream": { "is_live": false, "viewer_count": 0 } }"""));

    [Fact]
    public void Missing_livestream_property_yields_nothing() =>
        Assert.Null(Parse("""{ "slug": "x", "user": { "username": "X" } }"""));

    [Fact]
    public void Falls_back_to_the_slug_when_username_is_absent()
    {
        var stream = Parse("""{ "livestream": { "is_live": true, "viewer_count": 5 } }""", "fallback-slug");

        Assert.Equal("fallback-slug", stream!.DisplayName);
    }

    [Fact]
    public void Tolerates_missing_optional_fields()
    {
        var stream = Parse("""{ "user": { "username": "X" }, "livestream": { "is_live": true } }""");

        Assert.NotNull(stream);
        Assert.Equal(0, stream.ViewerCount);
        Assert.Null(stream.Title);
        Assert.Null(stream.Category);
        Assert.Null(stream.StartedAtUtc);
    }

    /// <summary>The channel endpoint frequently returns thumbnail: null even while live.</summary>
    [Fact]
    public void Handles_null_thumbnail()
    {
        var stream = Parse("""
        { "user": { "username": "X" }, "livestream": { "is_live": true, "viewer_count": 9, "thumbnail": null } }
        """);

        Assert.Null(stream!.ThumbnailUrl);
    }

    [Fact]
    public void Skips_categories_without_a_name()
    {
        var stream = Parse("""
        {
          "user": { "username": "X" },
          "livestream": { "is_live": true, "viewer_count": 9, "categories": [ { "id": 1 }, { "name": "IRL" } ] }
        }
        """);

        Assert.Equal("IRL", stream!.Category);
    }
}
