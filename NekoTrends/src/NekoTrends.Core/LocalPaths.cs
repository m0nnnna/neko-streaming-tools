namespace NekoTrends.Core;

public static class LocalPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NekoTrends");

    public static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>User-curated VTuber roster, primarily for Kick (which exposes no VTuber tag).</summary>
    public static string RosterFilePath => Path.Combine(DataDirectory, "roster.json");

    /// <summary>Append-only viewer-count samples, one JSON object per line. Backs the Trending tab.</summary>
    public static string HistoryFilePath => Path.Combine(DataDirectory, "history.jsonl");

    /// <summary>Last completed refresh, so the feed can render immediately at launch.</summary>
    public static string SnapshotCacheFilePath => Path.Combine(DataDirectory, "last-snapshot.json");

    /// <summary>Alerts that have fired, newest last. Shared between the app and the background collector.</summary>
    public static string AlertLogFilePath => Path.Combine(DataDirectory, "alerts.jsonl");
}
