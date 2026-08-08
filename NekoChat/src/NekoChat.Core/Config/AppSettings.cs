namespace NekoChat.Core.Config;

public sealed class AppSettings
{
    public TwitchAccountSettings? Twitch { get; set; }

    public string? KickChannelSlug { get; set; }

    public YouTubeAccountSettings? YouTube { get; set; }

    public DonationSettings? Donations { get; set; }

    public AlertBoxSettings? AlertBox { get; set; }
}

/// <summary>
/// GIF/image + sound shown for alerts (follows, subs, raids, donations, etc).
/// <see cref="Default"/> plays for any alert kind without its own entry in
/// <see cref="Overrides"/> (keyed by <c>ChatMessageKind.ToString()</c>, e.g.
/// "Donation"). File paths only, nothing here is a secret.
/// </summary>
public sealed class AlertBoxSettings
{
    public AlertMediaConfig Default { get; set; } = new();
    public Dictionary<string, AlertMediaConfig> Overrides { get; set; } = new();
}

public sealed class AlertMediaConfig
{
    public string? ImagePath { get; set; }
    public string? SoundPath { get; set; }

    /// <summary>Null means "inherit from AlertBoxSettings.Default" when this is an override entry.</summary>
    public int? DisplaySeconds { get; set; }
}

/// <summary>
/// Optional donation-alert sources. Only the StreamElements token is a real
/// secret (DPAPI-encrypted, like everything else). The Liberapay username and
/// BTC/ETH addresses are meant to be shared publicly for receiving donations, and
/// the Etherscan key is a low-sensitivity rate-limit key, not a financial secret
/// — plaintext is fine for all of those, same treatment as TwitchClientId/KickChannelSlug.
/// </summary>
public sealed class DonationSettings
{
    public string? StreamElementsEncryptedJwtToken { get; set; }
    public string? LiberapayUsername { get; set; }
    public string? BitcoinAddress { get; set; }
    public string? EthereumAddress { get; set; }
    public string? EtherscanApiKey { get; set; }
}

/// <summary>Persisted Twitch connection — access/refresh tokens are DPAPI-encrypted, never plaintext on disk. ClientId is not a secret (public client, no app secret involved).</summary>
public sealed class TwitchAccountSettings
{
    public required string ClientId { get; set; }
    public required string EncryptedAccessToken { get; set; }
    public required string EncryptedRefreshToken { get; set; }
    public int ExpiresInSeconds { get; set; }
    public DateTimeOffset ObtainedAtUtc { get; set; }
}

/// <summary>
/// Just remembers the OAuth client so re-authorizing doesn't need retyping —
/// the actual access/refresh tokens live in EncryptedDataStore, managed by
/// Google's own auth library.
/// </summary>
public sealed class YouTubeAccountSettings
{
    public required string ClientId { get; set; }
    public required string EncryptedClientSecret { get; set; }
}
