namespace NekoChat.Core.Twitch;

/// <summary>Response from POST id.twitch.tv/oauth2/device — what to show the user
/// and what to keep polling with.</summary>
public sealed record DeviceCodeInfo(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds);
