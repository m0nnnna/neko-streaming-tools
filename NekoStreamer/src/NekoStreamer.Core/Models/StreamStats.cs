namespace NekoStreamer.Core.Models;

public sealed record StreamStats(TimeSpan Elapsed, double? BitrateKbps, double? Speed, int DroppedFrames);
