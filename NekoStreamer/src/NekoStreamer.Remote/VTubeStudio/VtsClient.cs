using System.Net.WebSockets;
using System.Text.Json.Nodes;

namespace NekoStreamer.Remote.VTubeStudio;

/// <summary>
/// Talks to the VTube Studio API (enabled in VTS under Settings -> Plugins, default ws://localhost:8001).
/// "Emotions" are triggered via Hotkeys — VTS hotkeys are how expressions/animations get exposed to plugins.
/// Reference: https://github.com/DenchiSoft/VTubeStudio
/// </summary>
public sealed class VtsClient : IAsyncDisposable
{
    private const string PluginName = "NekoStreamer Remote";
    private const string PluginDeveloper = "NekoStreamer";

    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private ClientWebSocket? _socket;

    public event Action<bool>? ConnectionChanged;
    public event Action<bool>? AuthenticationChanged;

    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }

    public async Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, ct);

        _socket = socket;
        IsConnected = true;
        IsAuthenticated = false;
        ConnectionChanged?.Invoke(true);
        _ = Task.Run(() => ReceiveLoopAsync(socket, ct));
    }

    /// <summary>Triggers the in-app VTS popup asking the user to approve this plugin. Call once; persist the returned token.</summary>
    public async Task<string> RequestAuthTokenAsync(CancellationToken ct)
    {
        var resp = await RequestAsync("AuthenticationTokenRequest", new JsonObject
        {
            ["pluginName"] = PluginName,
            ["pluginDeveloper"] = PluginDeveloper,
        }, ct);
        return resp["authenticationToken"]!.GetValue<string>();
    }

    public async Task<bool> AuthenticateAsync(string token, CancellationToken ct)
    {
        var resp = await RequestAsync("AuthenticationRequest", new JsonObject
        {
            ["pluginName"] = PluginName,
            ["pluginDeveloper"] = PluginDeveloper,
            ["authenticationToken"] = token,
        }, ct);
        IsAuthenticated = resp["authenticated"]?.GetValue<bool>() ?? false;
        AuthenticationChanged?.Invoke(IsAuthenticated);
        return IsAuthenticated;
    }

    public async Task<List<VtsHotkey>> GetHotkeysAsync(CancellationToken ct)
    {
        var resp = await RequestAsync("HotkeysInCurrentModelRequest", new JsonObject(), ct);
        var hotkeys = resp["availableHotkeys"]?.AsArray() ?? new JsonArray();
        return hotkeys
            .Select(n => new VtsHotkey(
                n!["hotkeyID"]!.GetValue<string>(),
                n["name"]!.GetValue<string>(),
                n["type"]?.GetValue<string>() ?? ""))
            .ToList();
    }

    public Task TriggerHotkeyAsync(string hotkeyId, CancellationToken ct) =>
        RequestAsync("HotkeyTriggerRequest", new JsonObject { ["hotkeyID"] = hotkeyId }, ct);

    private async Task<JsonObject> RequestAsync(string messageType, JsonObject data, CancellationToken ct)
    {
        if (_socket is null || !IsConnected)
            throw new InvalidOperationException("Not connected to VTube Studio");

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[requestId] = tcs;

        var envelope = new JsonObject
        {
            ["apiName"] = "VTubeStudioPublicAPI",
            ["apiVersion"] = "1.0",
            ["requestID"] = requestId,
            ["messageType"] = messageType,
            ["data"] = data,
        };

        await WebSocketJson.SendAsync(_socket, envelope, ct);
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

                var requestId = json["requestID"]?.GetValue<string>();
                if (requestId is null) continue;

                TaskCompletionSource<JsonObject>? tcs;
                lock (_pending)
                {
                    _pending.Remove(requestId, out tcs);
                }
                if (tcs is null) continue;

                var data = json["data"] as JsonObject ?? new JsonObject();
                if (json["messageType"]?.GetValue<string>() == "APIError")
                    tcs.TrySetException(new InvalidOperationException(data["message"]?.GetValue<string>() ?? "VTube Studio API error"));
                else
                    tcs.TrySetResult(data);
            }
        }
        catch
        {
            // connection dropped — fall through to cleanup below
        }

        IsConnected = false;
        IsAuthenticated = false;
        ConnectionChanged?.Invoke(false);
        lock (_pending)
        {
            foreach (var tcs in _pending.Values) tcs.TrySetException(new IOException("VTube Studio disconnected"));
            _pending.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is { State: WebSocketState.Open } socket)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        _socket?.Dispose();
    }
}

public sealed record VtsHotkey(string HotkeyId, string Name, string Type);
