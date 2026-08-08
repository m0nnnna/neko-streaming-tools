using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Config;

public sealed class AppSettings
{
    public int DelaySeconds { get; set; } = 30;

    public List<StoredDestination> Destinations { get; set; } = new();
}

/// <summary>Persisted shape of a destination — stream key is DPAPI-encrypted, never plaintext on disk.</summary>
public sealed class StoredDestination
{
    public required string Name { get; set; }
    public required string RtmpUrl { get; set; }
    public required string EncryptedStreamKey { get; set; }
    public StreamingPlatform Platform { get; set; } = StreamingPlatform.Custom;
}
