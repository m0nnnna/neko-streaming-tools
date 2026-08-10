using System.Text.Json;
using NekoTrends.Core.Discovery;
using NekoTrends.Core.Http;

namespace NekoTrends.Core.Kick;

/// <summary>A roster candidate awaiting the user's yes/no.</summary>
public sealed record KickCandidate
{
    public required string Slug { get; init; }
    public required string DisplayName { get; init; }
    public int Followers { get; init; }
    public bool IsLive { get; init; }
    public string Url => $"https://kick.com/{Slug}";
}

/// <summary>
/// Proposes channels for the Kick roster by running VTuber keyword searches against Kick's
/// search endpoint.
///
/// This only ever suggests — it never auto-adds. Kick's search matches on raw substrings of the
/// channel name, so a query for "live2d" happily returns "live2dance4ever" and similar unrelated
/// channels. Requiring a human yes/no is what keeps the Kick feed honest, given the platform
/// gives us no tag to verify against.
/// </summary>
public sealed class KickRosterSuggester
{
    public async Task<IReadOnlyList<KickCandidate>> SuggestAsync(
        IReadOnlyCollection<string> alreadyInRoster,
        IReadOnlyCollection<string> dismissed,
        CancellationToken ct = default)
    {
        var excluded = new HashSet<string>(alreadyInRoster, StringComparer.OrdinalIgnoreCase);
        excluded.UnionWith(dismissed);

        var candidates = new Dictionary<string, KickCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in VTuberClassifier.SearchTerms)
        {
            ct.ThrowIfCancellationRequested();

            string json;
            try
            {
                json = await CurlHttpClient.GetStringAsync(
                    $"https://kick.com/api/search?searched_word={Uri.EscapeDataString(term)}",
                    ct,
                    CurlHttpClient.BrowserUserAgent);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(json))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                CollectChannels(doc.RootElement, excluded, candidates);
            }
            catch (JsonException)
            {
                // Kick occasionally answers search with an HTML error page; skip that term.
            }
        }

        return candidates.Values
            .OrderByDescending(c => c.IsLive)
            .ThenByDescending(c => c.Followers)
            .ToList();
    }

    private static void CollectChannels(
        JsonElement root,
        HashSet<string> excluded,
        Dictionary<string, KickCandidate> into)
    {
        if (!root.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
            return;

        foreach (var channel in channels.EnumerateArray())
        {
            if (channel.TryGetProperty("slug", out var slugEl) is false || slugEl.GetString() is not { Length: > 0 } slug)
                continue;

            if (excluded.Contains(slug) || into.ContainsKey(slug))
                continue;

            var user = channel.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;

            var displayName = user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("username", out var nameEl)
                && nameEl.GetString() is { Length: > 0 } name
                    ? name
                    : slug;

            var bio = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("bio", out var bioEl)
                ? bioEl.GetString()
                : null;

            // Kick's search is a bare substring match, so re-check the result actually reads as a
            // VTuber on a word boundary. This is what rejects "live2dance4ever" from a "live2d" query.
            if (!VTuberClassifier.LooksLikeVTuber(slug, displayName, bio))
                continue;

            into[slug] = new KickCandidate
            {
                Slug = slug,
                DisplayName = displayName,
                Followers = channel.TryGetProperty("followersCount", out var f) && f.TryGetInt32(out var followers)
                    ? followers
                    : 0,
                IsLive = channel.TryGetProperty("isLive", out var live) && live.ValueKind == JsonValueKind.True,
            };
        }
    }
}
