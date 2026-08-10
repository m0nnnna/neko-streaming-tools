using System.Text.Json;

namespace NekoTrends.Core.Config;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        if (!File.Exists(LocalPaths.SettingsFilePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(LocalPaths.SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings file should never block startup — the user can just reconnect.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(LocalPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(LocalPaths.SettingsFilePath, json);
    }

    public static string? ReadYouTubeApiKey(YouTubeSettings? stored) =>
        string.IsNullOrEmpty(stored?.EncryptedApiKey) ? null : SecretProtector.Unprotect(stored.EncryptedApiKey);

    public static YouTubeSettings StoreYouTubeApiKey(string apiKey, YouTubeSettings? existing = null)
    {
        var settings = existing ?? new YouTubeSettings();
        settings.EncryptedApiKey = SecretProtector.Protect(apiKey);
        return settings;
    }
}
