using NekoStreamer.Core.Models;
using NekoStreamer.Core.Processes;

namespace NekoStreamer.Core.Ingest;

/// <summary>
/// Pulls the live stream from MediaMTX and writes it to disk as small stream-copied
/// .ts segments, forming the ring buffer the delay is built out of. Segment
/// completion is detected by polling ffmpeg's own segment_list file rather than
/// FileSystemWatcher, which is unreliable for rapid successive writes on Windows.
/// </summary>
public sealed class RingBufferSegmentWriter : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _ffmpegPath;
    private readonly Uri _sourceUrl;
    private readonly PipelineSettings _settings;
    private readonly string _segmentListPath;
    private ManagedProcess? _process;
    private long _lastReadOffset;
    private Timer? _pollTimer;
    private Timer? _pruneTimer;

    private volatile bool _stopping;

    public event Action<SegmentInfo>? SegmentCompleted;
    public event Action<string>? OutputLogReceived;

    /// <summary>Fires if the ingest process exits on its own — i.e. OBS stopped
    /// streaming — as opposed to us having called StopAsync ourselves.</summary>
    public event Action? SourceLost;

    public RingBufferSegmentWriter(string ffmpegPath, Uri sourceUrl, PipelineSettings settings)
    {
        _ffmpegPath = ffmpegPath;
        _sourceUrl = sourceUrl;
        _settings = settings;
        _segmentListPath = Path.Combine(settings.BufferDirectory, "index.txt");
    }

    public void Start()
    {
        Directory.CreateDirectory(_settings.BufferDirectory);

        if (File.Exists(_segmentListPath))
            File.Delete(_segmentListPath);
        _lastReadOffset = 0;

        var segmentSeconds = _settings.SegmentDuration.TotalSeconds.ToString("0.###");
        var args = new[]
        {
            "-hide_banner", "-loglevel", "info",
            "-i", _sourceUrl.ToString(),
            "-c", "copy",
            "-f", "segment",
            "-segment_time", segmentSeconds,
            "-segment_format", "mpegts",
            // Segments must be continuations of one MPEG-TS elementary stream, not
            // independent files each with their own PAT/PMT and continuity
            // counters — otherwise the pusher can't safely concatenate their raw
            // bytes back into a single valid stream for the destinations.
            "-individual_header_trailer", "0",
            "-segment_list", _segmentListPath,
            "-segment_list_type", "flat",
            "-segment_list_flags", "+live",
            Path.Combine(_settings.BufferDirectory, "seg_%08d.ts"),
        };

        _stopping = false;
        _process = ManagedProcess.Start(_ffmpegPath, args);
        _process.OutputReceived += line => OutputLogReceived?.Invoke(line);
        _process.Exited += () =>
        {
            if (!_stopping)
                SourceLost?.Invoke();
        };

        _pollTimer = new Timer(_ => ReadNewSegmentLines(), null, TimeSpan.Zero, PollInterval);
        _pruneTimer = new Timer(_ => PruneOldSegments(), null, _settings.Retention, TimeSpan.FromSeconds(10));
    }

    private void ReadNewSegmentLines()
    {
        if (!File.Exists(_segmentListPath))
            return;

        try
        {
            using var stream = new FileStream(_segmentListPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(_lastReadOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var segmentPath = Path.Combine(_settings.BufferDirectory, line.Trim());
                SegmentCompleted?.Invoke(new SegmentInfo(segmentPath, DateTimeOffset.UtcNow));
            }

            _lastReadOffset = stream.Position;
        }
        catch (IOException)
        {
            // file briefly locked by ffmpeg mid-write; next poll tick will retry
        }
    }

    private void PruneOldSegments()
    {
        var cutoff = DateTime.UtcNow - _settings.Retention;
        foreach (var file in Directory.EnumerateFiles(_settings.BufferDirectory, "seg_*.ts"))
        {
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                try { File.Delete(file); } catch (IOException) { /* still open, prune next tick */ }
            }
        }
    }

    /// <summary>
    /// Kills the ingest process immediately rather than waiting for a graceful
    /// exit. There's nothing worth flushing here — an interrupted last segment is
    /// simply never referenced (the segment writer never got as far as appending
    /// it to index.txt), so waiting around only stalls Stop for no benefit.
    /// </summary>
    public Task StopAsync()
    {
        _stopping = true;
        _pruneTimer?.Dispose();
        _pruneTimer = null;
        _pollTimer?.Dispose();
        _pollTimer = null;

        if (_process is not null)
        {
            _process.Kill();
            _process.Dispose();
            _process = null;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _pruneTimer?.Dispose();
        _pollTimer?.Dispose();
        _process?.Dispose();
    }
}
