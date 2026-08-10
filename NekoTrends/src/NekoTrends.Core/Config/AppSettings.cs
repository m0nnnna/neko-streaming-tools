using NekoTrends.Core.Alerts;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Config;

public sealed class AppSettings
{
    public TwitchSettings? Twitch { get; set; }
    public YouTubeSettings? YouTube { get; set; }
    public KickSettings Kick { get; set; } = new();

    /// <summary>
    /// The watchlist: channels polled directly on every refresh regardless of ranking.
    /// This is the only mechanism that reaches Kick at all, and the only one that reaches
    /// Twitch channels below the discovery scan floor.
    /// </summary>
    public List<TrackedChannel> TrackedChannels { get; set; } = [];

    /// <summary>Saved keyword rules used to label and filter discovered streams.</summary>
    public List<KeywordRule> KeywordRules { get; set; } = [];

    public List<AlertRule> AlertRules { get; set; } = [];

    /// <summary>
    /// How often the feed refreshes. Floored at 10 minutes by <see cref="TrendsService"/> —
    /// YouTube's 10,000 unit/day quota is the binding constraint, not politeness.
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 15;

    /// <summary>Streams below this are excluded from Trending so tiny channels can't post absurd ratios.</summary>
    public int MinViewersForTrending { get; set; } = 50;

    /// <summary>Days of viewer samples to retain. Also the momentum baseline window.</summary>
    public int HistoryRetentionDays { get; set; } = 14;

    /// <summary>Show a desktop popup when an alert fires while the app is open.</summary>
    public bool ShowAlertPopups { get; set; } = true;

    public IReadOnlyList<string> TrackedIdsFor(StreamPlatform platform) =>
        TrackedChannels
            .Where(c => c.Platform == platform && !string.IsNullOrWhiteSpace(c.Id))
            .Select(c => c.Id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed class TwitchSettings
{
    public string ClientId { get; set; } = "";
    public string EncryptedAccessToken { get; set; } = "";
    public string EncryptedRefreshToken { get; set; } = "";
    public int ExpiresInSeconds { get; set; }
    public DateTimeOffset ObtainedAtUtc { get; set; }

    /// <summary>Pages of 100 top streams to scan for VTuber tags. 10 pages ≈ the top 1000 live channels.</summary>
    public int PagesToScan { get; set; } = 10;

    /// <summary>Turn off to poll only the watchlist — useful if you don't care about global discovery.</summary>
    public bool DiscoveryEnabled { get; set; } = true;
}

public sealed class YouTubeSettings
{
    /// <summary>
    /// A plain Google API key, not OAuth — YouTube's search/videos endpoints are public
    /// reads, so no consent screen or client secret is needed here (unlike NekoChat,
    /// which needs OAuth because it reads the user's own live chat).
    /// </summary>
    public string EncryptedApiKey { get; set; } = "";

    /// <summary>Search terms to run per refresh. Each costs 100 quota units of the 10,000/day default.</summary>
    public int SearchTermsPerRefresh { get; set; } = 2;

    /// <summary>
    /// Turn off to stop paying quota for global discovery while keeping the watchlist, which
    /// runs on free RSS plus ~1 unit per 50 channels.
    /// </summary>
    public bool DiscoveryEnabled { get; set; } = true;
}

public sealed class KickSettings
{
    /// <summary>Slugs the user explicitly rejected, so suggestion scans stop re-proposing them.</summary>
    public List<string> DismissedSlugs { get; set; } = [];
}
