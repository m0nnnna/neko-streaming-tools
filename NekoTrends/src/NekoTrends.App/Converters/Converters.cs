using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NekoTrends.Core.Alerts;
using NekoTrends.Core.Models;

namespace NekoTrends.App.Converters;

/// <summary>Maps a platform to its brand colour so a card's origin reads without reading text.</summary>
public sealed class PlatformBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            StreamPlatform.Twitch => "TwitchBrush",
            StreamPlatform.YouTube => "YouTubeBrush",
            StreamPlatform.Kick => "KickBrush",
            _ => "TextMutedBrush",
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Newcomers get the amber "new" accent; surging channels get the green one.</summary>
public sealed class MomentumBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "NewcomerBrush" : "SurgeBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Compacts viewer counts — a wall of "1247893" is unreadable at card density.</summary>
public sealed class ViewerCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int viewers)
            return "—";

        return viewers switch
        {
            >= 1_000_000 => (viewers / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
            >= 10_000 => (viewers / 1_000.0).ToString("0", CultureInfo.InvariantCulture) + "K",
            >= 1_000 => (viewers / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K",
            _ => viewers.ToString(CultureInfo.InvariantCulture),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Renders uptime as "3h 12m" — how long they've been live is part of the story.</summary>
public sealed class UptimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TimeSpan uptime || uptime < TimeSpan.Zero)
            return "";

        return uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
            : $"{uptime.Minutes}m";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            null => Visibility.Collapsed,
            string s when string.IsNullOrWhiteSpace(s) => Visibility.Collapsed,
            _ => Visibility.Visible,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Set to true to show the element when the bound value is false.</summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert)
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PlatformNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is StreamPlatform platform ? platform.ToString() : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AlertTriggerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            AlertTrigger.WentLive => "when they go live",
            AlertTrigger.ViewerThreshold => "when they pass a viewer count",
            AlertTrigger.MomentumSpike => "when they spike above their baseline",
            _ => "",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Renders a term list as "a · b · c" for the rule summary line.</summary>
public sealed class JoinConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable<string> items ? string.Join(" · ", items) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = value is not int count || count == 0;
        var show = Invert ? isEmpty : !isEmpty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
