using NekoChat.Core.Models;

namespace NekoChat.Core.Viewers;

/// <summary>
/// Polls every registered platform's viewer count on a timer and reports the sum.
/// A platform that isn't live (returns null) contributes 0 rather than dropping
/// the total — losing one platform's count shouldn't hide the others'.
/// </summary>
public sealed class ViewerCountAggregator : IDisposable
{
    private readonly List<IViewerCountSource> _sources = new();
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public event Action<int>? TotalUpdated;
    public event Action<ChatPlatform, Exception>? SourceFaulted;

    public void AddSource(IViewerCountSource source) => _sources.Add(source);

    public void Start(TimeSpan interval)
    {
        Stop();
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(interval, _pollCts.Token), CancellationToken.None);
    }

    public void Stop()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _pollTask = null;
    }

    private async Task PollLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var counts = await Task.WhenAll(_sources.Select(s => PollSourceAsync(s, ct)));
                TotalUpdated?.Invoke(counts.Sum());
                await Task.Delay(interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task<int> PollSourceAsync(IViewerCountSource source, CancellationToken ct)
    {
        try
        {
            return await source.GetViewerCountAsync(ct) ?? 0;
        }
        catch (Exception ex)
        {
            SourceFaulted?.Invoke(source.Platform, ex);
            return 0;
        }
    }

    public void Dispose() => Stop();
}
