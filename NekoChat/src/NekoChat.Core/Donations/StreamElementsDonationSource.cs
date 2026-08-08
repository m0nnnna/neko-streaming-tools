using System.Text.Json;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;
using SocketIOClient;

namespace NekoChat.Core.Donations;

/// <summary>
/// Watches a StreamElements channel's real-time tip feed over Socket.IO. Auth is a
/// JWT copy-pasted from streamelements.com/dashboard/account/channels — no OAuth
/// app registration needed, matching the "connect out, no public endpoint" shape
/// every other source in this app uses. There's no maintained C# client for this
/// protocol (the one on NuGet is abandoned), so this is hand-rolled against
/// StreamElements' documented realtime event shape, same as Kick's approach.
/// </summary>
public sealed class StreamElementsDonationSource : IChatSource
{
    private readonly string _jwtToken;
    private SocketIO? _client;

    public ChatPlatform Platform => ChatPlatform.StreamElements;

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public StreamElementsDonationSource(string jwtToken)
    {
        _jwtToken = jwtToken;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _client = new SocketIO(new Uri("https://realtime.streamelements.com"), new SocketIOOptions
        {
            Transport = SocketIOClient.Common.TransportProtocol.WebSocket,
            Reconnection = true,
        });

        _client.OnConnected += async (_, _) =>
        {
            await _client.EmitAsync("authenticate", new object[] { new { method = "jwt", token = _jwtToken } });
        };

        _client.On("authenticated", _ =>
        {
            IsConnected = true;
            return Task.CompletedTask;
        });
        _client.On("unauthorized", ctx =>
        {
            Faulted?.Invoke(new InvalidOperationException($"StreamElements rejected the token: {ctx.RawText}"));
            return Task.CompletedTask;
        });
        _client.On("event", ctx =>
        {
            HandleEvent(ctx);
            return Task.CompletedTask;
        });

        _client.OnDisconnected += (_, _) => IsConnected = false;
        _client.OnError += (_, message) => Faulted?.Invoke(new InvalidOperationException($"StreamElements socket error: {message}"));

        await _client.ConnectAsync();
    }

    private void HandleEvent(IEventContext ctx)
    {
        try
        {
            using var doc = JsonDocument.Parse(ctx.RawText);
            var root = doc.RootElement;
            var payload = root.ValueKind == JsonValueKind.Array ? root[0] : root;

            if (!payload.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "tip")
                return;

            if (!payload.TryGetProperty("data", out var data))
                return;

            var name = data.TryGetProperty("username", out var u) ? u.GetString()
                : data.TryGetProperty("displayName", out var d) ? d.GetString()
                : "anonymous";
            var amount = data.TryGetProperty("amount", out var a) ? a.ToString() : "?";
            var currency = data.TryGetProperty("currency", out var c) ? c.GetString() : "";
            var message = data.TryGetProperty("message", out var m) ? m.GetString() : null;

            var body = string.IsNullOrWhiteSpace(message)
                ? $"donated {amount} {currency}"
                : $"donated {amount} {currency}: {message}";

            MessageReceived?.Invoke(new ChatMessage(
                ChatPlatform.StreamElements, name ?? "anonymous", body, DateTimeOffset.Now, Kind: ChatMessageKind.Donation));
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        if (_client is not null)
        {
            await _client.DisconnectAsync();
            _client.Dispose();
            _client = null;
        }
    }
}
