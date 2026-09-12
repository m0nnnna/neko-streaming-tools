using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace NekoStreamer.Remote;

/// <summary>Minimal JSON-over-WebSocket send/receive shared by the OBS and VTube Studio clients — both protocols are plain JSON text frames.</summary>
internal static class WebSocketJson
{
    public static Task SendAsync(ClientWebSocket socket, JsonNode payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    /// <summary>Returns null when the remote closed the connection.</summary>
    public static async Task<JsonObject?> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        stream.Position = 0;
        return JsonNode.Parse(stream) as JsonObject;
    }
}
