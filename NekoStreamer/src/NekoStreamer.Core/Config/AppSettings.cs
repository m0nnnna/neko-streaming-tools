using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Config;

public sealed class AppSettings
{
    public int DelaySeconds { get; set; } = 30;

    public int ClipSeconds { get; set; } = 30;

    /// <summary>Raw WPF ModifierKeys flags — Alt=1, Control=2, Shift=4, Windows=8. Default is Control|Alt.</summary>
    public int ClipHotkeyModifiers { get; set; } = 3;

    /// <summary>WPF Key enum name, e.g. "C".</summary>
    public string ClipHotkeyKey { get; set; } = "C";

    public bool RecordSession { get; set; }

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
