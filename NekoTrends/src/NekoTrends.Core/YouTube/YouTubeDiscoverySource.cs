using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using NekoTrends.Core.Discovery;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.YouTube;

/// <summary>
/// Finds live VTubers on YouTube via keyword search over active broadcasts.
///
/// Quota is the whole design constraint here. A <c>search.list</c> call costs 100 units against
/// the default 10,000/day, while <c>videos.list</c> costs 1 unit for up to 50 ids. So the shape
/// is: as few searches as possible, then a single batched videos.list to get concurrent viewer
/// counts (search results don't include them). At the default 2 search terms per refresh that's
/// ~201 units per cycle — roughly 45 refreshes a day, which is why the refresh interval floor
/// exists in <see cref="TrendsService"/>.
///
/// Auth is a plain API key rather than OAuth: these are public reads, so unlike NekoChat — which
/// needs OAuth to reach the user's own live chat — there's no consent screen or client secret.
/// </summary>
public sealed class YouTubeDiscoverySource : IDiscoverySource
{
    private readonly YouTubeService _service;
    private readonly IReadOnlyList<string> _terms;
    private readonly int _termsPerRefresh;
    private int _termOffset;

    public StreamPlatform Platform => StreamPlatform.YouTube;

    /// <param name="extraTerms">
    /// Terms from keyword rules the user opted into searching. These are appended to the built-in
    /// VTuber terms and share the same per-refresh rotation, so enabling a rule widens coverage
    /// over time rather than multiplying the quota cost of any single refresh.
    /// </param>
    public YouTubeDiscoverySource(string apiKey, int termsPerRefresh = 2, IReadOnlyList<string>? extraTerms = null)
    {
        _service = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = apiKey,
            ApplicationName = "NekoTrends",
        });

        _terms = extraTerms is { Count: > 0 }
            ? VTuberClassifier.SearchTerms.Concat(extraTerms.Select(t => t.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : VTuberClassifier.SearchTerms;

        _termsPerRefresh = Math.Clamp(termsPerRefresh, 1, _terms.Count);
    }

    public async Task<IReadOnlyList<LiveStream>> DiscoverAsync(CancellationToken ct = default)
    {
        var videoIds = new HashSet<string>(StringComparer.Ordinal);

        // Rotate which terms run each refresh so all of them get covered over time
        // without paying for every term on every cycle.
        for (var i = 0; i < _termsPerRefresh; i++)
        {
            ct.ThrowIfCancellationRequested();

            var term = _terms[(_termOffset + i) % _terms.Count];

            var search = _service.Search.List("snippet");
            search.Q = term;
            search.Type = "video";
            search.EventType = SearchResource.ListRequest.EventTypeEnum.Live;
            search.Order = SearchResource.ListRequest.OrderEnum.ViewCount;
            search.MaxResults = 50;

            var searchResponse = await search.ExecuteAsync(ct);
            foreach (var item in searchResponse.Items)
            {
                if (item.Id?.VideoId is { Length: > 0 } id)
                    videoIds.Add(id);
            }
        }

        _termOffset = (_termOffset + _termsPerRefresh) % _terms.Count;

        if (videoIds.Count == 0)
            return [];

        return await FetchViewerCountsAsync(videoIds, ct);
    }

    private async Task<IReadOnlyList<LiveStream>> FetchViewerCountsAsync(
        IReadOnlyCollection<string> videoIds, CancellationToken ct)
    {
        var streams = new List<LiveStream>();

        foreach (var batch in videoIds.Chunk(50))
        {
            ct.ThrowIfCancellationRequested();

            var request = _service.Videos.List("snippet,liveStreamingDetails");
            request.Id = string.Join(',', batch);
            request.MaxResults = 50;

            var response = await request.ExecuteAsync(ct);

            foreach (var video in response.Items)
            {
                var details = video.LiveStreamingDetails;
                var snippet = video.Snippet;
                if (details is null || snippet is null)
                    continue;

                // A video can drop out of "live" between the search and this call.
                if (details.ActualEndTimeDateTimeOffset is not null || details.ConcurrentViewers is null)
                    continue;

                // Search matched on the query, not on the channel actually being a VTuber —
                // a clips or reaction channel talking *about* VTubers ranks just as well.
                if (!VTuberClassifier.LooksLikeVTuber(snippet.Title, snippet.Description, snippet.ChannelTitle))
                    continue;

                streams.Add(new LiveStream
                {
                    Platform = StreamPlatform.YouTube,
                    ChannelId = snippet.ChannelId,
                    DisplayName = snippet.ChannelTitle,
                    Title = snippet.Title,
                    Category = null,
                    Language = snippet.DefaultAudioLanguage,
                    ViewerCount = (int)Math.Min(details.ConcurrentViewers.Value, int.MaxValue),
                    ThumbnailUrl = snippet.Thumbnails?.Medium?.Url ?? snippet.Thumbnails?.Default__?.Url,
                    StartedAtUtc = details.ActualStartTimeDateTimeOffset?.ToUniversalTime(),
                    Tags = [],
                    Url = $"https://www.youtube.com/watch?v={video.Id}",
                });
            }
        }

        // One channel can run several concurrent broadcasts; keep only its biggest.
        return streams
            .GroupBy(s => s.ChannelId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(s => s.ViewerCount).First())
            .OrderByDescending(s => s.ViewerCount)
            .ToList();
    }
}
