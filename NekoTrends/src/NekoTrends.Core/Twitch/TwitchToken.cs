namespace NekoTrends.Core.Twitch;

public sealed record TwitchToken(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    IReadOnlyList<string> Scopes)
{
    public DateTimeOffset ObtainedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAtUtc => ObtainedAtUtc.AddSeconds(ExpiresInSeconds);

    /// <summary>Treated as expired a minute early so a refresh never races a request already in flight.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc.AddMinutes(-1);
}

public sealed record DeviceCodeInfo(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds);
