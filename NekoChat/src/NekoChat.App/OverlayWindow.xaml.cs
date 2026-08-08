using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using NekoChat.App.ViewModels;

namespace NekoChat.App;

/// <summary>
/// Borderless, transparent window meant to be captured by OBS's Window Capture
/// source — sits on the desktop (e.g. a second monitor) showing the unified feed
/// and combined viewer count for on-stream display. No title bar means drag/resize
/// are hand-rolled instead of relying on OS window chrome.
/// </summary>
public partial class OverlayWindow : Window
{
    private bool _resizing;
    private Point _resizeStart;

    public OverlayWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || FeedListBox.Items.Count == 0)
            return;

        FeedListBox.ScrollIntoView(FeedListBox.Items[^1]);
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void HideButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Hide();
        e.Handled = true;
    }

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
