using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace NekoStreamer.App;

/// <summary>Registers a system-wide hotkey that fires even when the app isn't focused — used for the instant-clip trigger while OBS/a game has focus. Rebindable at runtime via <see cref="SetBinding"/>.</summary>
internal sealed class GlobalHotkey
{
    private const int WM_HOTKEY = 0x0312;
    private static int _nextId = 0xB000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private readonly Action _onPressed;
    private HwndSource? _source;
    private bool _sourceReady;
    private int? _registeredId;
    private (ModifierKeys Modifiers, Key Key)? _pending;

    /// <summary>Fires when a <see cref="SetBinding"/> call fails to register — e.g. the combo is already claimed by another app. The previously-registered binding (if any) stays active.</summary>
    public event Action? RegistrationFailed;

    public GlobalHotkey(Window window, Action onPressed)
    {
        _window = window;
        _onPressed = onPressed;

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);
            _sourceReady = true;

            if (_pending is { } binding)
                Apply(binding.Modifiers, binding.Key);
        };

        window.Closed += (_, _) =>
        {
            if (_registeredId is { } id)
                UnregisterHotKey(new WindowInteropHelper(window).Handle, id);
            _source?.RemoveHook(WndProc);
        };
    }

    public void SetBinding(ModifierKeys modifiers, Key key)
    {
        if (!_sourceReady)
        {
            _pending = (modifiers, key);
            return;
        }

        Apply(modifiers, key);
    }

    private void Apply(ModifierKeys modifiers, Key key)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var newId = _nextId++;

        if (!RegisterHotKey(hwnd, newId, (uint)modifiers, vk))
        {
            RegistrationFailed?.Invoke();
            return;
        }

        if (_registeredId is { } oldId)
            UnregisterHotKey(hwnd, oldId);

        _registeredId = newId;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registeredId == wParam.ToInt32())
        {
            _onPressed();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
