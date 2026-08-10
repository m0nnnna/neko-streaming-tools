using System.Text.RegularExpressions;

namespace NekoTrends.Core.Discovery;

/// <summary>
/// Decides whether a stream looks like a VTuber broadcast from its text metadata.
///
/// Platform support for this is wildly uneven, which is why this exists at all:
/// Twitch has a real, widely-used "VTuber" freeform tag, so there the match is
/// essentially authoritative. YouTube has no tag concept for it, so we lean on
/// title/description wording. Kick has neither — verified against its live browse
/// API, where every stream returns an empty <c>tags</c> array and no VTuber
/// category exists at all — so on Kick this is only ever used to *suggest*
/// channels for the user-curated roster, never to include one automatically.
/// </summary>
public static class VTuberClassifier
{
    /// <summary>
    /// Requires a trailing word boundary but deliberately not a leading one.
    ///
    /// The trailing boundary is what does the real work of rejecting Kick's substring-search
    /// false positives — "live2dance4ever" and "vtubercypress" both die on the character that
    /// follows the term. A leading boundary adds nothing there, and costs real recall: names
    /// like "starvtuber" and "tsubakisoulvtuber" glue the term onto a preceding word, which is
    /// an extremely common way VTubers name their channels.
    ///
    /// JP/KR/RU terms are included because a large share of the scene tags only in its own language.
    /// </summary>
    private static readonly Regex Pattern = new(
        @"(v-?tuber|v-?tubing|vtub|virtual\s+(?:youtuber|streamer|idol)|live\s?2d|"
        + @"バーチャル(?:ユーチューバー|youtuber)?|ブイチューバー|버튜버|버추얼|втубер|виртуальн)(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when any Twitch-style tag is a VTuber tag. This is the high-confidence path.</summary>
    public static bool HasVTuberTag(IEnumerable<string>? tags) =>
        tags is not null && tags.Any(t => !string.IsNullOrWhiteSpace(t) && Pattern.IsMatch(t));

    /// <summary>
    /// Fuzzy match across free text (channel name, stream title, bio). Lower confidence than
    /// <see cref="HasVTuberTag"/> — a stream can mention a VTuber without being one.
    /// </summary>
    public static bool LooksLikeVTuber(params string?[] textFields) =>
        textFields.Any(f => !string.IsNullOrWhiteSpace(f) && Pattern.IsMatch(f));

    /// <summary>Search terms used to trawl a platform that has no VTuber tag of its own.</summary>
    public static IReadOnlyList<string> SearchTerms { get; } =
    [
        "vtuber",
        "virtual youtuber",
        "virtual streamer",
        "vtuber live",
        "バーチャルyoutuber",
    ];
}
