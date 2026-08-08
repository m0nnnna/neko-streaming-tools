namespace NekoStreamer.Core.Models;

public sealed record DestinationTarget(string Name, string RtmpUrl, string StreamKey)
{
    public string FullUrl => $"{RtmpUrl.TrimEnd('/')}/{StreamKey}";
}
