using NekoChat.Core.Models;

namespace NekoChat.Core.Sources;

/// <summary>
/// One connection to one platform's chat (one Twitch channel, one YouTube live
/// chat, etc). Each real implementation (Twitch IRC, YouTube Live Chat API, Kick's
/// websocket, X) owns its own auth and protocol — this is just the shape the
/// aggregator merges them through.
/// </summary>
public interface IChatSource
{
    ChatPlatform Platform { get; }

    bool IsConnected { get; }

    event Action<ChatMessage>? MessageReceived;

    event Action<Exception>? Faulted;

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync();
}
