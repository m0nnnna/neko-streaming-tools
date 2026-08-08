using System.Globalization;
using System.Windows.Data;
using NekoChat.Core.Models;

namespace NekoChat.App.Converters;

/// <summary>Chat messages read "Author: body"; alerts read "Author body" (body is already a full phrase, e.g. "followed").</summary>
public sealed class KindToSeparatorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ChatMessageKind.Message or null ? ": " : " ";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
