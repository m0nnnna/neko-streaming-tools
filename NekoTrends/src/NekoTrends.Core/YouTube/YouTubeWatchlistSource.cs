using System.Text.RegularExpressions;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using NekoTrends.Core.Discovery;
using NekoTrends.Core.Http;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.YouTube;

/// <summary>
/// Polls specific YouTube channels by reading each channel's public RSS feed, then batching a
/// single quota-cheap lookup for live status and viewer counts.
///
/// The obvious approach — <c>search.list</c> with a channelId filter — costs 100 quota units per
/// channel per refresh, which would exhaust the entire 10,000/day allowance on about six tracked
/// channels. The RSS feed at /feeds/videos.xml is public, needs no API key, and consumes no quota
/// at all; it lists a channel's most recent uploads including any active broadcast. Feeding those
/// video ids into <c>videos.list</c> costs 1 unit per 50 ids, so a whole watchlist is effectively
/// free. The tradeoff is that RSS lags slightly, so a stream may take a few minutes to appear.
/// </summary>
public sealed class YouTubeWatchlistSource : IDiscoverySource
{
    /// <summary>Only the newest few entries can plausibly be a live broadcast; the rest are back catalogue.</summary>
    private const int EntriesToConsiderPerChannel = 3;

    private static readonly Regex VideoIdPattern = new(
        @"<yt:videoId>([^<]+)</yt:videoId>", RegexOptions.Compiled);

    private readonly YouTubeService _service;
    private readonly IReadOnlyList<string> _channelIds;

    public StreamPlatform Platform => StreamPlatform.YouTube;

    public YouTubeWatchlistSource(string apiKey, IReadOnlyList<string> channelIds)
    {
        _service = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = apiKey,
            ApplicationName = "NekoTrends",
        });
        _channelIds = channelIds;
    }

    public async Task<IReadOnlyList<LiveStream>> DiscoverAsync(CancellationToken ct = default)
    {
        if (_channelIds.Count == 0)
            return [];

        var videoIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var channelId in _channelIds)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var videoId in await FetchRecentVideoIdsAsync(channelId, ct))
                videoIds.Add(videoId);
        }

        if (videoIds.Count == 0)
            return [];

        return await FetchLiveDetailsAsync(videoIds, ct);
    }

    private static async Task<IReadOnlyList<string>> FetchRecentVideoIdsAsync(string channelId, CancellationToken ct)
    {
        string xml;
        try
        {
            xml = await CurlHttpClient.GetStringAsync(
                $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(channelId)}", ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // A deleted or mistyped channel id must not fail the whole watchlist.
            return [];
        }

        if (string.IsNullOrWhiteSpace(xml))
            return [];

        return VideoIdPattern.Matches(xml)
            .Take(EntriesToConsiderPerChannel)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private async Task<IReadOnlyList<LiveStream>> FetchLiveDetailsAsync(
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

                // Most RSS entries are ordinary uploads with no live details at all.
                if (details is null || snippet is null)
                    continue;

                if (details.ActualEndTimeDateTimeOffset is not null || details.ConcurrentViewers is null)
                    continue;

                streams.Add(new LiveStream
                {
                    Platform = StreamPlatform.YouTube,
                    ChannelId = snippet.ChannelId,
                    DisplayName = snippet.ChannelTitle,
                    Title = snippet.Title,
                    Language = snippet.DefaultAudioLanguage,
                    ViewerCount = (int)Math.Min(details.ConcurrentViewers.Value, int.MaxValue),
                    ThumbnailUrl = snippet.Thumbnails?.Medium?.Url ?? snippet.Thumbnails?.Default__?.Url,
                    StartedAtUtc = details.ActualStartTimeDateTimeOffset?.ToUniversalTime(),
                    Tags = [],
                    Url = $"https://www.youtube.com/watch?v={video.Id}",
                    IsTracked = true,
                });
            }
        }

        return streams
            .GroupBy(s => s.ChannelId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(s => s.ViewerCount).First())
            .OrderByDescending(s => s.ViewerCount)
            .ToList();
    }
}
