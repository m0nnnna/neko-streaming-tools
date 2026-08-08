using NekoChat.Core.Models;

namespace NekoChat.Core.Sources;

/// <summary>Merges every connected platform's chat into one ordered-by-arrival feed.</summary>
public sealed class ChatAggregator
{
    private readonly List<IChatSource> _sources = new();

    public event Action<ChatMessage>? MessageReceived;

    public event Action<ChatPlatform, Exception>? SourceFaulted;

    public IReadOnlyList<IChatSource> Sources => _sources;

    public void AddSource(IChatSource source)
    {
        source.MessageReceived += OnMessageReceived;
        source.Faulted += ex => SourceFaulted?.Invoke(source.Platform, ex);
        _sources.Add(source);
    }

    /// <summary>
    /// Connects every source independently — one platform failing (e.g. YouTube
    /// with no active broadcast right now) is reported via SourceFaulted rather
    /// than throwing, so it can never take down the other platforms' connections
    /// or the caller.
    /// </summary>
    public Task ConnectAllAsync(CancellationToken ct = default) =>
        Task.WhenAll(_sources.Select(s => ConnectSourceAsync(s, ct)));

    private async Task ConnectSourceAsync(IChatSource source, CancellationToken ct)
    {
        try
        {
            await source.ConnectAsync(ct);
        }
        catch (Exception ex)
        {
            SourceFaulted?.Invoke(source.Platform, ex);
        }
    }

    public Task DisconnectAllAsync() =>
        Task.WhenAll(_sources.Select(s => DisconnectSourceAsync(s)));

    private async Task DisconnectSourceAsync(IChatSource source)
    {
        try
        {
            await source.DisconnectAsync();
        }
        catch (Exception ex)
        {
            SourceFaulted?.Invoke(source.Platform, ex);
        }
    }

    private void OnMessageReceived(ChatMessage message) => MessageReceived?.Invoke(message);
}
