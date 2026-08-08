using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NekoChat.Core.Models;

namespace NekoChat.App.Converters;

/// <summary>Maps each platform to its brand color for the feed's platform badge.</summary>
public sealed class PlatformBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Twitch = new(Color.FromRgb(0x91, 0x46, 0xFF));
    private static readonly SolidColorBrush YouTube = new(Color.FromRgb(0xFF, 0x00, 0x00));
    private static readonly SolidColorBrush Kick = new(Color.FromRgb(0x53, 0xFC, 0x18));
    private static readonly SolidColorBrush X = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly SolidColorBrush StreamElements = new(Color.FromRgb(0x3E, 0xE6, 0xB4));
    private static readonly SolidColorBrush Liberapay = new(Color.FromRgb(0xF6, 0xC9, 0x15));
    private static readonly SolidColorBrush Bitcoin = new(Color.FromRgb(0xF7, 0x93, 0x1A));
    private static readonly SolidColorBrush Ethereum = new(Color.FromRgb(0x62, 0x7E, 0xEA));
    private static readonly SolidColorBrush Default = new(Colors.Gray);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ChatPlatform.Twitch => Twitch,
        ChatPlatform.YouTube => YouTube,
        ChatPlatform.Kick => Kick,
        ChatPlatform.X => X,
        ChatPlatform.StreamElements => StreamElements,
        ChatPlatform.Liberapay => Liberapay,
        ChatPlatform.Bitcoin => Bitcoin,
        ChatPlatform.Ethereum => Ethereum,
        _ => Default,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
