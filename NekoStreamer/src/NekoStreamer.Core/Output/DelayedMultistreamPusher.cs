using NekoStreamer.Core.Models;
using NekoStreamer.Core.Processes;

namespace NekoStreamer.Core.Output;

/// <summary>
/// Holds completed segments until their configured delay has elapsed, then streams
/// their raw MPEG-TS bytes into a single long-lived ffmpeg process reading from
/// stdin. Byte concatenation (not the concat demuxer) matters here: the concat
/// demuxer buffers everything and only starts producing output once it sees EOF,
/// which never happens on a genuinely live, indefinitely-open pipe — so nothing
/// would ever reach the destinations until the stream ended. Raw MPEG-TS bytes
/// process incrementally as they arrive instead. This only works because the
/// segment writer disables per-segment headers (-individual_header_trailer 0), so
/// segments are continuations of one elementary stream rather than independent
/// files with their own PAT/PMT and continuity counters. That process does a
/// stream-copy `tee` fan-out to every destination, so CPU/GPU cost stays flat
/// regardless of destination count.
/// </summary>
public sealed class DelayedMultistreamPusher : IDisposable
{
    private readonly string _ffmpegPath;
    private readonly TimeSpan _delay;
    private readonly IReadOnlyList<DestinationTarget> _destinations;
    private readonly Queue<SegmentInfo> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _queueLock = new();

    private ManagedProcess? _outputProcess;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;

    public event Action<string>? OutputLogReceived;
    public event Action<Exception>? Faulted;

    public DelayedMultistreamPusher(string ffmpegPath, TimeSpan delay, IReadOnlyList<DestinationTarget> destinations)
    {
        if (destinations.Count == 0)
            throw new ArgumentException("At least one destination is required.", nameof(destinations));

        _ffmpegPath = ffmpegPath;
        _delay = delay;
        _destinations = destinations;
    }

    public void Start()
    {
        var teeTargets = string.Join('|', _destinations.Select(d => $"[f=flv]{d.FullUrl}"));
        var args = new[]
        {
            "-hide_banner", "-loglevel", "info",
            "-f", "mpegts", "-i", "pipe:0",
            "-map", "0",
            "-c", "copy",
            // MPEG-TS tags H264/AAC with their TS stream_type values (27/15); FLV
            // requires its own tag values (7/10) and rejects the inherited ones as
            // "incompatible" even though the codecs themselves are fine.
            "-tag:v", "7", "-tag:a", "10",
            "-f", "tee",
            teeTargets,
        };

        _outputProcess = ManagedProcess.Start(_ffmpegPath, args);
        _outputProcess.OutputReceived += line => OutputLogReceived?.Invoke(line);

        _cts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpLoopAsync(_cts.Token));
    }

    /// <summary>Called by the segment writer whenever a new segment finishes.</summary>
    public void Enqueue(SegmentInfo segment)
    {
        lock (_queueLock)
            _pending.Enqueue(segment);
        _signal.Release();
    }

    private async Task PumpLoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(ct);

                SegmentInfo segment;
                lock (_queueLock)
                {
                    if (_pending.Count == 0)
                        continue;
                    segment = _pending.Dequeue();
                }

                var waitTime = segment.ReleaseAtUtc(_delay) - DateTimeOffset.UtcNow;
                if (waitTime > TimeSpan.Zero)
                    await Task.Delay(waitTime, ct);

                await WriteSegmentToOutputAsync(segment, ct);
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
    }

    private async Task WriteSegmentToOutputAsync(SegmentInfo segment, CancellationToken ct)
    {
        if (_outputProcess is null || !_outputProcess.IsRunning)
            throw new InvalidOperationException("Output ffmpeg process is not running.");

        await using var fileStream = new FileStream(
            segment.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        await fileStream.CopyToAsync(_outputProcess.StandardInput, ct);
        await _outputProcess.StandardInput.FlushAsync(ct);
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_pumpTask is not null)
                await _pumpTask.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });
            _cts.Dispose();
            _cts = null;
        }

        if (_outputProcess is not null)
        {
            // ffmpeg reading pipe:0 won't flush/finalize its output until it sees
            // EOF on stdin — closing it is what lets any still-buffered video
            // actually reach the destinations before the process exits. This is
            // the one process that genuinely needs a graceful stop: it's what's
            // actually connected to Twitch/Kick/etc, so this is what guarantees
            // Stop actually ends the broadcast there instead of leaving it hanging.
            _outputProcess.CloseStandardInput();
            await _outputProcess.StopAsync(TimeSpan.FromSeconds(2));
            _outputProcess.Dispose();
            _outputProcess = null;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _outputProcess?.Dispose();
    }
}
