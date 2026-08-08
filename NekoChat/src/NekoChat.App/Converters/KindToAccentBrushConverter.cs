using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NekoChat.Core.Models;

namespace NekoChat.App.Converters;

/// <summary>Maps a feed entry's Kind to a translucent row background — a neutral
/// dark tint for normal chat, a colored tint for alerts so they stand out on stream.</summary>
public sealed class KindToAccentBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Message = new(Color.FromArgb(0x59, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush Follow = new(Color.FromArgb(0xB0, 0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush Subscribe = new(Color.FromArgb(0xB0, 0x9B, 0x51, 0xE0));
    private static readonly SolidColorBrush GiftSub = new(Color.FromArgb(0xB0, 0xE0, 0x51, 0xA8));
    private static readonly SolidColorBrush Raid = new(Color.FromArgb(0xB0, 0xE8, 0x5D, 0x2C));
    private static readonly SolidColorBrush Host = new(Color.FromArgb(0xB0, 0xE8, 0x9A, 0x2C));
    private static readonly SolidColorBrush SuperChatBrush = new(Color.FromArgb(0xB0, 0xE0, 0xB8, 0x1F));
    private static readonly SolidColorBrush Membership = new(Color.FromArgb(0xB0, 0x1F, 0xB8, 0xA8));
    private static readonly SolidColorBrush Donation = new(Color.FromArgb(0xB0, 0x2E, 0xB8, 0x4A));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ChatMessageKind.Follow => Follow,
        ChatMessageKind.Subscribe => Subscribe,
        ChatMessageKind.GiftSub => GiftSub,
        ChatMessageKind.Raid => Raid,
        ChatMessageKind.Host => Host,
        ChatMessageKind.SuperChat or ChatMessageKind.SuperSticker => SuperChatBrush,
        ChatMessageKind.NewMember or ChatMessageKind.MemberMilestone => Membership,
        ChatMessageKind.Donation => Donation,
        _ => Message,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
