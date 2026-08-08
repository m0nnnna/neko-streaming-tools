using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NekoChat.Core.Models;

namespace NekoChat.App.Converters;

/// <summary>Readable text color for each platform's badge background.</summary>
public sealed class PlatformTextBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ChatPlatform.Kick => Brushes.Black,
        ChatPlatform.StreamElements => Brushes.Black,
        ChatPlatform.Liberapay => Brushes.Black,
        _ => Brushes.White,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
