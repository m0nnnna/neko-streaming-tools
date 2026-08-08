using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
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
    private MediaMtxServer? _mediaMtx;
    private RestreamPipeline? _pipeline;

    public ObservableCollection<DestinationEditItem> Destinations { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public static IReadOnlyList<StreamingPlatform> AvailablePlatforms { get; } =
        Enum.GetValues<StreamingPlatform>();

    public string IngestServerUrl => "rtmp://localhost:1935";
    public string IngestStreamKey => "live";

    private int _delaySeconds = 30;
    public int DelaySeconds
    {
        get => _delaySeconds;
        set => SetField(ref _delaySeconds, value);
    }

    private string _statusText = "Stopped";
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetField(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand AddDestinationCommand { get; }
    public RelayCommand<DestinationEditItem> RemoveDestinationCommand { get; }

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

        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();
        DelaySeconds = settings.DelaySeconds;

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
        };

        _pipeline = new RestreamPipeline(_mediaMtx, "ffmpeg", settings);
        _pipeline.StateChanged += OnPipelineStateChanged;
        _pipeline.LogReceived += Log;
        _pipeline.Faulted += ex => Log($"ERROR: {ex.Message}");

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

        _mediaMtx = null;
        IsRunning = false;
        StatusText = "Stopped";
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

    private void Log(string line)
    {
        _dispatcher.Invoke(() =>
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });
    }
}
