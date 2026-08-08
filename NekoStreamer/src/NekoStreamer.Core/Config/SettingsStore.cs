using System.Text.Json;
using NekoStreamer.Core.Models;

namespace NekoStreamer.Core.Config;

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

    public static DestinationTarget ToRuntime(StoredDestination stored) => new(
        stored.Name,
        stored.RtmpUrl,
        SecretProtector.Unprotect(stored.EncryptedStreamKey));

    public static StoredDestination ToStored(DestinationTarget target, StreamingPlatform platform) => new()
    {
        Name = target.Name,
        RtmpUrl = target.RtmpUrl,
        EncryptedStreamKey = SecretProtector.Protect(target.StreamKey),
        Platform = platform,
    };
}
