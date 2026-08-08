using KickLib.Client;
using KickLib.Client.Models.Args;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;

namespace NekoChat.Core.Kick;

/// <summary>
/// Watches a Kick channel's chat via Kick's unofficial Pusher-based websocket
/// (there's no stable public API for this — Kick's official chat.message.sent
/// event is webhook-only, impractical for a desktop app). No auth needed for
/// read-only access, just the channel's slug (e.g. "xqc").
///
/// The chatroom-id lookup goes through <see cref="KickApiClient"/> (curl.exe,
/// not HttpClient — see its doc comment) rather than KickLib's alternative
/// clients, which pull in either a complex, DI-oriented internal auth stack
/// (TlsSpoofClient) or a headless-Chrome dependency via Puppeteer (BrowserClient)
/// just for one lookup.
/// </summary>
public sealed class KickChatSource : IChatSource
{
    private readonly string _channelSlug;
    private readonly KickClient _client = new();

    public ChatPlatform Platform => ChatPlatform.Kick;

    public bool IsConnected => _client.IsConnected;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public KickChatSource(string channelSlug)
    {
        _channelSlug = channelSlug;
        _client.OnMessage += OnMessage;
        _client.OnSubscription += OnSubscription;
        _client.OnGiftedSubscription += OnGiftedSubscription;
        _client.OnFollowersUpdated += OnFollowersUpdated;
        _client.OnStreamHost += OnStreamHost;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var chatroomId = await ResolveChatroomIdAsync(ct);

        await _client.ListenToChatRoomAsync(chatroomId);
        await _client.ConnectAsync();
    }

    private async Task<int> ResolveChatroomIdAsync(CancellationToken ct)
    {
        using var doc = await KickApiClient.FetchJsonAsync($"https://kick.com/api/v2/channels/{_channelSlug}/chatroom", ct);
        if (!doc.RootElement.TryGetProperty("id", out var idProp))
            throw new InvalidOperationException($"Kick channel '{_channelSlug}' not found or has no chatroom.");

        return idProp.GetInt32();
    }

    public async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
    }

    private void OnMessage(object? sender, ChatMessageEventArgs e)
    {
        try
        {
            var data = e.Data;
            var message = new ChatMessage(
                ChatPlatform.Kick,
                data.Sender.Username,
                data.Content,
                data.CreatedAt,
                data.Sender.Identity?.Color);

            MessageReceived?.Invoke(message);
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    private void OnSubscription(object? sender, SubscriptionEventArgs e)
    {
        try
        {
            var data = e.Data;
            MessageReceived?.Invoke(new ChatMessage(
                ChatPlatform.Kick, data.Username, $"subscribed ({data.Months} month(s))", DateTimeOffset.Now, Kind: ChatMessageKind.Subscribe));
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    private void OnGiftedSubscription(object? sender, GiftedSubscriptionsEventArgs e)
    {
        try
        {
            var data = e.Data;
            MessageReceived?.Invoke(new ChatMessage(
                ChatPlatform.Kick, data.GifterUsername, $"gifted {data.Count} sub(s)", DateTimeOffset.Now, Kind: ChatMessageKind.GiftSub));
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    private void OnFollowersUpdated(object? sender, FollowersUpdatedEventArgs e)
    {
        try
        {
            var data = e.Data;
            if (!data.Followed)
                return;

            MessageReceived?.Invoke(new ChatMessage(
                ChatPlatform.Kick, data.Username, "followed", DateTimeOffset.Now, Kind: ChatMessageKind.Follow));
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    private void OnStreamHost(object? sender, StreamHostEventArgs e)
    {
        try
        {
            var data = e.Data;
            MessageReceived?.Invoke(new ChatMessage(
                ChatPlatform.Kick, data.HostUsername, $"hosted with {data.NumberViewers} viewer(s)", DateTimeOffset.Now, Kind: ChatMessageKind.Host));
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }
}
