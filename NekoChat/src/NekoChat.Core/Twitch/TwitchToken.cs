namespace NekoChat.Core.Twitch;

public sealed record TwitchToken(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    IReadOnlyList<string> Scopes)
{
    public DateTimeOffset ObtainedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAtUtc => ObtainedAtUtc + TimeSpan.FromSeconds(ExpiresInSeconds);
}
