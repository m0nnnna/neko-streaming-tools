using NekoTrends.Core.Models;

namespace NekoTrends.Core.Discovery;

/// <summary>
/// Applies the user's keyword rules to discovered streams, stamping each with the rules it matched.
///
/// Matching is plain case-insensitive substring across the fields a viewer would actually read —
/// title, category, tags, channel name. Unlike <see cref="VTuberClassifier"/> this deliberately
/// does not enforce word boundaries: these are user-authored terms like "hololive" or "minecraft"
/// where a substring hit is almost always what was meant.
/// </summary>
public static class RuleEngine
{
    public static IReadOnlyList<LiveStream> Apply(
        IReadOnlyList<LiveStream> streams, IReadOnlyList<KeywordRule> rules)
    {
        var active = rules.Where(r => r.Enabled && r.Terms.Count > 0).ToList();
        if (active.Count == 0)
            return streams;

        return streams.Select(stream =>
        {
            var matched = active
                .Where(rule => Matches(stream, rule))
                .Select(rule => rule.Name)
                .ToList();

            return matched.Count == 0 ? stream : stream with { MatchedRules = matched };
        }).ToList();
    }

    public static bool Matches(LiveStream stream, KeywordRule rule)
    {
        foreach (var term in rule.Terms)
        {
            if (string.IsNullOrWhiteSpace(term))
                continue;

            if (Contains(stream.DisplayName, term)
                || Contains(stream.Title, term)
                || Contains(stream.Category, term)
                || Contains(stream.Language, term)
                || stream.Tags.Any(tag => Contains(tag, term)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
}
