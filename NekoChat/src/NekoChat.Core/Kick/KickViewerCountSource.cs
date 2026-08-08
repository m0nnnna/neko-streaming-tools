using NekoChat.Core.Models;
using NekoChat.Core.Viewers;

namespace NekoChat.Core.Kick;

public sealed class KickViewerCountSource : IViewerCountSource
{
    private readonly string _channelSlug;

    public ChatPlatform Platform => ChatPlatform.Kick;

    public KickViewerCountSource(string channelSlug) => _channelSlug = channelSlug;

    public async Task<int?> GetViewerCountAsync(CancellationToken ct = default)
    {
        using var doc = await KickApiClient.FetchJsonAsync($"https://kick.com/api/v2/channels/{_channelSlug}", ct);

        if (!doc.RootElement.TryGetProperty("livestream", out var livestream) || livestream.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        return livestream.TryGetProperty("viewer_count", out var vc) ? vc.GetInt32() : null;
    }
}
