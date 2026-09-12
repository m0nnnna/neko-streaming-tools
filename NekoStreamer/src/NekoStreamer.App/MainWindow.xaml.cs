using System.Linq;
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
using NekoStreamer.App.ViewModels;

namespace NekoStreamer.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Key[] ModifierOnlyKeys =
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
        Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin, Key.System,
    };

    private readonly GlobalHotkey _clipHotkey;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        Notifications.Initialize();
        Closed += (_, _) => Notifications.Dispose();

        _clipHotkey = new GlobalHotkey(this, () => (DataContext as MainViewModel)?.SaveClipCommand.Execute(null));
        _clipHotkey.RegistrationFailed += () =>
            (DataContext as MainViewModel)?.Log("Couldn't register the clip hotkey — it may already be in use by another app.");

        if (DataContext is MainViewModel vm)
        {
            _clipHotkey.SetBinding(vm.ClipHotkeyModifiers, vm.ClipHotkeyKey);
            vm.HotkeyChanged += (modifiers, key) => _clipHotkey.SetBinding(modifiers, key);
        }

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsCapturingHotkey)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            vm.IsCapturingHotkey = false;
            e.Handled = true;
            return;
        }

        if (ModifierOnlyKeys.Contains(key))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
            return; // require at least one modifier — a bare key would hijack normal typing everywhere

        vm.ApplyCapturedHotkey(modifiers, key);
    }
}