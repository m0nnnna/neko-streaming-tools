using NekoTrends.Core.Alerts;
using NekoTrends.Core.Config;
using NekoTrends.Core.Discovery;
using NekoTrends.Core.History;
using NekoTrends.Core.Kick;
using NekoTrends.Core.Models;
using NekoTrends.Core.Twitch;
using NekoTrends.Core.YouTube;

namespace NekoTrends.Core;

public sealed record RefreshResult
{
    public required TrendsSnapshot Snapshot { get; init; }
    public required IReadOnlyList<FiredAlert> FiredAlerts { get; init; }
}

/// <summary>
/// Builds every configured source from settings, runs one refresh, and persists the results.
///
/// Deliberately the single entry point shared by the UI and the <c>--collect</c> background task.
/// If the collector built its sources separately the two would drift, and history gathered in the
/// background would stop matching what the app shows.
/// </summary>
public sealed class RefreshPipeline
{
    private readonly AppSettings _settings;
    private readonly SnapshotStore _history;
    private readonly SnapshotCache _cache;
    private readonly AlertLog _alertLog;

    /// <summary>Ratio at which a stream counts as "spiking" for alert edge-detection.</summary>
    private const double SpikeThreshold = 2.0;

    public RefreshPipeline(
        AppSettings settings,
        SnapshotStore? history = null,
        SnapshotCache? cache = null,
        AlertLog? alertLog = null)
    {
        _settings = settings;
        _history = history ?? new SnapshotStore(retention: TimeSpan.FromDays(settings.HistoryRetentionDays));
        _cache = cache ?? new SnapshotCache();
        _alertLog = alertLog ?? new AlertLog();
    }

    public bool HasAnySource => BuildSources().Count > 0;

    public async Task<RefreshResult> RunAsync(CancellationToken ct = default)
    {
        var sources = BuildSources();
        if (sources.Count == 0)
        {
            return new RefreshResult
            {
                Snapshot = new TrendsSnapshot
                {
                    FetchedAtUtc = DateTimeOffset.UtcNow,
                    TopNow = [],
                    Trending = [],
                    Errors = new Dictionary<StreamPlatform, string>(),
                    HealthyPlatforms = [],
                },
                FiredAlerts = [],
            };
        }

        // Read the previous cycle before overwriting it — alerts are edge-triggered against it.
        var (_, _, previous) = _cache.Load();

        var service = new TrendsService(
            sources, _history, new TrendOptions { MinViewers = _settings.MinViewersForTrending });

        var snapshot = await service.RefreshAsync(ct);

        var labelled = RuleEngine.Apply(snapshot.TopNow, _settings.KeywordRules);
        snapshot = snapshot with { TopNow = labelled };

        var fired = AlertEngine.Evaluate(
            labelled, snapshot.Trending, previous, _settings.AlertRules, snapshot.FetchedAtUtc);

        if (fired.Count > 0)
            _alertLog.Append(fired);

        _cache.Save(labelled, snapshot.Trending, SpikeThreshold, snapshot.FetchedAtUtc);

        return new RefreshResult { Snapshot = snapshot, FiredAlerts = fired };
    }

    private List<IDiscoverySource> BuildSources()
    {
        var sources = new List<IDiscoverySource>();

        if (_settings.Twitch is { } twitch && !string.IsNullOrEmpty(twitch.EncryptedAccessToken))
        {
            if (TryReadSecret(twitch.EncryptedAccessToken) is { Length: > 0 } token)
            {
                if (twitch.DiscoveryEnabled)
                    sources.Add(new TwitchDiscoverySource(twitch.ClientId, token, twitch.PagesToScan));

                var logins = _settings.TrackedIdsFor(StreamPlatform.Twitch);
                if (logins.Count > 0)
                    sources.Add(new TwitchWatchlistSource(twitch.ClientId, token, logins));
            }
        }

        if (SettingsStore.ReadYouTubeApiKey(_settings.YouTube) is { Length: > 0 } apiKey)
        {
            var youTube = _settings.YouTube!;

            if (youTube.DiscoveryEnabled)
            {
                var extraTerms = _settings.KeywordRules
                    .Where(r => r is { Enabled: true, SearchYouTube: true })
                    .SelectMany(r => r.Terms)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                sources.Add(new YouTubeDiscoverySource(apiKey, youTube.SearchTermsPerRefresh, extraTerms));
            }

            var channelIds = _settings.TrackedIdsFor(StreamPlatform.YouTube);
            if (channelIds.Count > 0)
                sources.Add(new YouTubeWatchlistSource(apiKey, channelIds));
        }

        var kickSlugs = _settings.TrackedIdsFor(StreamPlatform.Kick);
        if (kickSlugs.Count > 0)
            sources.Add(new KickDiscoverySource(kickSlugs));

        return sources;
    }

    /// <summary>DPAPI blobs are unreadable if the settings file was copied between Windows accounts.</summary>
    private static string? TryReadSecret(string encrypted)
    {
        try
        {
            return SecretProtector.Unprotect(encrypted);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
