using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using NekoChat.Core.Models;
using NekoChat.Core.Viewers;

namespace NekoChat.Core.YouTube;

public sealed class YouTubeViewerCountSource : IViewerCountSource
{
    private readonly YouTubeService _service;

    public ChatPlatform Platform => ChatPlatform.YouTube;

    public YouTubeViewerCountSource(UserCredential credential)
    {
        _service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "NekoChat",
        });
    }

    public async Task<int?> GetViewerCountAsync(CancellationToken ct = default)
    {
        var listRequest = _service.LiveBroadcasts.List("statistics");
        listRequest.BroadcastStatus = LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active;

        var response = await listRequest.ExecuteAsync(ct);
        var count = response.Items?.FirstOrDefault()?.Statistics?.ConcurrentViewers;
        return count.HasValue ? (int)count.Value : null;
    }
}
