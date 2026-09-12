using NekoStreamer.Core.Models;
using NekoStreamer.Core.Processes;

namespace NekoStreamer.Core.Output;

/// <summary>
/// Tracks recently-seen segments from the same ring buffer the delay pipeline
/// reads from, and remuxes the trailing N seconds of them into a standalone .mp4
/// on demand. Reuses the byte-concatenation trick <see cref="DelayedMultistreamPusher"/>
/// depends on: segments are continuations of one MPEG-TS elementary stream (the
/// writer disables per-segment headers), so feeding them in order into one
/// ffmpeg process reconstructs a valid stream.
/// </summary>
public sealed class ClipRecorder
{
    private readonly string _ffmpegPath;
    private readonly string _clipsDirectory;
    private readonly TimeSpan _maxLookback;
    private readonly object _lock = new();
    private readonly Queue<SegmentInfo> _recent = new();

    public ClipRecorder(string ffmpegPath, string clipsDirectory, TimeSpan maxLookback)
    {
        _ffmpegPath = ffmpegPath;
        _clipsDirectory = clipsDirectory;
        _maxLookback = maxLookback;
    }

    /// <summary>Called by the pipeline on every completed segment, same as the pusher's Enqueue.</summary>
    public void Track(SegmentInfo segment)
    {
        lock (_lock)
        {
            _recent.Enqueue(segment);

            var cutoff = DateTimeOffset.UtcNow - _maxLookback;
            while (_recent.Count > 0 && _recent.Peek().DiscoveredAtUtc < cutoff)
                _recent.Dequeue();
        }
    }

    public async Task<string> SaveClipAsync(TimeSpan duration, CancellationToken ct = default)
    {
        List<SegmentInfo> segments;
        lock (_lock)
        {
            var cutoff = DateTimeOffset.UtcNow - duration;
            segments = _recent.Where(s => s.DiscoveredAtUtc >= cutoff).ToList();
        }

        if (segments.Count == 0)
            throw new InvalidOperationException("Not enough buffered video yet to save a clip.");

        Directory.CreateDirectory(_clipsDirectory);
        var outputPath = Path.Combine(_clipsDirectory, $"clip_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");

        var args = new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "mpegts", "-i", "pipe:0",
            "-c", "copy",
            "-movflags", "+faststart",
            outputPath,
        };

        using var process = ManagedProcess.Start(_ffmpegPath, args);
        foreach (var segment in segments)
        {
            if (!File.Exists(segment.FilePath))
                continue; // pruned or raced out from under us; skip rather than fail the whole clip

            await using var fileStream = new FileStream(
                segment.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 81920, useAsync: true);
            await fileStream.CopyToAsync(process.StandardInput, ct);
        }

        process.CloseStandardInput();
        await process.StopAsync(TimeSpan.FromSeconds(5));

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException("ffmpeg did not produce a clip file.");

        return outputPath;
    }
}
