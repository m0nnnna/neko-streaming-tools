namespace NekoStreamer.Core.Config;

/// <summary>Persisted config for the phone-controlled OBS/VTube Studio remote server.</summary>
public sealed class RemoteSettings
{
    public int Port { get; set; } = 5170;

    /// <summary>
    /// LAN IP the pairing QR code/URL should point at. Leave empty to auto-detect (picks the
    /// first non-loopback IPv4 address found, which is wrong on machines with several
    /// adapters — VPNs, VirtualBox host-only, etc.). Set this explicitly to the address your
    /// phone can actually reach, e.g. "192.168.0.10".
    /// </summary>
    public string AnnounceIp { get; set; } = "";

    public string ObsUrl { get; set; } = "ws://127.0.0.1:4455";

    /// <summary>DPAPI-encrypted OBS websocket password. Empty when OBS auth is disabled.</summary>
    public string EncryptedObsPassword { get; set; } = "";

    public string VtsUrl { get; set; } = "ws://127.0.0.1:8001";

    /// <summary>DPAPI-encrypted VTube Studio plugin auth token, obtained once via the in-app approval popup.</summary>
    public string EncryptedVtsToken { get; set; } = "";

    /// <summary>Shared secret the phone must send to use the API — prevents other devices on the LAN from taking control.</summary>
    public string AccessToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Set to false to let any device on the LAN use the API with no pairing step at all —
    /// same trust model as most local Stream Deck-style tools. Only turn this off on a
    /// network you trust.
    /// </summary>
    public bool RequireAccessToken { get; set; } = true;
}
