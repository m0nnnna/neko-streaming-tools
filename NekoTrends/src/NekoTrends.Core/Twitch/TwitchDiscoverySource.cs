using NekoTrends.Core.Discovery;
using NekoTrends.Core.Models;
using TwitchLib.Api;

namespace NekoTrends.Core.Twitch;

/// <summary>
/// Finds live VTubers on Twitch by scanning the global top-streams list and keeping the ones
/// carrying a VTuber tag.
///
/// Helix has no server-side tag filter — the old <c>tag_ids</c> parameter on Get Streams was
/// removed when Twitch moved to freeform tags — so client-side filtering over paginated top
/// streams is the only route. At 100 streams per page the default 10 pages covers roughly the
/// top 1,000 live channels for ~10 requests, comfortably inside Helix's 800-points/minute budget.
/// The tradeoff is a viewer floor: a VTuber below the top 1,000 is invisible here.
/// </summary>
public sealed class TwitchDiscoverySource : IDiscoverySource
{
    private readonly TwitchAPI _api;
    private readonly int _pagesToScan;

    public StreamPlatform Platform => StreamPlatform.Twitch;

    public TwitchDiscoverySource(string clientId, string accessToken, int pagesToScan = 10)
    {
        _api = new TwitchAPI();
        _api.Settings.ClientId = clientId;
        _api.Settings.AccessToken = accessToken;
        _pagesToScan = Math.Max(pagesToScan, 1);
    }

    public async Task<IReadOnlyList<LiveStream>> DiscoverAsync(CancellationToken ct = default)
    {
        var found = new List<LiveStream>();
        string? cursor = null;

        for (var page = 0; page < _pagesToScan; page++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _api.Helix.Streams.GetStreamsAsync(first: 100, after: cursor);
            if (response.Streams.Length == 0)
                break;

            foreach (var stream in response.Streams)
            {
                if (!VTuberClassifier.HasVTuberTag(stream.Tags))
                    continue;

                found.Add(new LiveStream
                {
                    Platform = StreamPlatform.Twitch,
                    ChannelId = stream.UserId,
                    DisplayName = stream.UserName,
                    Title = stream.Title,
                    Category = stream.GameName,
                    Language = stream.Language,
                    ViewerCount = stream.ViewerCount,
                    ThumbnailUrl = stream.ThumbnailUrl?.Replace("{width}", "440").Replace("{height}", "248"),
                    StartedAtUtc = stream.StartedAt.ToUniversalTime(),
                    Tags = stream.Tags ?? [],
                    Url = $"https://twitch.tv/{stream.UserLogin}",
                });
            }

            cursor = response.Pagination?.Cursor;
            if (string.IsNullOrEmpty(cursor))
                break;
        }

        return found.OrderByDescending(s => s.ViewerCount).ToList();
    }
}
