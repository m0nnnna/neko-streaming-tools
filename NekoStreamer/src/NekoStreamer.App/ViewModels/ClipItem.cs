namespace NekoStreamer.App.ViewModels;

public sealed class ClipItem
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required string SavedAtDisplay { get; init; }
    public required string SizeDisplay { get; init; }
}
