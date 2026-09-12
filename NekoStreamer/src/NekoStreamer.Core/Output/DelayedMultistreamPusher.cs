using System.Globalization;
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
    private static readonly HashSet<string> ProgressKeys = new(StringComparer.Ordinal)
    {
        "frame", "fps", "bitrate", "total_size", "out_time_us", "out_time_ms",
        "out_time", "dup_frames", "drop_frames", "speed", "progress",
    };

    private readonly string _ffmpegPath;
    private readonly TimeSpan _delay;
    private readonly IReadOnlyList<DestinationTarget> _destinations;
    private readonly Queue<SegmentInfo> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly Dictionary<string, string> _progressFields = new();

    private ManagedProcess? _outputProcess;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;

    public event Action<string>? OutputLogReceived;
    public event Action<Exception>? Faulted;

    /// <summary>Fires once per ffmpeg -progress block (roughly every second) with the current bitrate/elapsed/speed/dropped-frame counts.</summary>
    public event Action<StreamStats>? StatsUpdated;

    public bool IsMuted { get; private set; }

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
        _outputProcess = ManagedProcess.Start(_ffmpegPath, BuildArgs());
        _outputProcess.OutputReceived += HandleOutputLine;

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

    /// <summary>Restarts the output leg with (or without) the audio stream mapped. Causes a brief reconnect on every destination — there's no live volume control on a pure stream-copy pipeline.</summary>
    public async Task SetMutedAsync(bool muted)
    {
        if (muted == IsMuted)
            return;

        await _processLock.WaitAsync();
        try
        {
            IsMuted = muted;
            await RestartOutputProcessAsync();
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task RestartOutputProcessAsync()
    {
        if (_outputProcess is not null)
        {
            _outputProcess.CloseStandardInput();
            await _outputProcess.StopAsync(TimeSpan.FromSeconds(2));
            _outputProcess.Dispose();
        }

        _outputProcess = ManagedProcess.Start(_ffmpegPath, BuildArgs());
        _outputProcess.OutputReceived += HandleOutputLine;
    }

    private string[] BuildArgs()
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "info", "-progress", "pipe:1",
            "-f", "mpegts", "-i", "pipe:0",
            "-map", "0:v",
        };

        if (!IsMuted)
            args.AddRange(new[] { "-map", "0:a" });

        args.AddRange(new[] { "-c", "copy" });

        // MPEG-TS tags H264/AAC with their TS stream_type values (27/15); FLV
        // requires its own tag values (7/10) and rejects the inherited ones as
        // "incompatible" even though the codecs themselves are fine. Only tag
        // streams actually mapped above — tagging a stream that doesn't exist in
        // the (muted) output errors out.
        args.AddRange(new[] { "-tag:v", "7" });
        if (!IsMuted)
            args.AddRange(new[] { "-tag:a", "10" });

        var teeTargets = string.Join('|', _destinations.Select(d => $"[f=flv]{d.FullUrl}"));
        args.AddRange(new[] { "-f", "tee", teeTargets });

        return args.ToArray();
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
        await _processLock.WaitAsync(ct);
        try
        {
            if (_outputProcess is null || !_outputProcess.IsRunning)
                throw new InvalidOperationException("Output ffmpeg process is not running.");

            await using var fileStream = new FileStream(
                segment.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);

            await fileStream.CopyToAsync(_outputProcess.StandardInput, ct);
            await _outputProcess.StandardInput.FlushAsync(ct);
        }
        finally
        {
            _processLock.Release();
        }
    }

    /// <summary>Diverts ffmpeg's `-progress` key=value lines into the stats accumulator instead of the log — everything else passes through as before.</summary>
    private void HandleOutputLine(string line)
    {
        var eq = line.IndexOf('=');
        if (eq > 0)
        {
            var key = line[..eq].Trim();
            if (ProgressKeys.Contains(key))
            {
                _progressFields[key] = line[(eq + 1)..].Trim();
                if (key == "progress")
                    FlushStats();
                return;
            }
        }

        OutputLogReceived?.Invoke(line);
    }

    private void FlushStats()
    {
        var elapsed = ParseElapsed(_progressFields.GetValueOrDefault("out_time"));
        var bitrate = ParseKbps(_progressFields.GetValueOrDefault("bitrate"));
        var speed = ParseSpeed(_progressFields.GetValueOrDefault("speed"));
        var dropped = int.TryParse(_progressFields.GetValueOrDefault("drop_frames"), out var d) ? d : 0;

        StatsUpdated?.Invoke(new StreamStats(elapsed, bitrate, speed, dropped));
    }

    private static TimeSpan ParseElapsed(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return TimeSpan.Zero;

        var parts = value.Split(':');
        if (parts.Length != 3)
            return TimeSpan.Zero;

        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s)
            : TimeSpan.Zero;
    }

    private static double? ParseKbps(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "N/A")
            return null;

        var trimmed = value.Replace("kbits/s", "").Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var kbps) ? kbps : null;
    }

    private static double? ParseSpeed(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "N/A")
            return null;

        var trimmed = value.TrimEnd('x');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : null;
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

        await _processLock.WaitAsync();
        try
        {
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
        finally
        {
            _processLock.Release();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _outputProcess?.Dispose();
    }
}
