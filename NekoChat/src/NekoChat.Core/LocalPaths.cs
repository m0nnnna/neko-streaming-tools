namespace NekoChat.Core;

public static class LocalPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NekoChat");

    public static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");
}
