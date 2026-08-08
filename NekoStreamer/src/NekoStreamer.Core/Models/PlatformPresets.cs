namespace NekoStreamer.Core.Models;

/// <summary>
/// Twitch and YouTube publish a fixed, documented RTMP ingest URL that works for any
/// account. Kick and X do not — both generate a server URL (and Kick a key too) fresh
/// per broadcast from their own creator dashboard, so there's nothing safe to
/// hardcode for them. Guidance text fills that gap instead of a wrong or stale URL.
/// </summary>
public static class PlatformPresets
{
    public static string? DefaultServerUrl(StreamingPlatform platform) => platform switch
    {
        StreamingPlatform.Twitch => "rtmp://live.twitch.tv/app",
        StreamingPlatform.YouTube => "rtmp://a.rtmp.youtube.com/live2",
        _ => null,
    };

    public static string? SetupHint(StreamingPlatform platform) => platform switch
    {
        StreamingPlatform.Kick => "Copy the server URL and stream key from kick.com/dashboard — Kick generates both fresh per broadcast.",
        StreamingPlatform.X => "Requires X Premium/Premium+. Copy the server URL and key from studio.x.com (Producer > Sources > Create Source > RTMP).",
        _ => null,
    };
}
