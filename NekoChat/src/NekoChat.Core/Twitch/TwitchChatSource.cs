using NekoChat.Core.Models;
using NekoChat.Core.Sources;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Channel;

namespace NekoChat.Core.Twitch;

/// <summary>
/// Watches the authenticated user's own Twitch channel chat via EventSub over
/// WebSocket. Only supports watching your own channel — broadcaster_user_id and
/// user_id in the subscription condition are both the authenticated user's id.
/// </summary>
public sealed class TwitchChatSource : IChatSource
{
    private readonly TwitchAPI _api;
    private readonly EventSubWebsocketClient _client;
    private string? _userId;

    public ChatPlatform Platform => ChatPlatform.Twitch;

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public TwitchChatSource(string clientId, string accessToken)
    {
        _api = new TwitchAPI();
        _api.Settings.ClientId = clientId;
        _api.Settings.AccessToken = accessToken;

        _client = new EventSubWebsocketClient();
        _client.WebsocketConnected += OnWebsocketConnected;
        _client.WebsocketDisconnected += OnWebsocketDisconnected;
        _client.ErrorOccurred += OnErrorOccurred;
        _client.ChannelChatMessage += OnChannelChatMessage;
        _client.ChannelFollow += OnChannelFollow;
        _client.ChannelSubscribe += OnChannelSubscribe;
        _client.ChannelSubscriptionGift += OnChannelSubscriptionGift;
        _client.ChannelRaid += OnChannelRaid;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var users = await _api.Helix.Users.GetUsersAsync();
        _userId = users.Users.First().Id;

        await _client.ConnectAsync();
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        await _client.DisconnectAsync();
    }

    private async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        if (e.IsRequestedReconnect)
            return;

        var chatCondition = new Dictionary<string, string>
        {
            ["broadcaster_user_id"] = _userId!,
            ["user_id"] = _userId!,
        };
        await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
            "channel.chat.message", "1", chatCondition,
            EventSubTransportMethod.Websocket, _client.SessionId);

        var broadcasterCondition = new Dictionary<string, string> { ["broadcaster_user_id"] = _userId! };

        // Follow/subscribe/gift alerts require moderator:read:followers and
        // channel:read:subscriptions — if the stored token predates those scopes
        // (added for this feature), these subscription requests 401 individually.
        // That's reported via Faulted per-subscription rather than blocking chat,
        // since chat itself doesn't need those scopes.
        await TrySubscribeAsync("channel.follow", "2",
            new Dictionary<string, string> { ["broadcaster_user_id"] = _userId!, ["moderator_user_id"] = _userId! });
        await TrySubscribeAsync("channel.subscribe", "1", broadcasterCondition);
        await TrySubscribeAsync("channel.subscription.gift", "1", broadcasterCondition);
        await TrySubscribeAsync("channel.raid", "1",
            new Dictionary<string, string> { ["to_broadcaster_user_id"] = _userId! });

        IsConnected = true;
    }

    private async Task TrySubscribeAsync(string type, string version, Dictionary<string, string> condition)
    {
        try
        {
            await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                type, version, condition, EventSubTransportMethod.Websocket, _client.SessionId);
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(new InvalidOperationException($"Could not subscribe to Twitch {type} alerts (may need to reconnect Twitch to grant new permissions): {ex.Message}", ex));
        }
    }

    private Task OnWebsocketDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    private Task OnErrorOccurred(object? sender, ErrorOccuredArgs e)
    {
        Faulted?.Invoke(e.Exception);
        return Task.CompletedTask;
    }

    private Task OnChannelChatMessage(object? sender, ChannelChatMessageArgs e)
    {
        var chatEvent = e.Payload.Event;
        var message = new ChatMessage(
            ChatPlatform.Twitch,
            chatEvent.ChatterUserName,
            chatEvent.Message.Text,
            DateTimeOffset.Now,
            chatEvent.Color);

        MessageReceived?.Invoke(message);
        return Task.CompletedTask;
    }

    private Task OnChannelFollow(object? sender, ChannelFollowArgs e)
    {
        var ev = e.Payload.Event;
        MessageReceived?.Invoke(new ChatMessage(
            ChatPlatform.Twitch, ev.UserName, "followed", DateTimeOffset.Now, Kind: ChatMessageKind.Follow));
        return Task.CompletedTask;
    }

    private Task OnChannelSubscribe(object? sender, ChannelSubscribeArgs e)
    {
        var ev = e.Payload.Event;
        // Gifted subs are announced separately, once per gift batch, via
        // channel.subscription.gift — skip here to avoid a duplicate alert
        // per recipient.
        if (ev.IsGift)
            return Task.CompletedTask;

        MessageReceived?.Invoke(new ChatMessage(
            ChatPlatform.Twitch, ev.UserName, $"subscribed (Tier {FormatTier(ev.Tier)})", DateTimeOffset.Now, Kind: ChatMessageKind.Subscribe));
        return Task.CompletedTask;
    }

    private Task OnChannelSubscriptionGift(object? sender, ChannelSubscriptionGiftArgs e)
    {
        var ev = e.Payload.Event;
        var gifter = ev.IsAnonymous ? "an anonymous gifter" : ev.UserName;
        MessageReceived?.Invoke(new ChatMessage(
            ChatPlatform.Twitch, gifter, $"gifted {ev.Total} sub(s) (Tier {FormatTier(ev.Tier)})", DateTimeOffset.Now, Kind: ChatMessageKind.GiftSub));
        return Task.CompletedTask;
    }

    private Task OnChannelRaid(object? sender, ChannelRaidArgs e)
    {
        var ev = e.Payload.Event;
        MessageReceived?.Invoke(new ChatMessage(
            ChatPlatform.Twitch, ev.FromBroadcasterUserName, $"raided with {ev.Viewers} viewer(s)", DateTimeOffset.Now, Kind: ChatMessageKind.Raid));
        return Task.CompletedTask;
    }

    private static string FormatTier(string tier) => tier switch
    {
        "1000" => "1",
        "2000" => "2",
        "3000" => "3",
        _ => tier,
    };
}
