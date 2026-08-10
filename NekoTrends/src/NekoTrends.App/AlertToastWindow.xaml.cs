using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NekoTrends.Core.Alerts;

namespace NekoTrends.App;

/// <summary>
/// A self-dismissing popup in the bottom-right corner, in the same spirit as NekoChat's Alert Box.
///
/// Hand-rolled rather than a Windows toast notification because those require a registered AUMID
/// and Start Menu shortcut to appear at all — a lot of install-time machinery for a tool that's
/// meant to run from an unzipped folder.
/// </summary>
public partial class AlertToastWindow : Window
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(8);

    /// <summary>Showing every alert from a large batch would cover the screen; the rest stay on the Alerts tab.</summary>
    private const int MaxShown = 4;

    private readonly DispatcherTimer _dismissTimer;

    public AlertToastWindow(IReadOnlyList<FiredAlert> alerts)
    {
        InitializeComponent();

        AlertList.ItemsSource = alerts.Take(MaxShown).ToList();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width;
        Loaded += (_, _) => Top = workArea.Bottom - ActualHeight;

        _dismissTimer = new DispatcherTimer { Interval = VisibleFor };
        _dismissTimer.Tick += (_, _) => Close();
        _dismissTimer.Start();

        // Hovering keeps it up long enough to actually click through.
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => _dismissTimer.Start();
        Closed += (_, _) => _dismissTimer.Stop();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Close();

    private void OnAlertClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string url } || string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser registered.
        }

        Close();
    }
}
