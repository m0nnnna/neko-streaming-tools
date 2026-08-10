using System.Text.Json;
using System.Text.Json.Serialization;
using NekoTrends.Core.Alerts;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.History;

/// <summary>
/// The last completed refresh, persisted so the feed renders instantly at launch instead of
/// showing an empty window while Twitch pages and the Kick roster are polled.
///
/// It doubles as the reference point for edge-triggered alerts: because the background collector
/// writes here too, "went live" still fires correctly for channels that came online while the
/// app was closed, rather than dumping a wall of alerts the next time it opens.
/// </summary>
public sealed class SnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;

    public SnapshotCache(string? filePath = null) =>
        _filePath = filePath ?? LocalPaths.SnapshotCacheFilePath;

    private sealed record CachedSnapshot
    {
        public DateTimeOffset FetchedAtUtc { get; init; }
        public List<LiveStream> Streams { get; init; } = [];
        public List<string> SpikingKeys { get; init; } = [];
    }

    public void Save(IReadOnlyList<LiveStream> streams, IReadOnlyList<TrendEntry> trending, double spikeThreshold, DateTimeOffset fetchedAtUtc)
    {
        var payload = new CachedSnapshot
        {
            FetchedAtUtc = fetchedAtUtc,
            Streams = streams.ToList(),
            SpikingKeys = trending.Where(t => t.MomentumRatio >= spikeThreshold).Select(t => t.Stream.Key).ToList(),
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Write-then-move so an interrupted write can't leave an unreadable cache behind.
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimisation; failing to write it must never fail a refresh.
        }
    }

    /// <summary>Returns the cached streams and the state alerts should compare against, or empty on a cold start.</summary>
    public (IReadOnlyList<LiveStream> Streams, DateTimeOffset? FetchedAtUtc, AlertEngine.PreviousState Previous) Load()
    {
        if (!File.Exists(_filePath))
            return ([], null, AlertEngine.PreviousState.Empty);

        try
        {
            var json = File.ReadAllText(_filePath);
            var payload = JsonSerializer.Deserialize<CachedSnapshot>(json, JsonOptions);
            if (payload is null)
                return ([], null, AlertEngine.PreviousState.Empty);

            var previous = new AlertEngine.PreviousState
            {
                LiveKeys = payload.Streams.Select(s => s.Key).ToHashSet(StringComparer.Ordinal),
                ViewersByKey = payload.Streams
                    .GroupBy(s => s.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Max(s => s.ViewerCount), StringComparer.Ordinal),
                SpikingKeys = payload.SpikingKeys.ToHashSet(StringComparer.Ordinal),
            };

            return (payload.Streams, payload.FetchedAtUtc, previous);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return ([], null, AlertEngine.PreviousState.Empty);
        }
    }
}
