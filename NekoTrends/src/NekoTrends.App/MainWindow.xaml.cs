using System.Windows;
using NekoTrends.App.ViewModels;

namespace NekoTrends.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private AlertToastWindow? _toast;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        DataContext = _viewModel;

        _viewModel.AlertsFired += ShowToast;

        Loaded += async (_, _) => await _viewModel.StartAsync();
        Closed += (_, _) =>
        {
            _viewModel.AlertsFired -= ShowToast;
            _viewModel.Stop();
            _toast?.Close();
        };
    }

    private void ShowToast(IReadOnlyList<Core.Alerts.FiredAlert> alerts)
    {
        // Replace any popup still on screen so a burst of refreshes can't stack windows.
        _toast?.Close();
        _toast = new AlertToastWindow(alerts);
        _toast.Closed += (_, _) => _toast = null;
        _toast.Show();
    }

    /// <summary>
    /// PasswordBox.Password deliberately isn't a DependencyProperty — WPF avoids exposing secrets
    /// to the binding system — so it has to be pushed across by hand.
    /// </summary>
    private void OnYouTubeKeyChanged(object sender, RoutedEventArgs e) =>
        _viewModel.YouTubeApiKey = YouTubeKeyBox.Password;
}
