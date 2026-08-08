using NekoStreamer.Core.Ingest;
using NekoStreamer.Core.Models;
using NekoStreamer.Core.Output;

namespace NekoStreamer.Core.Pipeline;

/// <summary>
/// Wires MediaMTX (ingest) -> RingBufferSegmentWriter (delay buffer) ->
/// DelayedMultistreamPusher (fan-out) into the one thing the UI actually drives.
/// </summary>
public sealed class RestreamPipeline : IAsyncDisposable
{
    private static readonly Uri IngestUrl = new("rtmp://127.0.0.1:1935/live");
    private const string IngestPathName = "live";

    private readonly MediaMtxServer _mediaMtx;
    private readonly string _ffmpegPath;
    private readonly PipelineSettings _settings;

    // Guards transitions between Live <-> WaitingForSource <-> Stopped so a
    // user-initiated Stop and an OBS disconnect firing at the same moment can't
    // race each other into disposing the same process twice or resurrecting a
    // pipeline the user just told us to stop.
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private IReadOnlyList<DestinationTarget>? _destinations;
    private RingBufferSegmentWriter? _writer;
    private DelayedMultistreamPusher? _pusher;
    private CancellationTokenSource? _watchCts;
    private Task? _watchTask;
    private bool _stopRequested;

    public PipelineState State { get; private set; } = PipelineState.Stopped;

    public event Action<PipelineState>? StateChanged;
    public event Action<string>? LogReceived;
    public event Action<Exception>? Faulted;

    public RestreamPipeline(MediaMtxServer mediaMtx, string ffmpegPath, PipelineSettings settings)
    {
        _mediaMtx = mediaMtx;
        _ffmpegPath = ffmpegPath;
        _settings = settings;
    }

    public void Start(IReadOnlyList<DestinationTarget> destinations)
    {
        if (State != PipelineState.Stopped)
            return;

        _destinations = destinations;
        _stopRequested = false;

        _mediaMtx.OutputLogReceived += line => LogReceived?.Invoke($"[mtx] {line}");
        _mediaMtx.Start();

        BeginWaitingForSource();
    }

    private void BeginWaitingForSource()
    {
        SetState(PipelineState.WaitingForSource);
        _watchCts = new CancellationTokenSource();
        _watchTask = Task.Run(() => WatchForSourceAsync(_watchCts.Token));
    }

    private async Task WatchForSourceAsync(CancellationToken ct)
    {
        try
        {
            while (!await _mediaMtx.IsPathReadyAsync(IngestPathName, ct))
                await Task.Delay(TimeSpan.FromSeconds(1), ct);

            StartDownstream(_destinations!);
            SetState(PipelineState.Live);
        }
        catch (OperationCanceledException)
        {
            // stopped while waiting for OBS to connect
        }
        catch (Exception ex)
        {
            SetState(PipelineState.Faulted);
            Faulted?.Invoke(ex);
        }
    }

    private void StartDownstream(IReadOnlyList<DestinationTarget> destinations)
    {
        _pusher = new DelayedMultistreamPusher(_ffmpegPath, _settings.Delay, destinations);
        _pusher.OutputLogReceived += line => LogReceived?.Invoke($"[out] {line}");
        _pusher.Faulted += ex =>
        {
            SetState(PipelineState.Faulted);
            Faulted?.Invoke(ex);
        };
        _pusher.Start();

        _writer = new RingBufferSegmentWriter(_ffmpegPath, IngestUrl, _settings);
        _writer.SegmentCompleted += segment => _pusher.Enqueue(segment);
        _writer.OutputLogReceived += line => LogReceived?.Invoke($"[in] {line}");
        _writer.SourceLost += OnSourceLost;
        _writer.Start();
    }

    /// <summary>
    /// Fires from the writer's ffmpeg process exiting on its own — OBS stopped
    /// streaming. Tears down the downstream pusher (so the destinations actually
    /// see the broadcast end instead of hanging) and goes back to waiting, so
    /// streaming resumes automatically if OBS reconnects.
    /// </summary>
    private void OnSourceLost()
    {
        _ = RecoverFromSourceLossAsync();
    }

    private async Task RecoverFromSourceLossAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (State != PipelineState.Live || _stopRequested)
                return;

            LogReceived?.Invoke("OBS stream ended — waiting for it to reconnect...");

            if (_pusher is not null)
            {
                await _pusher.StopAsync();
                _pusher.Dispose();
                _pusher = null;
            }

            _writer?.Dispose();
            _writer = null;

            if (_stopRequested)
                return;

            BeginWaitingForSource();
        }
        catch (Exception ex)
        {
            SetState(PipelineState.Faulted);
            Faulted?.Invoke(ex);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        _stopRequested = true;

        if (_watchCts is not null)
        {
            await _watchCts.CancelAsync();
            if (_watchTask is not null)
                await _watchTask.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });
            _watchCts.Dispose();
            _watchCts = null;
        }

        await _lifecycleLock.WaitAsync();
        try
        {
            // The pusher goes first, and is the only one worth waiting on: it's
            // what's actually connected to the destinations, so this is what
            // guarantees the broadcast really ends there. The writer and
            // mediamtx have nothing worth flushing, so they're killed
            // immediately and in parallel rather than stalling Stop with
            // sequential waits.
            if (_pusher is not null)
            {
                await _pusher.StopAsync();
                _pusher.Dispose();
                _pusher = null;
            }

            var writerStop = _writer?.StopAsync() ?? Task.CompletedTask;
            var mediaMtxStop = _mediaMtx.StopAsync();
            await Task.WhenAll(writerStop, mediaMtxStop);

            _writer?.Dispose();
            _writer = null;

            SetState(PipelineState.Stopped);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void SetState(PipelineState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
