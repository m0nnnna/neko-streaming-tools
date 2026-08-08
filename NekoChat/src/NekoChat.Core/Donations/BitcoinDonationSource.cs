using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;

namespace NekoChat.Core.Donations;

/// <summary>
/// Watches a Bitcoin address for incoming payments via blockchain.info's free
/// public websocket feed — no account, no API key. A blockchain transaction has
/// no text field, so alerts can only ever show an amount, never a donor name or
/// message.
/// </summary>
public sealed class BitcoinDonationSource : IChatSource
{
    private readonly string _address;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public ChatPlatform Platform => ChatPlatform.Bitcoin;

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public BitcoinDonationSource(string address)
    {
        _address = address;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri("wss://ws.blockchain.info/inv"), ct);

        var subscribeMessage = JsonSerializer.Serialize(new { op = "addr_sub", addr = _address });
        await _socket.SendAsync(Encoding.UTF8.GetBytes(subscribeMessage), WebSocketMessageType.Text, true, ct);

        IsConnected = true;
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16384];
        try
        {
            while (_socket!.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
        finally
        {
            IsConnected = false;
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("op", out var opProp) || opProp.GetString() != "utx")
                return;

            var tx = root.GetProperty("x");
            long receivedSatoshis = 0;
            foreach (var output in tx.GetProperty("out").EnumerateArray())
            {
                if (output.TryGetProperty("addr", out var addrProp) && addrProp.GetString() == _address)
                    receivedSatoshis += output.GetProperty("value").GetInt64();
            }

            if (receivedSatoshis <= 0)
                return;

            var btc = receivedSatoshis / 100_000_000m;
            MessageReceived?.Invoke(new ChatMessage(
                ChatPlatform.Bitcoin, "anonymous", $"received {btc:0.########} BTC", DateTimeOffset.Now, Kind: ChatMessageKind.Donation));
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        _receiveCts?.Cancel();
        if (_receiveTask is not null)
            await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });

        if (_socket is not null)
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            _socket.Dispose();
            _socket = null;
        }
    }
}
