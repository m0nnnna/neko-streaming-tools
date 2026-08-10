namespace NekoTrends.Core.Models;

/// <summary>One currently-live broadcast, normalized across all three platforms.</summary>
public sealed record LiveStream
{
    public required StreamPlatform Platform { get; init; }

    /// <summary>Stable per-platform channel identifier — Twitch user id, YouTube channel id, Kick slug.</summary>
    public required string ChannelId { get; init; }

    public required string DisplayName { get; init; }
    public string? Title { get; init; }
    public string? Category { get; init; }
    public string? Language { get; init; }
    public int ViewerCount { get; init; }
    public string? ThumbnailUrl { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Url { get; init; } = "";

    /// <summary>True when this channel is on the user's watchlist rather than found by discovery.</summary>
    public bool IsTracked { get; init; }

    /// <summary>Names of the keyword rules this stream matched, used for labelling and filtering.</summary>
    public IReadOnlyList<string> MatchedRules { get; init; } = [];

    /// <summary>Identity used to correlate this stream with its own history across refreshes.</summary>
    public string Key => $"{Platform}:{ChannelId}";

    public TimeSpan? Uptime => StartedAtUtc is { } started ? DateTimeOffset.UtcNow - started : null;
}
