using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Output;

/// <summary>
/// Appends every completed segment's raw bytes onto one growing local file for the
/// life of a live session — a full backup independent of the multistream delay and
/// of whatever each destination's own VOD/archive does. Written as .ts, not .mp4:
/// segments are already valid continuations of one MPEG-TS stream (same reasoning
/// as ClipRecorder/DelayedMultistreamPusher), and unlike .mp4, a .ts file stays
/// playable even if the app is killed mid-recording rather than stopped cleanly.
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    private readonly string _outputPath;
    private FileStream? _output;

    public SessionRecorder(string outputPath)
    {
        _outputPath = outputPath;
    }

    public void Start()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        _output = new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    /// <summary>Called inline from the same segment-completed handler as the pusher's Enqueue and the clip recorder's Track — segments arrive serially, so no locking is needed here.</summary>
    public void Append(SegmentInfo segment)
    {
        if (_output is null || !File.Exists(segment.FilePath))
            return;

        using var segmentStream = new FileStream(segment.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        segmentStream.CopyTo(_output);
        _output.Flush();
    }

    public void Dispose() => _output?.Dispose();
}
