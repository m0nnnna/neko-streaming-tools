namespace NekoStreamer.Core.Models;

public sealed class PipelineSettings
{
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(30);

    public required string BufferDirectory { get; init; }

    public required string ClipsDirectory { get; init; }

    public bool RecordSession { get; init; }

    public required string RecordingsDirectory { get; init; }

    public TimeSpan SegmentDuration { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetentionMargin { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan Retention => Delay + RetentionMargin;
}
