namespace NekoTrends.Core.Models;

/// <summary>Why a stream is interesting right now — the "news" layer on top of a raw <see cref="LiveStream"/>.</summary>
public sealed record TrendEntry
{
    public required LiveStream Stream { get; init; }

    /// <summary>The streamer's typical concurrent viewers over the lookback window, or null when untracked so far.</summary>
    public int? BaselineViewers { get; init; }

    /// <summary>Current viewers divided by <see cref="BaselineViewers"/>. 2.0 means "double their usual".</summary>
    public double? MomentumRatio { get; init; }

    /// <summary>True when we have no prior history for this channel — they appeared out of nowhere.</summary>
    public bool IsNewcomer { get; init; }

    /// <summary>How many historical samples backed the baseline. Low counts mean a shaky ratio.</summary>
    public int SampleCount { get; init; }

    /// <summary>Ranking score for the Trending feed. Higher is more newsworthy.</summary>
    public double Score { get; init; }

    /// <summary>Short human-readable reason, e.g. "3.4x their usual" — rendered as the card's headline.</summary>
    public string Headline { get; init; } = "";
}
