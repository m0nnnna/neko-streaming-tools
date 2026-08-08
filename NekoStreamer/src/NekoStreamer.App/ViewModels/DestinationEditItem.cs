using NekoStreamer.Core.Config;
using NekoStreamer.Core.Models;

namespace NekoStreamer.App.ViewModels;

/// <summary>Mutable, bindable wrapper around a DestinationTarget for the WPF editor grid.</summary>
public sealed class DestinationEditItem : ObservableObject
{
    private string _name = "";
    private string _rtmpUrl = "";
    private string _streamKey = "";
    private StreamingPlatform _platform = StreamingPlatform.Custom;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string RtmpUrl
    {
        get => _rtmpUrl;
        set => SetField(ref _rtmpUrl, value);
    }

    public string StreamKey
    {
        get => _streamKey;
        set => SetField(ref _streamKey, value);
    }

    public StreamingPlatform Platform
    {
        get => _platform;
        set
        {
            var previousPreset = PlatformPresets.DefaultServerUrl(_platform);
            if (!SetField(ref _platform, value))
                return;

            // Auto-fill the server URL for platforms with a known-good default, but
            // only when the field looks untouched — never clobber a URL the user
            // typed or pasted themselves.
            var isUntouched = string.IsNullOrWhiteSpace(RtmpUrl) || RtmpUrl == "rtmp://" || RtmpUrl == previousPreset;
            var newPreset = PlatformPresets.DefaultServerUrl(value);
            if (isUntouched && newPreset is not null)
                RtmpUrl = newPreset;

            RaisePropertyChanged(nameof(SetupHint));
        }
    }

    public string? SetupHint => PlatformPresets.SetupHint(Platform);

    public DestinationTarget ToTarget() => new(Name, RtmpUrl, StreamKey);

    public static DestinationEditItem FromStored(StoredDestination stored, DestinationTarget target) => new()
    {
        Name = target.Name,
        RtmpUrl = target.RtmpUrl,
        StreamKey = target.StreamKey,
        Platform = stored.Platform,
    };
}
