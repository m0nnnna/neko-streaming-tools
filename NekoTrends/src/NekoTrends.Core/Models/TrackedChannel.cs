namespace NekoTrends.Core.Models;

/// <summary>
/// A channel the user explicitly pinned. Tracked channels are polled directly rather than
/// discovered, which is what lets them bypass Twitch's top-1,000 scan floor — a small VTuber
/// invisible to tag discovery still shows up if pinned.
/// </summary>
public sealed class TrackedChannel
{
    public StreamPlatform Platform { get; set; }

    /// <summary>Twitch login name, YouTube channel id (UC…), or Kick slug — whatever that platform polls by.</summary>
    public string Id { get; set; } = "";

    /// <summary>Optional label shown before the channel has ever been seen live.</summary>
    public string? Note { get; set; }

    public string Key => $"{Platform}:{Id}";
}

/// <summary>
/// A saved keyword rule — a way to follow a group (an agency, a game, a language) without
/// naming every channel in it. Rules label and filter what discovery already found; a rule
/// can additionally be pushed into YouTube search, which is opt-in because it costs quota.
/// </summary>
public sealed class KeywordRule
{
    public string Name { get; set; } = "";

    public List<string> Terms { get; set; } = [];

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set, this rule's terms are also used as YouTube search queries. Off by default:
    /// each extra search term costs 100 of the 10,000 daily quota units.
    /// </summary>
    public bool SearchYouTube { get; set; }
}
