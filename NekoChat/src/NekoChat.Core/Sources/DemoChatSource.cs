using NekoChat.Core.Models;

namespace NekoChat.Core.Sources;

/// <summary>
/// Generates synthetic messages on a timer. Exists only to prove the aggregator
/// and feed UI work correctly before any real platform client (Twitch IRC,
/// YouTube Live Chat API, Kick websocket, X) is built — not for shipping.
/// </summary>
public sealed class DemoChatSource : IChatSource
{
    private static readonly (string Author, string Body)[] SampleMessages =
    [
        ("neko_fan_1", "hyped for this stream!"),
        ("purple_heart", "POGGERS"),
        ("lurker99", "first time here, loving it"),
        ("mod_sarah", "welcome everyone, remember the rules"),
        ("chatty_cathy", "lol that was amazing"),
        ("silent_bob", "o7"),
    ];

    private readonly Random _random = new();
    private Timer? _timer;

    public ChatPlatform Platform { get; }

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;

    // Required by IChatSource; this demo source has no failure path to raise it from.
#pragma warning disable CS0067
    public event Action<Exception>? Faulted;
#pragma warning restore CS0067

    public DemoChatSource(ChatPlatform platform)
    {
        Platform = platform;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        var interval = TimeSpan.FromSeconds(2 + _random.NextDouble() * 4);
        _timer = new Timer(_ => EmitRandomMessage(), null, TimeSpan.Zero, interval);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    private void EmitRandomMessage()
    {
        var (author, body) = SampleMessages[_random.Next(SampleMessages.Length)];
        MessageReceived?.Invoke(new ChatMessage(Platform, author, body, DateTimeOffset.Now));
    }
}
