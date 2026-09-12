namespace NekoStreamer.Core;

/// <summary>
/// All runtime data (staged binaries, segment buffer, settings) lives on a local
/// disk under LocalAppData. Project source/config can live on a network drive, but
/// nothing the pipeline reads or writes at runtime should — see LocalBinaryStager
/// for why (SMB shares here refuse to execute binaries, and FileSystemWatcher-style
/// polling is also more reliable locally).
/// </summary>
public static class LocalPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NekoStreamer");

    public static string BufferDirectory => Path.Combine(DataDirectory, "buffer");

    public static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    public static string RemoteSettingsFilePath => Path.Combine(DataDirectory, "remote.json");

    /// <summary>Under Videos, not DataDirectory — clips are user-facing output, not runtime state.</summary>
    public static string ClipsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "NekoStreamer Clips");

    /// <summary>Under Videos, not DataDirectory — same reasoning as ClipsDirectory.</summary>
    public static string RecordingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "NekoStreamer Recordings");
}
