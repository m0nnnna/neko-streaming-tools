using System.Text;
using System.Text.Json;

namespace NekoTrends.Core.Alerts;

/// <summary>
/// Persisted alert history, newest last.
///
/// It lives on disk rather than in memory because the background collector is a separate process:
/// alerts that fire while the app is closed still need to be there when it next opens.
/// </summary>
public sealed class AlertLog
{
    private const int MaxRetained = 500;

    private readonly string _filePath;

    public AlertLog(string? filePath = null) => _filePath = filePath ?? LocalPaths.AlertLogFilePath;

    public void Append(IEnumerable<FiredAlert> alerts)
    {
        var builder = new StringBuilder();
        foreach (var alert in alerts)
            builder.Append(JsonSerializer.Serialize(alert)).Append('\n');

        if (builder.Length == 0)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.AppendAllText(_filePath, builder.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing an alert record is not worth failing a refresh over.
        }
    }

    /// <summary>Returns retained alerts, newest first.</summary>
    public IReadOnlyList<FiredAlert> Load()
    {
        if (!File.Exists(_filePath))
            return [];

        var alerts = new List<FiredAlert>();

        try
        {
            foreach (var line in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    if (JsonSerializer.Deserialize<FiredAlert>(line) is { } alert)
                        alerts.Add(alert);
                }
                catch (JsonException)
                {
                    // Skip a torn line rather than losing the whole log.
                }
            }
        }
        catch (IOException)
        {
            return [];
        }

        alerts.Reverse();
        return alerts.Take(MaxRetained).ToList();
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
