using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NekoChat.App.ViewModels;
using NekoChat.Core.Config;
using NekoChat.Core.Models;
using WpfAnimatedGif;

namespace NekoChat.App;

/// <summary>
/// Borderless, transparent window meant to be captured by OBS as a separate
/// Window Capture source from the chat overlay — shows a big image/GIF + caption
/// for follow/sub/raid/donation/etc alerts, plays a sound, then goes back to
/// fully invisible until the next one. Alerts that arrive while one is already
/// showing queue up rather than overlapping.
/// </summary>
public partial class AlertBoxWindow : Window
{
    private readonly Queue<ChatMessage> _queue = new();
    private readonly object _queueLock = new();
    private readonly DispatcherTimer _hideTimer = new();
    private readonly MediaPlayer _player = new();
    private bool _isPlaying;
    private bool _resizing;
    private Point _resizeStart;

    public AlertBoxWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        viewModel.Messages.CollectionChanged += Messages_CollectionChanged;

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            PlayNext();
        };
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
            return;

        var enqueuedAny = false;
        foreach (ChatMessage message in e.NewItems)
        {
            if (message.Kind == ChatMessageKind.Message)
                continue;

            lock (_queueLock)
                _queue.Enqueue(message);
            enqueuedAny = true;
        }

        if (enqueuedAny && !_isPlaying)
            PlayNext();
    }

    /// <summary>Lets the control panel's "Test Alert" button preview media/positioning without waiting for a real alert.</summary>
    public void EnqueueTestAlert(ChatMessage message)
    {
        lock (_queueLock)
            _queue.Enqueue(message);

        if (!_isPlaying)
            PlayNext();
    }

    private void PlayNext()
    {
        ChatMessage next;
        lock (_queueLock)
        {
            if (_queue.Count == 0)
            {
                _isPlaying = false;
                AlertContent.Visibility = Visibility.Collapsed;
                return;
            }

            next = _queue.Dequeue();
        }

        _isPlaying = true;
        AlertContent.DataContext = next;
        AlertContent.Visibility = Visibility.Visible;

        var config = ResolveConfig(next.Kind);

        if (!string.IsNullOrWhiteSpace(config.ImagePath) && File.Exists(config.ImagePath))
        {
            ImageBehavior.SetAnimatedSource(AlertImage, new BitmapImage(new Uri(config.ImagePath)));
            AlertImage.Visibility = Visibility.Visible;
        }
        else
        {
            AlertImage.Visibility = Visibility.Collapsed;
        }

        _player.Stop();
        if (!string.IsNullOrWhiteSpace(config.SoundPath) && File.Exists(config.SoundPath))
        {
            _player.Open(new Uri(config.SoundPath));
            _player.Play();
        }

        _hideTimer.Interval = TimeSpan.FromSeconds(config.DisplaySeconds ?? 6);
        _hideTimer.Start();
    }

    /// <summary>
    /// Reloads settings fresh on every alert (cheap, small file) rather than
    /// caching — lets the streamer tweak alert media/sounds live and have the
    /// very next alert pick it up without restarting anything.
    /// </summary>
    private static AlertMediaConfig ResolveConfig(ChatMessageKind kind)
    {
        var alertBox = SettingsStore.Load().AlertBox ?? new AlertBoxSettings();
        var fallback = alertBox.Default;

        if (!alertBox.Overrides.TryGetValue(kind.ToString(), out var over))
        {
            return new AlertMediaConfig
            {
                ImagePath = fallback.ImagePath,
                SoundPath = fallback.SoundPath,
                DisplaySeconds = fallback.DisplaySeconds ?? 6,
            };
        }

        return new AlertMediaConfig
        {
            ImagePath = over.ImagePath ?? fallback.ImagePath,
            SoundPath = over.SoundPath ?? fallback.SoundPath,
            DisplaySeconds = over.DisplaySeconds ?? fallback.DisplaySeconds ?? 6,
        };
    }

    private void RootGrid_MouseEnter(object sender, MouseEventArgs e)
    {
        DragHandle.Opacity = 1;
        ResizeGrip.Opacity = 1;
    }

    private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        DragHandle.Opacity = 0;
        ResizeGrip.Opacity = 0;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        _resizeStart = e.GetPosition(this);
        ResizeGrip.CaptureMouse();
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing)
            return;

        var pos = e.GetPosition(this);
        Width = Math.Max(MinWidth, Width + (pos.X - _resizeStart.X));
        Height = Math.Max(MinHeight, Height + (pos.Y - _resizeStart.Y));
        _resizeStart = pos;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _resizing = false;
        ResizeGrip.ReleaseMouseCapture();
    }
}
