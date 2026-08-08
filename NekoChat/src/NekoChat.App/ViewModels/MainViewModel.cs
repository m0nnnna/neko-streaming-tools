using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Google.Apis.Auth.OAuth2;
using Microsoft.Win32;
using NekoChat.Core.Config;
using NekoChat.Core.Donations;
using NekoChat.Core.Kick;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;
using NekoChat.Core.Twitch;
using NekoChat.Core.Viewers;
using NekoChat.Core.YouTube;

namespace NekoChat.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxMessages = 500;
    private static readonly TimeSpan ViewerCountPollInterval = TimeSpan.FromSeconds(30);
    private static readonly string[] TwitchScopes =
        ["user:read:chat", "moderator:read:followers", "channel:read:subscriptions"];

    private readonly Dispatcher _dispatcher;
    private readonly EncryptedDataStore _youTubeDataStore = new();
    private ChatAggregator _aggregator = new();
    private ViewerCountAggregator _viewerCountAggregator = new();
    private CancellationTokenSource? _twitchAuthCts;
    private TwitchToken? _twitchToken;
    private UserCredential? _youTubeCredential;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    private int _totalViewerCount;
    public int TotalViewerCount
    {
        get => _totalViewerCount;
        set => SetField(ref _totalViewerCount, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetField(ref _isConnected, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }

    // --- Twitch account connection ---

    private string _twitchClientId = "";
    public string TwitchClientId
    {
        get => _twitchClientId;
        set => SetField(ref _twitchClientId, value);
    }

    private string _twitchStatusText = "Not connected";
    public string TwitchStatusText
    {
        get => _twitchStatusText;
        set => SetField(ref _twitchStatusText, value);
    }

    private string? _twitchUserCode;
    public string? TwitchUserCode
    {
        get => _twitchUserCode;
        set => SetField(ref _twitchUserCode, value);
    }

    private string? _twitchVerificationUri;
    public string? TwitchVerificationUri
    {
        get => _twitchVerificationUri;
        set => SetField(ref _twitchVerificationUri, value);
    }

    private bool _twitchIsAuthenticating;
    public bool TwitchIsAuthenticating
    {
        get => _twitchIsAuthenticating;
        set
        {
            if (SetField(ref _twitchIsAuthenticating, value))
            {
                ConnectTwitchCommand.RaiseCanExecuteChanged();
                CancelTwitchAuthCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _twitchIsConnected;
    public bool TwitchIsConnected
    {
        get => _twitchIsConnected;
        set
        {
            if (SetField(ref _twitchIsConnected, value))
            {
                ConnectTwitchCommand.RaiseCanExecuteChanged();
                DisconnectTwitchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand ConnectTwitchCommand { get; }
    public RelayCommand CancelTwitchAuthCommand { get; }
    public RelayCommand DisconnectTwitchCommand { get; }
    public RelayCommand OpenTwitchVerificationUrlCommand { get; }

    // --- Kick channel ---

    private string _kickChannelSlug = "";
    public string KickChannelSlug
    {
        get => _kickChannelSlug;
        set
        {
            if (SetField(ref _kickChannelSlug, value))
                MutateSettings(s => s.KickChannelSlug = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    // --- YouTube account connection ---

    private string _youTubeClientId = "";
    public string YouTubeClientId
    {
        get => _youTubeClientId;
        set => SetField(ref _youTubeClientId, value);
    }

    private string _youTubeClientSecret = "";
    public string YouTubeClientSecret
    {
        get => _youTubeClientSecret;
        set => SetField(ref _youTubeClientSecret, value);
    }

    private string _youTubeStatusText = "Not connected";
    public string YouTubeStatusText
    {
        get => _youTubeStatusText;
        set => SetField(ref _youTubeStatusText, value);
    }

    private bool _youTubeIsAuthenticating;
    public bool YouTubeIsAuthenticating
    {
        get => _youTubeIsAuthenticating;
        set
        {
            if (SetField(ref _youTubeIsAuthenticating, value))
                ConnectYouTubeCommand.RaiseCanExecuteChanged();
        }
    }

    private bool _youTubeIsConnected;
    public bool YouTubeIsConnected
    {
        get => _youTubeIsConnected;
        set
        {
            if (SetField(ref _youTubeIsConnected, value))
            {
                ConnectYouTubeCommand.RaiseCanExecuteChanged();
                DisconnectYouTubeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand ConnectYouTubeCommand { get; }
    public RelayCommand DisconnectYouTubeCommand { get; }

    // --- Donation sources (all optional — just paste and go, no OAuth) ---

    private string _streamElementsToken = "";
    public string StreamElementsToken
    {
        get => _streamElementsToken;
        set
        {
            if (SetField(ref _streamElementsToken, value))
            {
                MutateDonationSettings(d => d.StreamElementsEncryptedJwtToken =
                    string.IsNullOrWhiteSpace(value) ? null : SettingsStore.ToStoredStreamElementsToken(value));
            }
        }
    }

    private string _liberapayUsername = "";
    public string LiberapayUsername
    {
        get => _liberapayUsername;
        set
        {
            if (SetField(ref _liberapayUsername, value))
                MutateDonationSettings(d => d.LiberapayUsername = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    private string _bitcoinAddress = "";
    public string BitcoinAddress
    {
        get => _bitcoinAddress;
        set
        {
            if (SetField(ref _bitcoinAddress, value))
                MutateDonationSettings(d => d.BitcoinAddress = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    private string _ethereumAddress = "";
    public string EthereumAddress
    {
        get => _ethereumAddress;
        set
        {
            if (SetField(ref _ethereumAddress, value))
                MutateDonationSettings(d => d.EthereumAddress = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    private string _etherscanApiKey = "";
    public string EtherscanApiKey
    {
        get => _etherscanApiKey;
        set
        {
            if (SetField(ref _etherscanApiKey, value))
                MutateDonationSettings(d => d.EtherscanApiKey = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    // --- Alert Box (one shared default + optional per-kind overrides) ---

    private static readonly ChatMessageKind[] AlertKinds = Enum.GetValues<ChatMessageKind>()
        .Where(k => k != ChatMessageKind.Message)
        .ToArray();

    private string _defaultAlertImagePath = "";
    public string DefaultAlertImagePath
    {
        get => _defaultAlertImagePath;
        set
        {
            if (SetField(ref _defaultAlertImagePath, value))
                MutateAlertBoxSettings(a => a.Default.ImagePath = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    private string _defaultAlertSoundPath = "";
    public string DefaultAlertSoundPath
    {
        get => _defaultAlertSoundPath;
        set
        {
            if (SetField(ref _defaultAlertSoundPath, value))
                MutateAlertBoxSettings(a => a.Default.SoundPath = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    private int _defaultAlertDisplaySeconds = 6;
    public int DefaultAlertDisplaySeconds
    {
        get => _defaultAlertDisplaySeconds;
        set
        {
            if (SetField(ref _defaultAlertDisplaySeconds, value))
                MutateAlertBoxSettings(a => a.Default.DisplaySeconds = value);
        }
    }

    public ObservableCollection<AlertOverrideEditItem> AlertOverrides { get; } = new();

    public RelayCommand BrowseDefaultAlertImageCommand { get; }
    public RelayCommand BrowseDefaultAlertSoundCommand { get; }
    public RelayCommand<AlertOverrideEditItem> BrowseOverrideImageCommand { get; }
    public RelayCommand<AlertOverrideEditItem> BrowseOverrideSoundCommand { get; }
    public RelayCommand TestAlertCommand { get; }

    public event Action<ChatMessage>? TestAlertRequested;

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);

        ConnectTwitchCommand = new RelayCommand(async () => await ConnectTwitchAsync(), () => !TwitchIsAuthenticating && !TwitchIsConnected);
        CancelTwitchAuthCommand = new RelayCommand(() => _twitchAuthCts?.Cancel(), () => TwitchIsAuthenticating);
        DisconnectTwitchCommand = new RelayCommand(DisconnectTwitch, () => TwitchIsConnected);
        OpenTwitchVerificationUrlCommand = new RelayCommand(() =>
        {
            if (TwitchVerificationUri is not null)
                Process.Start(new ProcessStartInfo(TwitchVerificationUri) { UseShellExecute = true });
        });

        ConnectYouTubeCommand = new RelayCommand(async () => await ConnectYouTubeAsync(), () => !YouTubeIsAuthenticating && !YouTubeIsConnected);
        DisconnectYouTubeCommand = new RelayCommand(async () => await DisconnectYouTubeAsync(), () => YouTubeIsConnected);

        BrowseDefaultAlertImageCommand = new RelayCommand(() => BrowseImage(p => DefaultAlertImagePath = p));
        BrowseDefaultAlertSoundCommand = new RelayCommand(() => BrowseSound(p => DefaultAlertSoundPath = p));
        BrowseOverrideImageCommand = new RelayCommand<AlertOverrideEditItem>(item =>
        {
            if (item is not null)
                BrowseImage(p => item.ImagePath = p);
        });
        BrowseOverrideSoundCommand = new RelayCommand<AlertOverrideEditItem>(item =>
        {
            if (item is not null)
                BrowseSound(p => item.SoundPath = p);
        });
        TestAlertCommand = new RelayCommand(() => TestAlertRequested?.Invoke(
            new ChatMessage(ChatPlatform.Twitch, "TestUser", "sent a test alert", DateTimeOffset.Now, Kind: ChatMessageKind.Donation)));

        foreach (var kind in AlertKinds)
        {
            var item = new AlertOverrideEditItem { Kind = kind };
            item.PropertyChanged += (_, _) => SaveAlertOverride(item);
            AlertOverrides.Add(item);
        }

        LoadSettings();
    }

    private static void BrowseImage(Action<string> onSelected)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an alert image or GIF",
            Filter = "Images (*.gif;*.png;*.jpg;*.jpeg;*.bmp)|*.gif;*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
            onSelected(dialog.FileName);
    }

    private static void BrowseSound(Action<string> onSelected)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an alert sound",
            Filter = "Audio (*.mp3;*.wav)|*.mp3;*.wav|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
            onSelected(dialog.FileName);
    }

    private void SaveAlertOverride(AlertOverrideEditItem item)
    {
        MutateAlertBoxSettings(a =>
        {
            var hasImage = !string.IsNullOrWhiteSpace(item.ImagePath);
            var hasSound = !string.IsNullOrWhiteSpace(item.SoundPath);
            if (!hasImage && !hasSound)
            {
                a.Overrides.Remove(item.Kind.ToString());
                return;
            }

            a.Overrides[item.Kind.ToString()] = new AlertMediaConfig
            {
                ImagePath = hasImage ? item.ImagePath : null,
                SoundPath = hasSound ? item.SoundPath : null,
            };
        });
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();

        _kickChannelSlug = settings.KickChannelSlug ?? "";
        RaisePropertyChanged(nameof(KickChannelSlug));

        if (settings.YouTube is not null)
        {
            var (clientId, clientSecret) = SettingsStore.ToRuntime(settings.YouTube);
            _youTubeClientId = clientId;
            _youTubeClientSecret = clientSecret;
            RaisePropertyChanged(nameof(YouTubeClientId));
            RaisePropertyChanged(nameof(YouTubeClientSecret));
        }

        if (settings.AlertBox is not null)
        {
            _defaultAlertImagePath = settings.AlertBox.Default.ImagePath ?? "";
            _defaultAlertSoundPath = settings.AlertBox.Default.SoundPath ?? "";
            _defaultAlertDisplaySeconds = settings.AlertBox.Default.DisplaySeconds ?? 6;
            RaisePropertyChanged(nameof(DefaultAlertImagePath));
            RaisePropertyChanged(nameof(DefaultAlertSoundPath));
            RaisePropertyChanged(nameof(DefaultAlertDisplaySeconds));

            foreach (var item in AlertOverrides)
            {
                if (settings.AlertBox.Overrides.TryGetValue(item.Kind.ToString(), out var over))
                {
                    item.ImagePath = over.ImagePath ?? "";
                    item.SoundPath = over.SoundPath ?? "";
                }
            }
        }

        if (settings.Donations is not null)
        {
            _liberapayUsername = settings.Donations.LiberapayUsername ?? "";
            _bitcoinAddress = settings.Donations.BitcoinAddress ?? "";
            _ethereumAddress = settings.Donations.EthereumAddress ?? "";
            _etherscanApiKey = settings.Donations.EtherscanApiKey ?? "";
            RaisePropertyChanged(nameof(LiberapayUsername));
            RaisePropertyChanged(nameof(BitcoinAddress));
            RaisePropertyChanged(nameof(EthereumAddress));
            RaisePropertyChanged(nameof(EtherscanApiKey));

            try
            {
                _streamElementsToken = SettingsStore.ToRuntimeStreamElementsToken(settings.Donations) ?? "";
                RaisePropertyChanged(nameof(StreamElementsToken));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                Log(ChatPlatform.StreamElements, "Could not decrypt saved StreamElements token — please re-enter it.");
            }
        }

        if (settings.Twitch is null)
            return;

        TwitchClientId = settings.Twitch.ClientId;
        try
        {
            _twitchToken = SettingsStore.ToRuntime(settings.Twitch);
            TwitchIsConnected = true;
            TwitchStatusText = "Connected (saved login)";
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Log(ChatPlatform.Twitch, "Could not decrypt saved Twitch login — please reconnect.");
        }
    }

    /// <summary>Load-modify-save so setting one platform's config never wipes another's.</summary>
    private static void MutateSettings(Action<AppSettings> mutate)
    {
        var settings = SettingsStore.Load();
        mutate(settings);
        SettingsStore.Save(settings);
    }

    private static void MutateDonationSettings(Action<DonationSettings> mutate) =>
        MutateSettings(s =>
        {
            s.Donations ??= new DonationSettings();
            mutate(s.Donations);
        });

    private static void MutateAlertBoxSettings(Action<AlertBoxSettings> mutate) =>
        MutateSettings(s =>
        {
            s.AlertBox ??= new AlertBoxSettings();
            mutate(s.AlertBox);
        });

    private async Task ConnectTwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(TwitchClientId))
        {
            TwitchStatusText = "Enter a Twitch Client ID first.";
            return;
        }

        TwitchIsAuthenticating = true;
        TwitchStatusText = "Requesting device code...";
        _twitchAuthCts = new CancellationTokenSource();

        try
        {
            var authenticator = new TwitchDeviceAuthenticator(TwitchClientId);
            var deviceCode = await authenticator.RequestDeviceCodeAsync(TwitchScopes, _twitchAuthCts.Token);

            TwitchUserCode = deviceCode.UserCode;
            TwitchVerificationUri = deviceCode.VerificationUri;
            TwitchStatusText = $"Enter code {deviceCode.UserCode} at {deviceCode.VerificationUri}";

            var token = await authenticator.PollForTokenAsync(deviceCode, TwitchScopes, _twitchAuthCts.Token);

            _twitchToken = token;
            MutateSettings(s => s.Twitch = SettingsStore.ToStored(TwitchClientId, token));

            TwitchIsConnected = true;
            TwitchStatusText = "Connected";
        }
        catch (OperationCanceledException)
        {
            TwitchStatusText = "Not connected";
        }
        catch (Exception ex)
        {
            TwitchStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            TwitchUserCode = null;
            TwitchVerificationUri = null;
            TwitchIsAuthenticating = false;
            _twitchAuthCts?.Dispose();
            _twitchAuthCts = null;
        }
    }

    private void DisconnectTwitch()
    {
        _twitchToken = null;
        TwitchIsConnected = false;
        TwitchStatusText = "Not connected";
        MutateSettings(s => s.Twitch = null);
    }

    private async Task ConnectYouTubeAsync()
    {
        if (string.IsNullOrWhiteSpace(YouTubeClientId) || string.IsNullOrWhiteSpace(YouTubeClientSecret))
        {
            YouTubeStatusText = "Enter a Client ID and Client Secret first.";
            return;
        }

        YouTubeIsAuthenticating = true;
        YouTubeStatusText = "Opening browser for Google sign-in...";

        try
        {
            // Opens the user's browser automatically; if a valid token is already
            // stored, returns almost instantly with no browser popup at all.
            _youTubeCredential = await YouTubeAuthenticator.AuthorizeAsync(YouTubeClientId, YouTubeClientSecret, _youTubeDataStore);

            MutateSettings(s => s.YouTube = SettingsStore.ToStored(YouTubeClientId, YouTubeClientSecret));

            YouTubeIsConnected = true;
            YouTubeStatusText = "Connected";
        }
        catch (Exception ex)
        {
            YouTubeStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            YouTubeIsAuthenticating = false;
        }
    }

    private async Task DisconnectYouTubeAsync()
    {
        _youTubeCredential = null;
        YouTubeIsConnected = false;
        YouTubeStatusText = "Not connected";
        await _youTubeDataStore.ClearAsync();
        MutateSettings(s => s.YouTube = null);
    }

    // --- Unified feed connect/disconnect ---

    private async Task ConnectAsync()
    {
        _aggregator = new ChatAggregator();
        _aggregator.MessageReceived += OnMessageReceived;
        _aggregator.SourceFaulted += (platform, ex) => Log(platform, $"chat error: {ex.Message}");

        _viewerCountAggregator = new ViewerCountAggregator();
        _viewerCountAggregator.TotalUpdated += OnTotalViewersUpdated;
        _viewerCountAggregator.SourceFaulted += (platform, ex) => Log(platform, $"viewer count error: {ex.Message}");

        if (TwitchIsConnected && _twitchToken is not null)
        {
            _aggregator.AddSource(new TwitchChatSource(TwitchClientId, _twitchToken.AccessToken));
            _viewerCountAggregator.AddSource(new TwitchViewerCountSource(TwitchClientId, _twitchToken.AccessToken));
        }
        else
        {
            _aggregator.AddSource(new DemoChatSource(ChatPlatform.Twitch));
        }

        if (!string.IsNullOrWhiteSpace(KickChannelSlug))
        {
            _aggregator.AddSource(new KickChatSource(KickChannelSlug));
            _viewerCountAggregator.AddSource(new KickViewerCountSource(KickChannelSlug));
        }
        else
        {
            _aggregator.AddSource(new DemoChatSource(ChatPlatform.Kick));
        }

        if (YouTubeIsConnected && _youTubeCredential is not null)
        {
            _aggregator.AddSource(new YouTubeChatSource(_youTubeCredential));
            _viewerCountAggregator.AddSource(new YouTubeViewerCountSource(_youTubeCredential));
        }
        else
        {
            _aggregator.AddSource(new DemoChatSource(ChatPlatform.YouTube));
        }

        // Donation sources are all optional extras, not core chat — no demo
        // fallback, just skip whichever aren't configured.
        if (!string.IsNullOrWhiteSpace(StreamElementsToken))
            _aggregator.AddSource(new StreamElementsDonationSource(StreamElementsToken));

        if (!string.IsNullOrWhiteSpace(LiberapayUsername))
            _aggregator.AddSource(new LiberapayDonationSource(LiberapayUsername));

        if (!string.IsNullOrWhiteSpace(BitcoinAddress))
            _aggregator.AddSource(new BitcoinDonationSource(BitcoinAddress));

        if (!string.IsNullOrWhiteSpace(EthereumAddress) && !string.IsNullOrWhiteSpace(EtherscanApiKey))
            _aggregator.AddSource(new EthereumDonationSource(EthereumAddress, EtherscanApiKey));

        await _aggregator.ConnectAllAsync();
        _viewerCountAggregator.Start(ViewerCountPollInterval);
        IsConnected = true;
    }

    private async Task DisconnectAsync()
    {
        _viewerCountAggregator.Stop();
        TotalViewerCount = 0;
        await _aggregator.DisconnectAllAsync();
        IsConnected = false;
    }

    private void OnTotalViewersUpdated(int total) =>
        _dispatcher.Invoke(() => TotalViewerCount = total);

    private void OnMessageReceived(ChatMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            Messages.Add(message);
            while (Messages.Count > MaxMessages)
                Messages.RemoveAt(0);
        });
    }

    private void Log(ChatPlatform platform, string text) =>
        OnMessageReceived(new ChatMessage(platform, "system", text, DateTimeOffset.Now));
}
