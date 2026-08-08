using System.Configuration;
using System.Data;
using System.Windows;
using NekoChat.App.ViewModels;

namespace NekoChat.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // MainWindow (private control panel), OverlayWindow (chat feed), and
        // AlertBoxWindow (follow/sub/donation/etc popups) all share one
        // MainViewModel — both public windows are separate OBS Window Capture
        // sources so the streamer can position/size them independently.
        var viewModel = new MainViewModel();
        var overlay = new OverlayWindow(viewModel);
        var alertBox = new AlertBoxWindow(viewModel);
        var mainWindow = new MainWindow(viewModel, overlay, alertBox);

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

