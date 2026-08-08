using System.Text.Json;
using NekoChat.Core.Twitch;

namespace NekoChat.Core.Config;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        if (!File.Exists(LocalPaths.SettingsFilePath))
            return new AppSettings();

        var json = File.ReadAllText(LocalPaths.SettingsFilePath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(LocalPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(LocalPaths.SettingsFilePath, json);
    }

    public static TwitchToken ToRuntime(TwitchAccountSettings stored) => new(
        SecretProtector.Unprotect(stored.EncryptedAccessToken),
        SecretProtector.Unprotect(stored.EncryptedRefreshToken),
        stored.ExpiresInSeconds,
        [])
    {
        ObtainedAtUtc = stored.ObtainedAtUtc,
    };

    public static TwitchAccountSettings ToStored(string clientId, TwitchToken token) => new()
    {
        ClientId = clientId,
        EncryptedAccessToken = SecretProtector.Protect(token.AccessToken),
        EncryptedRefreshToken = SecretProtector.Protect(token.RefreshToken),
        ExpiresInSeconds = token.ExpiresInSeconds,
        ObtainedAtUtc = token.ObtainedAtUtc,
    };

    public static (string ClientId, string ClientSecret) ToRuntime(YouTubeAccountSettings stored) =>
        (stored.ClientId, SecretProtector.Unprotect(stored.EncryptedClientSecret));

    public static YouTubeAccountSettings ToStored(string clientId, string clientSecret) => new()
    {
        ClientId = clientId,
        EncryptedClientSecret = SecretProtector.Protect(clientSecret),
    };

    public static string? ToRuntimeStreamElementsToken(DonationSettings? stored) =>
        stored?.StreamElementsEncryptedJwtToken is { } encrypted ? SecretProtector.Unprotect(encrypted) : null;

    public static string ToStoredStreamElementsToken(string jwtToken) => SecretProtector.Protect(jwtToken);
}
