using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using NekoStreamer.Core;
using NekoStreamer.Core.Binaries;
using NekoStreamer.Core.Config;
using NekoStreamer.Core.Ingest;
using NekoStreamer.Core.Models;
using NekoStreamer.Core.Pipeline;

namespace NekoStreamer.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<DestinationEditItem, PropertyChangedEventHandler> _destinationWatchers = new();
    private readonly List<double> _bitrateSamples = new();
    private MediaMtxServer? _mediaMtx;
    private RestreamPipeline? _pipeline;
    private DateTimeOffset _sessionStartedAt;
    private int _lastDroppedFrames;
    private int _clipsSavedThisSession;

    public ObservableCollection<DestinationEditItem> Destinations { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ClipItem> Clips { get; } = new();
    public static IReadOnlyList<StreamingPlatform> AvailablePlatforms { get; } =
        Enum.GetValues<StreamingPlatform>();
    public static IReadOnlyList<int> ClipLengthOptions { get; } = new[] { 15, 30, 60, 90, 120 };

    public string IngestServerUrl => "rtmp://localhost:1935";
    public string IngestStreamKey => "live";

    private int _delaySeconds = 30;
    public int DelaySeconds
    {
        get => _delaySeconds;
        set => SetField(ref _delaySeconds, value);
    }

    private int _clipSeconds = 30;
    public int ClipSeconds
    {
        get => _clipSeconds;
        set => SetField(ref _clipSeconds, value);
    }

    private bool _recordSession;
    public bool RecordSession
    {
        get => _recordSession;
        set => SetField(ref _recordSession, value);
    }

    private ModifierKeys _clipHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    public ModifierKeys ClipHotkeyModifiers
    {
        get => _clipHotkeyModifiers;
        private set
        {
            if (SetField(ref _clipHotkeyModifiers, value))
                NotifyHotkeyDisplayChanged();
        }
    }

    private Key _clipHotkeyKey = Key.C;
    public Key ClipHotkeyKey
    {
        get => _clipHotkeyKey;
        private set
        {
            if (SetField(ref _clipHotkeyKey, value))
                NotifyHotkeyDisplayChanged();
        }
    }

    public string ClipHotkeyDisplay => FormatHotkey(ClipHotkeyModifiers, ClipHotkeyKey);
    public string SaveClipButtonLabel => $"Save Clip ({ClipHotkeyDisplay})";
    public string HotkeyButtonLabel => IsCapturingHotkey ? "Press keys... (Esc to cancel)" : $"Set Hotkey: {ClipHotkeyDisplay}";

    private bool _isCapturingHotkey;
    public bool IsCapturingHotkey
    {
        get => _isCapturingHotkey;
        set
        {
            if (SetField(ref _isCapturingHotkey, value))
                RaisePropertyChanged(nameof(HotkeyButtonLabel));
        }
    }

    /// <summary>Fires when the user finishes rebinding the hotkey, so the view can push the new combo into its GlobalHotkey registration.</summary>
    public event Action<ModifierKeys, Key>? HotkeyChanged;

    private void NotifyHotkeyDisplayChanged()
    {
        RaisePropertyChanged(nameof(ClipHotkeyDisplay));
        RaisePropertyChanged(nameof(SaveClipButtonLabel));
        RaisePropertyChanged(nameof(HotkeyButtonLabel));
    }

    private static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private string _statusText = "Stopped";
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private string _healthText = "—";
    public string HealthText
    {
        get => _healthText;
        set => SetField(ref _healthText, value);
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (SetField(ref _isMuted, value))
                RaisePropertyChanged(nameof(PanicMuteButtonLabel));
        }
    }

    public string PanicMuteButtonLabel => IsMuted ? "🔇 Unmute" : "🔊 Panic Mute";

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetField(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                PanicMuteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand AddDestinationCommand { get; }
    public RelayCommand<DestinationEditItem> RemoveDestinationCommand { get; }
    public RelayCommand SaveClipCommand { get; }
    public RelayCommand SetHotkeyCommand { get; }
    public RelayCommand PanicMuteCommand { get; }
    public RelayCommand OpenClipsFolderCommand { get; }
    public RelayCommand<ClipItem> PlayClipCommand { get; }
    public RelayCommand<ClipItem> DeleteClipCommand { get; }

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(async () => await StopAsync(), () => IsRunning);
        AddDestinationCommand = new RelayCommand(() =>
        {
            var item = new DestinationEditItem
            {
                Name = "New destination",
                RtmpUrl = "rtmp://",
                StreamKey = "",
            };
            WatchDestination(item);
            Destinations.Add(item);
            SaveSettings();
        });
        RemoveDestinationCommand = new RelayCommand<DestinationEditItem>(item =>
        {
            if (item is null)
                return;

            UnwatchDestination(item);
            Destinations.Remove(item);
            SaveSettings();
        });
        SaveClipCommand = new RelayCommand(async () => await SaveClipAsync());
        SetHotkeyCommand = new RelayCommand(() => IsCapturingHotkey = !IsCapturingHotkey);
        PanicMuteCommand = new RelayCommand(async () => await TogglePanicMuteAsync(), () => IsRunning);
        OpenClipsFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(LocalPaths.ClipsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LocalPaths.ClipsDirectory}\"") { UseShellExecute = true });
        });
        PlayClipCommand = new RelayCommand<ClipItem>(item =>
        {
            if (item is null)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"Couldn't open clip: {ex.Message}");
            }
        });
        DeleteClipCommand = new RelayCommand<ClipItem>(item =>
        {
            if (item is null)
                return;

            try
            {
                FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                Clips.Remove(item);
            }
            catch (Exception ex)
            {
                Log($"Couldn't delete clip: {ex.Message}");
            }
        });

        LoadSettings();
        RefreshClips();
    }

    private void RefreshClips()
    {
        Clips.Clear();

        if (!Directory.Exists(LocalPaths.ClipsDirectory))
            return;

        var files = new DirectoryInfo(LocalPaths.ClipsDirectory)
            .GetFiles("clip_*.mp4")
            .OrderByDescending(f => f.CreationTimeUtc);

        foreach (var file in files)
        {
            Clips.Add(new ClipItem
            {
                FileName = file.Name,
                FullPath = file.FullName,
                SavedAtDisplay = file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                SizeDisplay = file.Length >= 1024 * 1024
                    ? $"{file.Length / (1024.0 * 1024.0):0.0} MB"
                    : $"{file.Length / 1024.0:0} KB",
            });
        }
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();
        DelaySeconds = settings.DelaySeconds;
        ClipSeconds = settings.ClipSeconds;
        ClipHotkeyModifiers = (ModifierKeys)settings.ClipHotkeyModifiers;
        ClipHotkeyKey = Enum.TryParse<Key>(settings.ClipHotkeyKey, out var hotkeyKey) ? hotkeyKey : Key.C;
        RecordSession = settings.RecordSession;

        foreach (var stored in settings.Destinations)
        {
            try
            {
                var item = DestinationEditItem.FromStored(stored, SettingsStore.ToRuntime(stored));
                WatchDestination(item);
                Destinations.Add(item);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                Log($"Could not decrypt saved destination '{stored.Name}' — skipping.");
            }
        }
    }

    /// <summary>
    /// Persists (DPAPI-encrypted) on every add/remove/edit, not just on Start —
    /// a stream key typed in and never touched again would otherwise never
    /// reach disk, defeating the point of encrypting it at rest.
    /// </summary>
    private void WatchDestination(DestinationEditItem item)
    {
        PropertyChangedEventHandler handler = (_, _) => SaveSettings();
        _destinationWatchers[item] = handler;
        item.PropertyChanged += handler;
    }

    private void UnwatchDestination(DestinationEditItem item)
    {
        if (_destinationWatchers.Remove(item, out var handler))
            item.PropertyChanged -= handler;
    }

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            DelaySeconds = DelaySeconds,
            ClipSeconds = ClipSeconds,
            ClipHotkeyModifiers = (int)ClipHotkeyModifiers,
            ClipHotkeyKey = ClipHotkeyKey.ToString(),
            RecordSession = RecordSession,
            Destinations = Destinations.Select(d => SettingsStore.ToStored(d.ToTarget(), d.Platform)).ToList(),
        };
        SettingsStore.Save(settings);
    }

    private void Start()
    {
        if (Destinations.Count == 0)
        {
            Log("Add at least one destination before starting.");
            return;
        }

        SaveSettings();

        var mediaMtxExe = VendoredTools.FindMediaMtxExe();
        var mediaMtxConfig = VendoredTools.FindMediaMtxConfig();
        _mediaMtx = new MediaMtxServer(mediaMtxExe, mediaMtxConfig, new Uri("http://127.0.0.1:9997"));

        var settings = new PipelineSettings
        {
            Delay = TimeSpan.FromSeconds(DelaySeconds),
            BufferDirectory = LocalPaths.BufferDirectory,
            ClipsDirectory = LocalPaths.ClipsDirectory,
            RecordSession = RecordSession,
            RecordingsDirectory = LocalPaths.RecordingsDirectory,
        };

        _sessionStartedAt = DateTimeOffset.UtcNow;
        _bitrateSamples.Clear();
        _lastDroppedFrames = 0;
        _clipsSavedThisSession = 0;

        _pipeline = new RestreamPipeline(_mediaMtx, "ffmpeg", settings);
        _pipeline.StateChanged += OnPipelineStateChanged;
        _pipeline.LogReceived += Log;
        _pipeline.StatsUpdated += OnStatsUpdated;
        _pipeline.SourceLost += () => _dispatcher.Invoke(() =>
            Notifications.Show("NekoStreamer", "OBS disconnected — waiting for it to reconnect...", System.Windows.Forms.ToolTipIcon.Warning));
        _pipeline.Faulted += ex =>
        {
            Log($"ERROR: {ex.Message}");
            _dispatcher.Invoke(() =>
                Notifications.Show("NekoStreamer", $"Pipeline error: {ex.Message}", System.Windows.Forms.ToolTipIcon.Error));
        };

        var targets = Destinations.Select(d => d.ToTarget()).ToList();
        _pipeline.Start(targets);

        IsRunning = true;
        OnPipelineStateChanged(_pipeline.State);
    }

    private async Task StopAsync()
    {
        if (_pipeline is not null)
        {
            await _pipeline.StopAsync();
            _pipeline = null;
        }

        LogSessionSummary();

        _mediaMtx = null;
        IsRunning = false;
        IsMuted = false;
        StatusText = "Stopped";
        HealthText = "—";
    }

    private void LogSessionSummary()
    {
        if (_sessionStartedAt == default)
            return;

        var duration = DateTimeOffset.UtcNow - _sessionStartedAt;
        var avgBitrate = _bitrateSamples.Count > 0 ? _bitrateSamples.Average() / 1000 : (double?)null;

        var parts = new List<string> { $"{duration:hh\\:mm\\:ss}" };
        if (avgBitrate is { } mbps)
            parts.Add($"avg {mbps:0.0} Mbps");
        if (_lastDroppedFrames > 0)
            parts.Add($"{_lastDroppedFrames} dropped frames");
        parts.Add($"{_clipsSavedThisSession} clip{(_clipsSavedThisSession == 1 ? "" : "s")} saved");

        Log($"Stream summary — {string.Join(" · ", parts)}");
        _sessionStartedAt = default;
    }

    private async Task SaveClipAsync()
    {
        if (_pipeline is null || _pipeline.State != PipelineState.Live)
        {
            Log("Can't save a clip — not currently live.");
            return;
        }

        try
        {
            var path = await _pipeline.SaveClipAsync(TimeSpan.FromSeconds(ClipSeconds));
            Log($"Clip saved: {path}");
            _clipsSavedThisSession++;
            RefreshClips();
        }
        catch (Exception ex)
        {
            Log($"Clip failed: {ex.Message}");
        }
    }

    private async Task TogglePanicMuteAsync()
    {
        if (_pipeline is null || _pipeline.State != PipelineState.Live)
        {
            Log("Can't toggle mute — not currently live.");
            return;
        }

        try
        {
            var newState = !IsMuted;
            await _pipeline.SetMutedAsync(newState);
            IsMuted = newState;
            Log(newState ? "PANIC MUTE engaged — audio cut to all destinations." : "Mute lifted — audio restored.");
        }
        catch (Exception ex)
        {
            Log($"Mute toggle failed: {ex.Message}");
        }
    }

    private void OnStatsUpdated(StreamStats stats)
    {
        _dispatcher.Invoke(() =>
        {
            if (stats.BitrateKbps is { } sampledKbps)
                _bitrateSamples.Add(sampledKbps);
            _lastDroppedFrames = stats.DroppedFrames;

            var bitrate = stats.BitrateKbps is { } kbps ? $"{kbps / 1000:0.0} Mbps" : "—";
            var speed = stats.Speed is { } sp ? $"{sp:0.0}x" : "—";
            var dropped = stats.DroppedFrames > 0 ? $" · {stats.DroppedFrames} dropped" : "";
            HealthText = $"{bitrate} · {stats.Elapsed:hh\\:mm\\:ss} · {speed}{dropped}";
        });
    }

    /// <summary>Called by the view once it has captured a new key combo while <see cref="IsCapturingHotkey"/> was true.</summary>
    public void ApplyCapturedHotkey(ModifierKeys modifiers, Key key)
    {
        ClipHotkeyModifiers = modifiers;
        ClipHotkeyKey = key;
        IsCapturingHotkey = false;
        SaveSettings();
        HotkeyChanged?.Invoke(modifiers, key);
    }

    private void OnPipelineStateChanged(PipelineState state)
    {
        _dispatcher.Invoke(() =>
        {
            StatusText = state switch
            {
                PipelineState.Stopped => "Stopped",
                PipelineState.WaitingForSource => "Waiting for OBS to connect...",
                PipelineState.Live => "Live",
                PipelineState.Faulted => "Error",
                _ => state.ToString(),
            };

            if (state == PipelineState.Faulted)
                IsRunning = false;
        });
    }

    internal void Log(string line)
    {
        _dispatcher.Invoke(() =>
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });
    }
}
