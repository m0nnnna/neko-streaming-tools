using System.Drawing;
using System.Windows.Forms;

namespace NekoStreamer.App;

/// <summary>
/// Windows notifications for an unpackaged WPF app, via a WinForms NotifyIcon —
/// toast APIs need MSIX/AppUserModelID registration this app doesn't have.
/// ShowBalloonTip is promoted to a real Action Center notification on Windows
/// 10/11, at the cost of needing a permanent tray icon to anchor it to.
/// </summary>
internal static class Notifications
{
    private static NotifyIcon? _icon;

    public static void Initialize()
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "NekoStreamer",
            Visible = true,
        };
    }

    public static void Show(string title, string message, ToolTipIcon icon = ToolTipIcon.Warning)
    {
        if (_icon is null)
            return;

        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(5000);
    }

    public static void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
