using NekoChat.Core.Models;

namespace NekoChat.App.ViewModels;

/// <summary>One row in the Alert Box per-kind override list. Empty ImagePath/SoundPath means "use the default".</summary>
public sealed class AlertOverrideEditItem : ObservableObject
{
    public required ChatMessageKind Kind { get; init; }

    public string DisplayName => Kind.ToString();

    private string _imagePath = "";
    public string ImagePath
    {
        get => _imagePath;
        set => SetField(ref _imagePath, value);
    }

    private string _soundPath = "";
    public string SoundPath
    {
        get => _soundPath;
        set => SetField(ref _soundPath, value);
    }
}
