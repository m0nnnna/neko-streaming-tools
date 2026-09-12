using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace NekoStreamer.Remote.Obs;

/// <summary>
/// Talks to obs-websocket v5 (built into OBS 28+, Tools -> obs-websocket Settings).
/// Reference: https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md
/// </summary>
public sealed class ObsClient : IAsyncDisposable
{
    private const int OpHello = 0;
    private const int OpIdentify = 1;
    private const int OpIdentified = 2;
    private const int OpEvent = 5;
    private const int OpRequest = 6;
    private const int OpRequestResponse = 7;

    /// <summary>EventSubscription.Scenes — see obs-websocket protocol docs.</summary>
    private const int SceneEventSubscription = 1 << 2;

    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private ClientWebSocket? _socket;

    public event Action<bool>? ConnectionChanged;
    public event Action<string>? CurrentSceneChanged;

    public bool IsConnected { get; private set; }
    public string? CurrentScene { get; private set; }

    public async Task ConnectAsync(Uri uri, string? password, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, ct);

        var hello = await WebSocketJson.ReceiveJsonAsync(socket, ct)
            ?? throw new IOException("OBS closed the connection during handshake");
        var helloData = hello["d"]!.AsObject();

        var identifyData = new JsonObject { ["rpcVersion"] = helloData["rpcVersion"]!.GetValue<int>() };
        if (helloData["authentication"] is JsonObject authInfo)
        {
            var challenge = authInfo["challenge"]!.GetValue<string>();
            var salt = authInfo["salt"]!.GetValue<string>();
            identifyData["authentication"] = BuildAuthString(password ?? "", salt, challenge);
        }
        identifyData["eventSubscriptions"] = SceneEventSubscription;

        await WebSocketJson.SendAsync(socket, new JsonObject { ["op"] = OpIdentify, ["d"] = identifyData }, ct);
        var identified = await WebSocketJson.ReceiveJsonAsync(socket, ct)
            ?? throw new IOException("OBS closed the connection during identify");
        if (identified["op"]?.GetValue<int>() != OpIdentified)
            throw new InvalidOperationException("OBS did not identify us — check the websocket password");

        _socket = socket;
        IsConnected = true;
        ConnectionChanged?.Invoke(true);
        _ = Task.Run(() => ReceiveLoopAsync(socket, ct));
    }

    public async Task<(string Current, List<string> Scenes)> GetSceneListAsync(CancellationToken ct)
    {
        var resp = await RequestAsync("GetSceneList", null, ct);
        var current = resp["currentProgramSceneName"]!.GetValue<string>();
        var scenes = resp["scenes"]!.AsArray()
            .Select(n => n!["sceneName"]!.GetValue<string>())
            .Reverse() // obs-websocket lists scenes bottom-to-top of the OBS scene list
            .ToList();
        CurrentScene = current;
        return (current, scenes);
    }

    public Task SetCurrentSceneAsync(string sceneName, CancellationToken ct) =>
        RequestAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = sceneName }, ct);

    private async Task<JsonObject> RequestAsync(string requestType, JsonObject? requestData, CancellationToken ct)
    {
        if (_socket is null || !IsConnected)
            throw new InvalidOperationException("Not connected to OBS");

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[requestId] = tcs;

        var message = new JsonObject
        {
            ["op"] = OpRequest,
            ["d"] = new JsonObject
            {
                ["requestType"] = requestType,
                ["requestId"] = requestId,
                ["requestData"] = requestData,
            },
        };

        await WebSocketJson.SendAsync(_socket, message, ct);
        await using var reg = ct.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var json = await WebSocketJson.ReceiveJsonAsync(socket, ct);
                if (json is null) break;

                var op = json["op"]?.GetValue<int>();
                var d = json["d"] as JsonObject;
                if (d is null) continue;

                if (op == OpRequestResponse)
                {
                    HandleResponse(d);
                }
                else if (op == OpEvent && d["eventType"]?.GetValue<string>() == "CurrentProgramSceneChanged")
                {
                    var sceneName = (d["eventData"] as JsonObject)?["sceneName"]?.GetValue<string>();
                    if (sceneName is not null)
                    {
                        CurrentScene = sceneName;
                        CurrentSceneChanged?.Invoke(sceneName);
                    }
                }
            }
        }
        catch
        {
            // connection dropped — fall through to cleanup below
        }

        IsConnected = false;
        ConnectionChanged?.Invoke(false);
        lock (_pending)
        {
            foreach (var tcs in _pending.Values) tcs.TrySetException(new IOException("OBS disconnected"));
            _pending.Clear();
        }
    }

    private void HandleResponse(JsonObject d)
    {
        var requestId = d["requestId"]!.GetValue<string>();
        TaskCompletionSource<JsonObject>? tcs;
        lock (_pending)
        {
            _pending.Remove(requestId, out tcs);
        }
        if (tcs is null) return;

        var status = d["requestStatus"] as JsonObject;
        var ok = status?["result"]?.GetValue<bool>() ?? false;
        if (ok)
            tcs.TrySetResult(d["responseData"] as JsonObject ?? new JsonObject());
        else
            tcs.TrySetException(new InvalidOperationException(status?["comment"]?.GetValue<string>() ?? "OBS request failed"));
    }

    private static string BuildAuthString(string password, string salt, string challenge)
    {
        var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        var secretBase64 = Convert.ToBase64String(secretHash);
        var authHash = SHA256.HashData(Encoding.UTF8.GetBytes(secretBase64 + challenge));
        return Convert.ToBase64String(authHash);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is { State: WebSocketState.Open } socket)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        _socket?.Dispose();
    }
}
