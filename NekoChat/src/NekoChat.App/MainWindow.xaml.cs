using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NekoChat.App.ViewModels;

namespace NekoChat.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly AlertBoxWindow _alertBox;

    public MainWindow(MainViewModel viewModel, OverlayWindow overlay, AlertBoxWindow alertBox)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        DataContext = viewModel;
        _overlay = overlay;
        _overlay.IsVisibleChanged += (_, _) =>
            ToggleOverlayButton.Content = _overlay.IsVisible ? "Hide Overlay" : "Show Overlay";

        _alertBox = alertBox;
        _alertBox.IsVisibleChanged += (_, _) =>
            ToggleAlertBoxButton.Content = _alertBox.IsVisible ? "Hide Alert Box" : "Show Alert Box";
        viewModel.TestAlertRequested += _alertBox.EnqueueTestAlert;

        // PasswordBox can't be bound, so sync in whatever was loaded from settings.
        if (!string.IsNullOrEmpty(viewModel.YouTubeClientSecret))
            YouTubeClientSecretBox.Password = viewModel.YouTubeClientSecret;
        if (!string.IsNullOrEmpty(viewModel.StreamElementsToken))
            StreamElementsTokenBox.Password = viewModel.StreamElementsToken;
    }

    // PasswordBox.Password can't be bound directly via XAML for security reasons —
    // this is the standard workaround.
    private void YouTubeClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.YouTubeClientSecret = YouTubeClientSecretBox.Password;
    }

    private void StreamElementsTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.StreamElementsToken = StreamElementsTokenBox.Password;
    }

    private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay.IsVisible)
            _overlay.Hide();
        else
            _overlay.Show();
    }

    private void ToggleAlertBoxButton_Click(object sender, RoutedEventArgs e)
    {
        if (_alertBox.IsVisible)
            _alertBox.Hide();
        else
            _alertBox.Show();
    }
}