using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.History;

/// <summary>
/// Append-only viewer-count history, one JSON object per line.
///
/// JSONL rather than a database because the access pattern is trivially append-heavy and
/// read-all-at-startup, and it keeps NekoTrends dependency-free (no SQLite native binary to
/// vendor per-architecture, which matters given how NekoStreamer's vendored MediaMTX binary
/// already complicates that repo). Property names are single letters because at a 15-minute
/// refresh across a few hundred channels this file accumulates ~400k records over the
/// default 14-day retention, and the key names would otherwise dominate the file size.
/// </summary>
public sealed class SnapshotStore
{
    private readonly string _filePath;
    private readonly TimeSpan _retention;

    public SnapshotStore(string? filePath = null, TimeSpan? retention = null)
    {
        _filePath = filePath ?? LocalPaths.HistoryFilePath;
        _retention = retention ?? TimeSpan.FromDays(14);
    }

    private sealed record Row
    {
        [JsonPropertyName("t")] public long T { get; init; }
        [JsonPropertyName("p")] public int P { get; init; }
        [JsonPropertyName("c")] public string C { get; init; } = "";
        [JsonPropertyName("v")] public int V { get; init; }
    }

    public void Append(IEnumerable<LiveStream> streams, DateTimeOffset? nowUtc = null)
    {
        var stamp = (nowUtc ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

        var builder = new StringBuilder();
        foreach (var stream in streams)
        {
            var row = new Row { T = stamp, P = (int)stream.Platform, C = stream.ChannelId, V = stream.ViewerCount };
            builder.Append(JsonSerializer.Serialize(row)).Append('\n');
        }

        if (builder.Length == 0)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.AppendAllText(_filePath, builder.ToString(), Encoding.UTF8);
    }

    /// <summary>Loads retained samples grouped by <see cref="ViewerSample.Key"/>, oldest first within each group.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ViewerSample>> Load(DateTimeOffset? nowUtc = null)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, IReadOnlyList<ViewerSample>>();

        var cutoff = (nowUtc ?? DateTimeOffset.UtcNow) - _retention;
        var grouped = new Dictionary<string, List<ViewerSample>>();

        foreach (var line in File.ReadLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            Row? row;
            try
            {
                row = JsonSerializer.Deserialize<Row>(line);
            }
            catch (JsonException)
            {
                // A torn final line (power loss mid-append) must not poison the whole history.
                continue;
            }

            if (row is null || !Enum.IsDefined(typeof(StreamPlatform), row.P))
                continue;

            var timestamp = DateTimeOffset.FromUnixTimeSeconds(row.T);
            if (timestamp < cutoff)
                continue;

            var sample = new ViewerSample
            {
                TimestampUtc = timestamp,
                Platform = (StreamPlatform)row.P,
                ChannelId = row.C,
                ViewerCount = row.V,
            };

            if (!grouped.TryGetValue(sample.Key, out var list))
                grouped[sample.Key] = list = [];
            list.Add(sample);
        }

        return grouped.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<ViewerSample>)kvp.Value.OrderBy(s => s.TimestampUtc).ToList());
    }

    /// <summary>
    /// Rewrites the file without samples older than the retention window. Cheap enough to run at
    /// startup; writes via a temp file so an interrupted prune can't destroy existing history.
    /// </summary>
    public void Prune(DateTimeOffset? nowUtc = null)
    {
        if (!File.Exists(_filePath))
            return;

        var cutoff = (nowUtc ?? DateTimeOffset.UtcNow) - _retention;
        var tempPath = _filePath + ".tmp";
        var removed = 0;

        using (var writer = new StreamWriter(tempPath, append: false, Encoding.UTF8))
        {
            foreach (var line in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var row = JsonSerializer.Deserialize<Row>(line);
                    if (row is not null && DateTimeOffset.FromUnixTimeSeconds(row.T) >= cutoff)
                    {
                        writer.Write(line);
                        writer.Write('\n');
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // Drop unparseable lines during prune — this is the only place they get cleaned up.
                }

                removed++;
            }
        }

        if (removed == 0)
        {
            File.Delete(tempPath);
            return;
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>Human-readable size for the settings screen, so the file never grows unnoticed.</summary>
    public string DescribeSize()
    {
        if (!File.Exists(_filePath))
            return "no history yet";

        var bytes = new FileInfo(_filePath).Length;
        return bytes < 1024 * 1024
            ? $"{bytes / 1024.0:0.#} KB"
            : (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}
