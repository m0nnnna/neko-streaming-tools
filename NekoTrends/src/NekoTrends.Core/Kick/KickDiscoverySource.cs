using System.Globalization;
using System.Text.Json;
using NekoTrends.Core.Discovery;
using NekoTrends.Core.Http;
using NekoTrends.Core.Models;

namespace NekoTrends.Core.Kick;

/// <summary>
/// Kick's VTuber feed, driven entirely by a user-curated roster of channel slugs.
///
/// Unlike Twitch (real VTuber tag) and YouTube (searchable titles), Kick offers no VTuber signal
/// whatsoever: verified against its live browse API, every stream returns an empty <c>tags</c>
/// array and no VTuber category or subcategory exists. A keyword sweep of the top 300 live English
/// streams matched exactly zero channels. So there is nothing to filter on, and the only workable
/// approach is to poll a known list — see <see cref="KickRosterSuggester"/> for how that list gets
/// populated in the first place.
/// </summary>
public sealed class KickDiscoverySource : IDiscoverySource
{
    private readonly IReadOnlyList<string> _rosterSlugs;
    private readonly int _maxConcurrency;

    public StreamPlatform Platform => StreamPlatform.Kick;

    public KickDiscoverySource(IReadOnlyList<string> rosterSlugs, int maxConcurrency = 4)
    {
        _rosterSlugs = rosterSlugs;
        _maxConcurrency = Math.Max(maxConcurrency, 1);
    }

    public async Task<IReadOnlyList<LiveStream>> DiscoverAsync(CancellationToken ct = default)
    {
        if (_rosterSlugs.Count == 0)
            return [];

        // One curl process per channel, throttled — the roster can be hundreds of slugs and
        // spawning that many processes at once would be worse for the machine than for Kick.
        using var gate = new SemaphoreSlim(_maxConcurrency);

        var tasks = _rosterSlugs.Select(async slug =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await TryFetchLiveAsync(slug, ct);
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        return results
            .OfType<LiveStream>()
            .OrderByDescending(s => s.ViewerCount)
            .ToList();
    }

    /// <summary>Returns the channel's stream if it's live, or null if offline or unreachable.</summary>
    private static async Task<LiveStream?> TryFetchLiveAsync(string slug, CancellationToken ct)
    {
        string json;
        try
        {
            json = await CurlHttpClient.GetStringAsync(
                $"https://kick.com/api/v2/channels/{Uri.EscapeDataString(slug)}",
                ct,
                CurlHttpClient.BrowserUserAgent);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // A single dead slug (renamed, banned, rate-limited) must not fail the whole refresh.
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseChannel(doc.RootElement, slug);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static LiveStream? ParseChannel(JsonElement root, string slug)
    {
        // Kick returns livestream: null for an offline channel rather than omitting the property.
        if (!root.TryGetProperty("livestream", out var live) || live.ValueKind is JsonValueKind.Null)
            return null;

        if (live.TryGetProperty("is_live", out var isLive) && isLive.ValueKind == JsonValueKind.False)
            return null;

        var user = root.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;

        var displayName = user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("username", out var username)
            && username.GetString() is { Length: > 0 } name
                ? name
                : slug;

        var category = live.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array
            ? cats.EnumerateArray()
                .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            : null;

        return new LiveStream
        {
            Platform = StreamPlatform.Kick,
            ChannelId = slug,
            DisplayName = displayName,
            Title = live.TryGetProperty("session_title", out var t) ? t.GetString() : null,
            Category = category,
            Language = live.TryGetProperty("lang_iso", out var lang) ? lang.GetString() : null,
            ViewerCount = live.TryGetProperty("viewer_count", out var vc) && vc.TryGetInt32(out var viewers)
                ? viewers
                : 0,
            ThumbnailUrl = ReadThumbnail(live),
            StartedAtUtc = ParseKickTimestamp(live),
            Tags = [],
            Url = $"https://kick.com/{slug}",
            // Kick has no discovery path at all, so everything here came from the watchlist.
            IsTracked = true,
        };
    }

    /// <summary>The channel endpoint often returns thumbnail: null even while live; the browse endpoint carries it.</summary>
    private static string? ReadThumbnail(JsonElement live)
    {
        if (!live.TryGetProperty("thumbnail", out var thumb) || thumb.ValueKind != JsonValueKind.Object)
            return null;

        return thumb.TryGetProperty("url", out var url) ? url.GetString() : null;
    }

    /// <summary>Kick sends "2026-08-10 15:00:30" — no offset, no 'T'. It is UTC despite looking naive.</summary>
    private static DateTimeOffset? ParseKickTimestamp(JsonElement live)
    {
        if (!live.TryGetProperty("start_time", out var start) || start.GetString() is not { Length: > 0 } raw)
            return null;

        return DateTime.TryParseExact(
            raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }
}
