using NekoChat.Core.Models;
using NekoChat.Core.Viewers;
using TwitchLib.Api;

namespace NekoChat.Core.Twitch;

public sealed class TwitchViewerCountSource : IViewerCountSource
{
    private readonly TwitchAPI _api;
    private string? _userId;

    public ChatPlatform Platform => ChatPlatform.Twitch;

    public TwitchViewerCountSource(string clientId, string accessToken)
    {
        _api = new TwitchAPI();
        _api.Settings.ClientId = clientId;
        _api.Settings.AccessToken = accessToken;
    }

    public async Task<int?> GetViewerCountAsync(CancellationToken ct = default)
    {
        if (_userId is null)
        {
            var users = await _api.Helix.Users.GetUsersAsync();
            _userId = users.Users.First().Id;
        }

        var streams = await _api.Helix.Streams.GetStreamsAsync(userIds: [_userId]);
        return streams.Streams.FirstOrDefault()?.ViewerCount;
    }
}
