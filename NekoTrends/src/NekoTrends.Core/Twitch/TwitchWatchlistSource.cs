using NekoTrends.Core.Discovery;
using NekoTrends.Core.Models;
using TwitchLib.Api;

namespace NekoTrends.Core.Twitch;

/// <summary>
/// Polls specific Twitch channels by login name, regardless of where they rank.
///
/// This is the answer to the scan floor in <see cref="TwitchDiscoverySource"/>: tag discovery only
/// sees the top ~1,000 live channels, so a smaller VTuber is invisible to it no matter how well
/// tagged. Helix's Get Streams accepts up to 100 user_login values per call, so an entire
/// watchlist costs one request per 100 channels — far cheaper than the discovery scan.
/// </summary>
public sealed class TwitchWatchlistSource : IDiscoverySource
{
    private readonly TwitchAPI _api;
    private readonly IReadOnlyList<string> _logins;

    public StreamPlatform Platform => StreamPlatform.Twitch;

    public TwitchWatchlistSource(string clientId, string accessToken, IReadOnlyList<string> logins)
    {
        _api = new TwitchAPI();
        _api.Settings.ClientId = clientId;
        _api.Settings.AccessToken = accessToken;
        _logins = logins;
    }

    public async Task<IReadOnlyList<LiveStream>> DiscoverAsync(CancellationToken ct = default)
    {
        if (_logins.Count == 0)
            return [];

        var found = new List<LiveStream>();

        foreach (var batch in _logins.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();

            // Offline channels are simply absent from the response, so there's nothing to filter out.
            var response = await _api.Helix.Streams.GetStreamsAsync(first: 100, userLogins: batch.ToList());

            foreach (var stream in response.Streams)
            {
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
                    IsTracked = true,
                });
            }
        }

        return found.OrderByDescending(s => s.ViewerCount).ToList();
    }
}
