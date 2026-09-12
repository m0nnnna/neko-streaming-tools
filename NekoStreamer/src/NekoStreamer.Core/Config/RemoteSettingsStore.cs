using System.Text.Json;

namespace NekoStreamer.Core.Config;

public static class RemoteSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static RemoteSettings Load()
    {
        if (!File.Exists(LocalPaths.RemoteSettingsFilePath))
            return new RemoteSettings();

        var json = File.ReadAllText(LocalPaths.RemoteSettingsFilePath);
        return JsonSerializer.Deserialize<RemoteSettings>(json, JsonOptions) ?? new RemoteSettings();
    }

    public static void Save(RemoteSettings settings)
    {
        Directory.CreateDirectory(LocalPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(LocalPaths.RemoteSettingsFilePath, json);
    }
}
