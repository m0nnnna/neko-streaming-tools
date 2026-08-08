using System.Globalization;
using System.Windows.Data;
using NekoChat.Core.Models;

namespace NekoChat.App.Converters;

public sealed class KindToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ChatMessageKind.Follow => "⭐ ",
        ChatMessageKind.Subscribe => "💎 ",
        ChatMessageKind.GiftSub => "🎁 ",
        ChatMessageKind.Raid => "⚔️ ",
        ChatMessageKind.Host => "📣 ",
        ChatMessageKind.SuperChat or ChatMessageKind.SuperSticker => "💰 ",
        ChatMessageKind.NewMember or ChatMessageKind.MemberMilestone => "🌟 ",
        ChatMessageKind.Donation => "💸 ",
        _ => "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
