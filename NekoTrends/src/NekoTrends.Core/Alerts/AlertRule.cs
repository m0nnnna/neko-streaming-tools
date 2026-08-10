namespace NekoTrends.Core.Alerts;

public enum AlertTrigger
{
    /// <summary>Fires on the transition from offline to live, not for every refresh while live.</summary>
    WentLive,

    /// <summary>Fires when viewers cross <see cref="AlertRule.Threshold"/> upward.</summary>
    ViewerThreshold,

    /// <summary>Fires when a stream exceeds <see cref="AlertRule.Threshold"/> times its own baseline.</summary>
    MomentumSpike,
}

public sealed class AlertRule
{
    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public AlertTrigger Trigger { get; set; } = AlertTrigger.WentLive;

    /// <summary>Viewer count for <see cref="AlertTrigger.ViewerThreshold"/>, or a multiple for a spike.</summary>
    public double Threshold { get; set; } = 2.0;

    /// <summary>
    /// When true the rule only considers watchlist channels. Discovery covers thousands of
    /// streams, so an unscoped "went live" rule would be unusable noise.
    /// </summary>
    public bool TrackedChannelsOnly { get; set; } = true;

    /// <summary>Restrict to one keyword rule's matches, or null for no restriction.</summary>
    public string? RuleFilter { get; set; }
}

public sealed record FiredAlert
{
    public required DateTimeOffset FiredAtUtc { get; init; }
    public required string RuleName { get; init; }
    public required string ChannelName { get; init; }
    public required string Message { get; init; }
    public required string Url { get; init; }
    public required Models.StreamPlatform Platform { get; init; }

    public string LocalTime => FiredAtUtc.ToLocalTime().ToString("t");
}
