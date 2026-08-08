using NekoChat.Core.Models;
using NekoChat.Core.Sources;

namespace NekoChat.Core.Tests.Sources;

public class ChatAggregatorTests
{
    [Fact]
    public void MessageReceived_FiresForMessagesFromAnySource()
    {
        var aggregator = new ChatAggregator();
        var twitch = new FakeChatSource(ChatPlatform.Twitch);
        var kick = new FakeChatSource(ChatPlatform.Kick);
        aggregator.AddSource(twitch);
        aggregator.AddSource(kick);

        var received = new List<ChatMessage>();
        aggregator.MessageReceived += received.Add;

        twitch.Emit("alice", "hello from twitch");
        kick.Emit("bob", "hello from kick");

        Assert.Equal(2, received.Count);
        Assert.Equal(ChatPlatform.Twitch, received[0].Platform);
        Assert.Equal(ChatPlatform.Kick, received[1].Platform);
    }

    [Fact]
    public void SourceFaulted_IdentifiesWhichPlatformFailed()
    {
        var aggregator = new ChatAggregator();
        var youtube = new FakeChatSource(ChatPlatform.YouTube);
        aggregator.AddSource(youtube);

        ChatPlatform? faultedPlatform = null;
        aggregator.SourceFaulted += (platform, _) => faultedPlatform = platform;

        youtube.EmitFault(new InvalidOperationException("connection dropped"));

        Assert.Equal(ChatPlatform.YouTube, faultedPlatform);
    }

    [Fact]
    public async Task ConnectAllAsync_OneSourceFailingDoesNotThrowOrBlockOthers()
    {
        var aggregator = new ChatAggregator();
        var youtube = new FakeChatSource(ChatPlatform.YouTube) { ThrowOnConnect = new InvalidOperationException("not currently live") };
        var twitch = new FakeChatSource(ChatPlatform.Twitch);
        aggregator.AddSource(youtube);
        aggregator.AddSource(twitch);

        ChatPlatform? faultedPlatform = null;
        aggregator.SourceFaulted += (platform, _) => faultedPlatform = platform;

        // Must not throw, even though YouTube's ConnectAsync does.
        await aggregator.ConnectAllAsync();

        Assert.Equal(ChatPlatform.YouTube, faultedPlatform);
        Assert.True(twitch.IsConnected);
        Assert.False(youtube.IsConnected);
    }

    private sealed class FakeChatSource : IChatSource
    {
        public ChatPlatform Platform { get; }
        public bool IsConnected { get; private set; }
        public Exception? ThrowOnConnect { get; init; }
        public event Action<ChatMessage>? MessageReceived;
        public event Action<Exception>? Faulted;

        public FakeChatSource(ChatPlatform platform) => Platform = platform;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            if (ThrowOnConnect is not null)
                throw ThrowOnConnect;

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Emit(string author, string body) =>
            MessageReceived?.Invoke(new ChatMessage(Platform, author, body, DateTimeOffset.UtcNow));

        public void EmitFault(Exception ex) => Faulted?.Invoke(ex);
    }
}
