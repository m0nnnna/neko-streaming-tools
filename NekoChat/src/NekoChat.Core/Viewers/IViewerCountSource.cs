using NekoChat.Core.Models;

namespace NekoChat.Core.Viewers;

/// <summary>A platform's current live viewer count. Returns null if not currently live.</summary>
public interface IViewerCountSource
{
    ChatPlatform Platform { get; }

    Task<int?> GetViewerCountAsync(CancellationToken ct = default);
}
