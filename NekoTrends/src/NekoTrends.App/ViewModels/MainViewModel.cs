using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using NekoTrends.Core;
using NekoTrends.Core.Alerts;
using NekoTrends.Core.Collection;
using NekoTrends.Core.Config;
using NekoTrends.Core.History;
using NekoTrends.Core.Kick;
using NekoTrends.Core.Models;
using NekoTrends.Core.Twitch;

namespace NekoTrends.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SnapshotStore _history;
    private readonly SnapshotCache _cache;
    private readonly AlertLog _alertLog;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _refreshCts;

    public ObservableCollection<TrendCardViewModel> Trending { get; } = [];
    public ObservableCollection<TrendCardViewModel> TopNow { get; } = [];
    public ObservableCollection<TrendCardViewModel> Watchlist { get; } = [];
    public ObservableCollection<KickCandidateViewModel> KickSuggestions { get; } = [];
    public ObservableCollection<TrackedChannel> TrackedChannels { get; } = [];
    public ObservableCollection<KeywordRule> KeywordRules { get; } = [];
    public ObservableCollection<AlertRule> AlertRules { get; } = [];
    public ObservableCollection<FiredAlert> Alerts { get; } = [];
    public ObservableCollection<string> PlatformErrors { get; } = [];

    /// <summary>Raised when alerts fire during a live refresh, so the window can show a popup.</summary>
    public event Action<IReadOnlyList<FiredAlert>>? AlertsFired;

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ConnectTwitchCommand { get; }
    public RelayCommand SaveYouTubeKeyCommand { get; }
    public RelayCommand ScanKickCommand { get; }
    public RelayCommand AddTrackedChannelCommand { get; }
    public RelayCommand<TrackedChannel> RemoveTrackedChannelCommand { get; }
    public RelayCommand AddKeywordRuleCommand { get; }
    public RelayCommand<KeywordRule> RemoveKeywordRuleCommand { get; }
    public RelayCommand AddAlertRuleCommand { get; }
    public RelayCommand<AlertRule> RemoveAlertRuleCommand { get; }
    public RelayCommand ClearAlertsCommand { get; }
    public RelayCommand ToggleBackgroundCollectionCommand { get; }
    public RelayCommand<TrendCardViewModel> OpenStreamCommand { get; }
    public RelayCommand<FiredAlert> OpenAlertCommand { get; }
    public RelayCommand<KickCandidateViewModel> AcceptCandidateCommand { get; }
    public RelayCommand<KickCandidateViewModel> DismissCandidateCommand { get; }

    public MainViewModel()
    {
        _settings = SettingsStore.Load();
        _history = new SnapshotStore(retention: TimeSpan.FromDays(_settings.HistoryRetentionDays));
        _history.Prune();
        _cache = new SnapshotCache();
        _alertLog = new AlertLog();

        foreach (var channel in _settings.TrackedChannels)
            TrackedChannels.Add(channel);
        foreach (var rule in _settings.KeywordRules)
            KeywordRules.Add(rule);
        foreach (var rule in _settings.AlertRules)
            AlertRules.Add(rule);
        foreach (var alert in _alertLog.Load())
            Alerts.Add(alert);

        _twitchClientId = _settings.Twitch?.ClientId ?? "";
        _minViewers = _settings.MinViewersForTrending;
        _refreshMinutes = _settings.RefreshIntervalMinutes;
        _isBackgroundCollectionOn = BackgroundCollector.IsRegistered();

        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsRefreshing);
        ConnectTwitchCommand = new RelayCommand(async () => await ConnectTwitchAsync(), () => !IsConnectingTwitch);
        SaveYouTubeKeyCommand = new RelayCommand(SaveYouTubeKey);
        ScanKickCommand = new RelayCommand(async () => await ScanKickAsync(), () => !IsScanningKick);
        AddTrackedChannelCommand = new RelayCommand(AddTrackedChannel);
        RemoveTrackedChannelCommand = new RelayCommand<TrackedChannel>(RemoveTrackedChannel);
        AddKeywordRuleCommand = new RelayCommand(AddKeywordRule);
        RemoveKeywordRuleCommand = new RelayCommand<KeywordRule>(RemoveKeywordRule);
        AddAlertRuleCommand = new RelayCommand(AddAlertRule);
        RemoveAlertRuleCommand = new RelayCommand<AlertRule>(RemoveAlertRule);
        ClearAlertsCommand = new RelayCommand(ClearAlerts);
        ToggleBackgroundCollectionCommand = new RelayCommand(ToggleBackgroundCollection);
        OpenStreamCommand = new RelayCommand<TrendCardViewModel>(card => OpenUrl(card?.Url));
        OpenAlertCommand = new RelayCommand<FiredAlert>(alert => OpenUrl(alert?.Url));
        AcceptCandidateCommand = new RelayCommand<KickCandidateViewModel>(AcceptCandidate);
        DismissCandidateCommand = new RelayCommand<KickCandidateViewModel>(DismissCandidate);

        _refreshTimer = new DispatcherTimer { Interval = TrendsService.ResolveInterval(_settings) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        HistorySize = _history.DescribeSize();
    }

    // ---- Feed state ------------------------------------------------------

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set { if (SetField(ref _isRefreshing, value)) RefreshCommand.RaiseCanExecuteChanged(); }
    }

    private string _status = "";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    private string _lastUpdated = "never";
    public string LastUpdated { get => _lastUpdated; private set => SetField(ref _lastUpdated, value); }

    private string _historySize = "";
    public string HistorySize { get => _historySize; private set => SetField(ref _historySize, value); }

    private StreamPlatform? _platformFilter;
    public StreamPlatform? PlatformFilter
    {
        get => _platformFilter;
        set { if (SetField(ref _platformFilter, value)) ApplyFilter(); }
    }

    /// <summary>Backs the filter ComboBox: 0 = all platforms, then one index per <see cref="StreamPlatform"/>.</summary>
    public int PlatformFilterIndex
    {
        get => _platformFilter is null ? 0 : (int)_platformFilter + 1;
        set => PlatformFilter = value <= 0 ? null : (StreamPlatform)(value - 1);
    }

    /// <summary>Name of the keyword rule to filter the feed by, or null for everything.</summary>
    private string? _ruleFilter;
    public string? RuleFilter
    {
        get => _ruleFilter;
        set { if (SetField(ref _ruleFilter, value)) ApplyFilter(); }
    }

    public ObservableCollection<string> RuleFilterOptions { get; } = ["All rules"];

    private int _ruleFilterIndex;
    public int RuleFilterIndex
    {
        get => _ruleFilterIndex;
        set
        {
            if (!SetField(ref _ruleFilterIndex, value))
                return;
            RuleFilter = value <= 0 || value >= RuleFilterOptions.Count ? null : RuleFilterOptions[value];
        }
    }

    private IReadOnlyList<TrendCardViewModel> _allTrending = [];
    private IReadOnlyList<TrendCardViewModel> _allTopNow = [];

    // ---- Setup state -----------------------------------------------------

    private string _twitchClientId = "";
    public string TwitchClientId { get => _twitchClientId; set => SetField(ref _twitchClientId, value); }

    private bool _isConnectingTwitch;
    public bool IsConnectingTwitch
    {
        get => _isConnectingTwitch;
        private set { if (SetField(ref _isConnectingTwitch, value)) ConnectTwitchCommand.RaiseCanExecuteChanged(); }
    }

    private string _twitchStatus = "Not connected";
    public string TwitchStatus { get => _twitchStatus; private set => SetField(ref _twitchStatus, value); }

    private string _youTubeApiKey = "";
    public string YouTubeApiKey { get => _youTubeApiKey; set => SetField(ref _youTubeApiKey, value); }

    private string _youTubeStatus = "Not configured — Twitch and Kick still work without this.";
    public string YouTubeStatus { get => _youTubeStatus; private set => SetField(ref _youTubeStatus, value); }

    private bool _isScanningKick;
    public bool IsScanningKick
    {
        get => _isScanningKick;
        private set { if (SetField(ref _isScanningKick, value)) ScanKickCommand.RaiseCanExecuteChanged(); }
    }

    private string _kickStatus = "";
    public string KickStatus { get => _kickStatus; private set => SetField(ref _kickStatus, value); }

    // ---- Tracker entry ---------------------------------------------------

    private int _newChannelPlatformIndex;
    public int NewChannelPlatformIndex { get => _newChannelPlatformIndex; set => SetField(ref _newChannelPlatformIndex, value); }

    private string _newChannelId = "";
    public string NewChannelId { get => _newChannelId; set => SetField(ref _newChannelId, value); }

    private string _trackerStatus = "";
    public string TrackerStatus { get => _trackerStatus; private set => SetField(ref _trackerStatus, value); }

    public string TrackerHint => NewChannelPlatformIndex switch
    {
        1 => "YouTube: paste the channel ID (starts with UC…) or a channel URL.",
        2 => "Kick: the channel slug, e.g. kick.com/<slug>.",
        _ => "Twitch: the login name, e.g. twitch.tv/<name>.",
    };

    // ---- Rule entry ------------------------------------------------------

    private string _newRuleName = "";
    public string NewRuleName { get => _newRuleName; set => SetField(ref _newRuleName, value); }

    private string _newRuleTerms = "";
    public string NewRuleTerms { get => _newRuleTerms; set => SetField(ref _newRuleTerms, value); }

    private string _newAlertName = "";
    public string NewAlertName { get => _newAlertName; set => SetField(ref _newAlertName, value); }

    private int _newAlertTriggerIndex;
    public int NewAlertTriggerIndex { get => _newAlertTriggerIndex; set => SetField(ref _newAlertTriggerIndex, value); }

    private string _newAlertThreshold = "2";
    public string NewAlertThreshold { get => _newAlertThreshold; set => SetField(ref _newAlertThreshold, value); }

    // ---- Settings --------------------------------------------------------

    private int _minViewers;
    public int MinViewers
    {
        get => _minViewers;
        set
        {
            if (!SetField(ref _minViewers, value))
                return;
            _settings.MinViewersForTrending = value;
            Save();
        }
    }

    private int _refreshMinutes;
    public int RefreshMinutes
    {
        get => _refreshMinutes;
        set
        {
            if (!SetField(ref _refreshMinutes, value))
                return;
            _settings.RefreshIntervalMinutes = value;
            Save();
            _refreshTimer.Interval = TrendsService.ResolveInterval(_settings);
            RaisePropertyChanged(nameof(EffectiveRefreshNote));
        }
    }

    public string EffectiveRefreshNote
    {
        get
        {
            var effective = TrendsService.ResolveInterval(_settings);
            return effective.TotalMinutes > _refreshMinutes
                ? $"Clamped to {effective.TotalMinutes:0} min to stay inside YouTube's daily quota."
                : $"Refreshing every {effective.TotalMinutes:0} min.";
        }
    }

    private bool _isBackgroundCollectionOn;
    public bool IsBackgroundCollectionOn
    {
        get => _isBackgroundCollectionOn;
        private set
        {
            if (SetField(ref _isBackgroundCollectionOn, value))
                RaisePropertyChanged(nameof(BackgroundCollectionLabel));
        }
    }

    public string BackgroundCollectionLabel =>
        IsBackgroundCollectionOn ? "Stop background collection" : "Collect in the background";

    private string _backgroundStatus = "";
    public string BackgroundStatus { get => _backgroundStatus; private set => SetField(ref _backgroundStatus, value); }

    public bool ShowAlertPopups
    {
        get => _settings.ShowAlertPopups;
        set
        {
            if (_settings.ShowAlertPopups == value)
                return;
            _settings.ShowAlertPopups = value;
            Save();
            RaisePropertyChanged();
        }
    }

    public bool HasAnySource => new RefreshPipeline(_settings, _history, _cache, _alertLog).HasAnySource;

    // ---- Lifecycle -------------------------------------------------------

    public async Task StartAsync()
    {
        LoadCachedSnapshot();

        if (!HasAnySource)
        {
            Status = "Add a channel on the Trackers tab, or connect Twitch on Sources, to get started.";
            return;
        }

        _refreshTimer.Start();
        await RefreshAsync();
    }

    /// <summary>
    /// Renders the last stored refresh immediately so the window is never blank at launch. If the
    /// background collector has been running, this is usually only minutes old.
    /// </summary>
    private void LoadCachedSnapshot()
    {
        var (streams, fetchedAt, _) = _cache.Load();
        if (streams.Count == 0)
            return;

        var history = _history.Load();
        var trending = TrendAnalyzer.Analyze(
            streams, history, new TrendOptions { MinViewers = _settings.MinViewersForTrending });

        _allTopNow = streams.Select(s => new TrendCardViewModel(s)).ToList();
        _allTrending = trending.Select(t => new TrendCardViewModel(t)).ToList();

        RebuildRuleFilterOptions();
        ApplyFilter();

        if (fetchedAt is { } stamp)
        {
            var age = DateTimeOffset.UtcNow - stamp;
            LastUpdated = age < TimeSpan.FromMinutes(2)
                ? stamp.ToLocalTime().ToString("t")
                : $"{stamp.ToLocalTime():t} ({Describe(age)} ago)";
        }
    }

    private static string Describe(TimeSpan age) => age.TotalHours >= 1
        ? $"{(int)age.TotalHours}h"
        : $"{Math.Max((int)age.TotalMinutes, 1)}m";

    public void Stop()
    {
        _refreshTimer.Stop();
        _refreshCts?.Cancel();
    }

    // ---- Refresh ---------------------------------------------------------

    private async Task RefreshAsync()
    {
        if (IsRefreshing)
            return;

        var pipeline = new RefreshPipeline(_settings, _history, _cache, _alertLog);
        if (!pipeline.HasAnySource)
        {
            Status = "No sources configured yet.";
            return;
        }

        IsRefreshing = true;
        Status = _allTopNow.Count == 0 ? "Loading…" : "Refreshing…";
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();

        try
        {
            var result = await pipeline.RunAsync(_refreshCts.Token);
            var snapshot = result.Snapshot;

            _allTrending = snapshot.Trending.Select(e => new TrendCardViewModel(e)).ToList();
            _allTopNow = snapshot.TopNow.Select(s => new TrendCardViewModel(s)).ToList();

            PlatformErrors.Clear();
            foreach (var error in snapshot.Errors.Values)
                PlatformErrors.Add(error);

            foreach (var alert in result.FiredAlerts)
                Alerts.Insert(0, alert);

            if (result.FiredAlerts.Count > 0 && ShowAlertPopups)
                AlertsFired?.Invoke(result.FiredAlerts);

            RebuildRuleFilterOptions();
            ApplyFilter();

            LastUpdated = snapshot.FetchedAtUtc.ToLocalTime().ToString("t");
            HistorySize = _history.DescribeSize();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer refresh or shutting down; the newer one owns the status text.
        }
        catch (Exception ex)
        {
            Status = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void UpdateStatus()
    {
        if (_allTopNow.Count == 0 && _allTrending.Count == 0)
        {
            Status = HasAnySource ? "No live VTubers found yet." : "No sources configured yet.";
            return;
        }

        var parts = new List<string> { $"{TopNow.Count} live", $"{Trending.Count} trending" };

        if (Watchlist.Count > 0)
            parts.Add($"{Watchlist.Count} tracked live");

        Status = string.Join(" · ", parts);
    }

    private void RebuildRuleFilterOptions()
    {
        var current = RuleFilterIndex > 0 && RuleFilterIndex < RuleFilterOptions.Count
            ? RuleFilterOptions[RuleFilterIndex]
            : null;

        RuleFilterOptions.Clear();
        RuleFilterOptions.Add("All rules");
        foreach (var rule in _settings.KeywordRules.Where(r => !string.IsNullOrWhiteSpace(r.Name)))
            RuleFilterOptions.Add(rule.Name);

        var restored = current is null ? 0 : RuleFilterOptions.IndexOf(current);
        _ruleFilterIndex = restored < 0 ? 0 : restored;
        _ruleFilter = _ruleFilterIndex <= 0 ? null : RuleFilterOptions[_ruleFilterIndex];
        RaisePropertyChanged(nameof(RuleFilterIndex));
    }

    private void ApplyFilter()
    {
        Trending.Clear();
        TopNow.Clear();
        Watchlist.Clear();

        foreach (var card in _allTrending.Where(Matches))
            Trending.Add(card);

        foreach (var card in _allTopNow.Where(Matches))
            TopNow.Add(card);

        foreach (var card in _allTopNow.Where(c => c.IsTracked).Where(Matches))
            Watchlist.Add(card);

        UpdateStatus();
    }

    private bool Matches(TrendCardViewModel card)
    {
        if (PlatformFilter is { } platform && card.Platform != platform)
            return false;

        if (RuleFilter is { Length: > 0 } rule
            && !card.MatchedRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void Save() => SettingsStore.Save(_settings);

    // ---- Twitch ----------------------------------------------------------

    private async Task ConnectTwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(TwitchClientId))
        {
            TwitchStatus = "Enter your Twitch Client ID first.";
            return;
        }

        IsConnectingTwitch = true;
        try
        {
            var auth = new TwitchDeviceAuthenticator(TwitchClientId.Trim());
            var deviceCode = await auth.RequestDeviceCodeAsync();

            TwitchStatus = $"Go to {deviceCode.VerificationUri} and enter code {deviceCode.UserCode}";
            OpenUrl(deviceCode.VerificationUri);

            var token = await auth.PollForTokenAsync(deviceCode);

            _settings.Twitch = new TwitchSettings
            {
                ClientId = TwitchClientId.Trim(),
                EncryptedAccessToken = SecretProtector.Protect(token.AccessToken),
                EncryptedRefreshToken = SecretProtector.Protect(token.RefreshToken),
                ExpiresInSeconds = token.ExpiresInSeconds,
                ObtainedAtUtc = token.ObtainedAtUtc,
            };
            Save();

            TwitchStatus = "Connected.";
            RaisePropertyChanged(nameof(HasAnySource));
            _refreshTimer.Start();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TwitchStatus = $"Connect failed: {ex.Message}";
        }
        finally
        {
            IsConnectingTwitch = false;
        }
    }

    // ---- YouTube ---------------------------------------------------------

    private void SaveYouTubeKey()
    {
        if (string.IsNullOrWhiteSpace(YouTubeApiKey))
        {
            _settings.YouTube = null;
            Save();
            YouTubeStatus = "Cleared — YouTube will be skipped.";
            return;
        }

        _settings.YouTube = SettingsStore.StoreYouTubeApiKey(YouTubeApiKey.Trim(), _settings.YouTube);
        Save();

        // Never keep the plaintext key in a bound property once it's encrypted at rest.
        YouTubeApiKey = "";
        YouTubeStatus = "Saved. YouTube will be included on the next refresh.";
        RaisePropertyChanged(nameof(HasAnySource));
    }

    // ---- Trackers --------------------------------------------------------

    private void AddTrackedChannel()
    {
        var platform = (StreamPlatform)Math.Clamp(NewChannelPlatformIndex, 0, 2);
        var id = NormalizeChannelId(NewChannelId, platform);

        if (id.Length == 0)
        {
            TrackerStatus = "Enter a channel name, slug or ID.";
            return;
        }

        if (_settings.TrackedChannels.Any(c => c.Platform == platform && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            TrackerStatus = $"{id} is already tracked.";
            return;
        }

        if (platform == StreamPlatform.YouTube && !id.StartsWith("UC", StringComparison.Ordinal))
        {
            TrackerStatus = "YouTube needs the channel ID (starts with UC…), not the @handle — it's in the channel's About → Share panel.";
            return;
        }

        var channel = new TrackedChannel { Platform = platform, Id = id };
        _settings.TrackedChannels.Add(channel);
        Save();

        TrackedChannels.Add(channel);
        NewChannelId = "";
        TrackerStatus = $"Tracking {platform} · {id}.";
        RaisePropertyChanged(nameof(HasAnySource));
    }

    /// <summary>Accepts a pasted URL as readily as a bare id, since that's what people copy.</summary>
    internal static string NormalizeChannelId(string raw, StreamPlatform platform)
    {
        var value = raw.Trim().Trim('/');
        if (value.Length == 0)
            return "";

        if (value.Contains("://", StringComparison.Ordinal) || value.Contains(".com/", StringComparison.OrdinalIgnoreCase))
        {
            var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // youtube.com/channel/UCxxxx — the id is the segment after "channel".
            var channelIndex = Array.FindIndex(segments, s => s.Equals("channel", StringComparison.OrdinalIgnoreCase));
            value = channelIndex >= 0 && channelIndex + 1 < segments.Length
                ? segments[channelIndex + 1]
                : segments[^1];
        }

        // Strip query strings and Twitch/Kick decorations.
        var queryIndex = value.IndexOf('?');
        if (queryIndex >= 0)
            value = value[..queryIndex];

        return platform == StreamPlatform.YouTube ? value : value.ToLowerInvariant();
    }

    private void RemoveTrackedChannel(TrackedChannel? channel)
    {
        if (channel is null)
            return;

        _settings.TrackedChannels.RemoveAll(c => c.Platform == channel.Platform
            && string.Equals(c.Id, channel.Id, StringComparison.OrdinalIgnoreCase));
        Save();
        TrackedChannels.Remove(channel);
    }

    // ---- Keyword rules ---------------------------------------------------

    private void AddKeywordRule()
    {
        var name = NewRuleName.Trim();
        var terms = NewRuleTerms
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (name.Length == 0 || terms.Count == 0)
        {
            TrackerStatus = "A rule needs a name and at least one term.";
            return;
        }

        var rule = new KeywordRule { Name = name, Terms = terms };
        _settings.KeywordRules.Add(rule);
        Save();

        KeywordRules.Add(rule);
        NewRuleName = "";
        NewRuleTerms = "";
        RebuildRuleFilterOptions();
        ApplyFilter();
    }

    private void RemoveKeywordRule(KeywordRule? rule)
    {
        if (rule is null)
            return;

        _settings.KeywordRules.Remove(rule);
        Save();
        KeywordRules.Remove(rule);
        RebuildRuleFilterOptions();
        ApplyFilter();
    }

    // ---- Alert rules -----------------------------------------------------

    private void AddAlertRule()
    {
        var name = NewAlertName.Trim();
        if (name.Length == 0)
        {
            TrackerStatus = "An alert needs a name.";
            return;
        }

        if (!double.TryParse(NewAlertThreshold, out var threshold))
            threshold = 2;

        var rule = new AlertRule
        {
            Name = name,
            Trigger = (AlertTrigger)Math.Clamp(NewAlertTriggerIndex, 0, 2),
            Threshold = threshold,
        };

        _settings.AlertRules.Add(rule);
        Save();

        AlertRules.Add(rule);
        NewAlertName = "";
    }

    private void RemoveAlertRule(AlertRule? rule)
    {
        if (rule is null)
            return;

        _settings.AlertRules.Remove(rule);
        Save();
        AlertRules.Remove(rule);
    }

    private void ClearAlerts()
    {
        _alertLog.Clear();
        Alerts.Clear();
    }

    // ---- Background collection -------------------------------------------

    private void ToggleBackgroundCollection()
    {
        if (IsBackgroundCollectionOn)
        {
            var (ok, message) = BackgroundCollector.Unregister();
            BackgroundStatus = message;
            if (ok)
                IsBackgroundCollectionOn = false;
            return;
        }

        var interval = (int)TrendsService.ResolveInterval(_settings).TotalMinutes;
        var (registered, result) = BackgroundCollector.Register(interval);
        BackgroundStatus = result;
        if (registered)
            IsBackgroundCollectionOn = true;
    }

    // ---- Kick suggestions ------------------------------------------------

    private async Task ScanKickAsync()
    {
        IsScanningKick = true;
        KickStatus = "Searching Kick…";
        try
        {
            var suggester = new KickRosterSuggester();
            var candidates = await suggester.SuggestAsync(
                _settings.TrackedIdsFor(StreamPlatform.Kick), _settings.Kick.DismissedSlugs);

            KickSuggestions.Clear();
            foreach (var candidate in candidates)
                KickSuggestions.Add(new KickCandidateViewModel(candidate));

            KickStatus = candidates.Count == 0
                ? "No new candidates found. You can still add channels by slug."
                : $"{candidates.Count} candidate(s) — add the ones that are actually VTubers.";
        }
        catch (Exception ex)
        {
            KickStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanningKick = false;
        }
    }

    private void AcceptCandidate(KickCandidateViewModel? candidate)
    {
        if (candidate is null)
            return;

        var channel = new TrackedChannel { Platform = StreamPlatform.Kick, Id = candidate.Slug };
        _settings.TrackedChannels.Add(channel);
        _settings.Kick.DismissedSlugs.RemoveAll(s => string.Equals(s, candidate.Slug, StringComparison.OrdinalIgnoreCase));
        Save();

        TrackedChannels.Add(channel);
        KickSuggestions.Remove(candidate);
        RaisePropertyChanged(nameof(HasAnySource));
    }

    private void DismissCandidate(KickCandidateViewModel? candidate)
    {
        if (candidate is null)
            return;

        if (!_settings.Kick.DismissedSlugs.Contains(candidate.Slug, StringComparer.OrdinalIgnoreCase))
            _settings.Kick.DismissedSlugs.Add(candidate.Slug);

        Save();
        KickSuggestions.Remove(candidate);
    }

    // ---- Shared ----------------------------------------------------------

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser registered — nothing useful to do, and crashing over it is worse.
        }
    }
}

/// <summary>One card in the feed. Wraps either a raw stream (Top Now) or an analysed trend (Trending).</summary>
public sealed class TrendCardViewModel
{
    private readonly LiveStream _stream;

    public TrendCardViewModel(LiveStream stream) => _stream = stream;

    public TrendCardViewModel(TrendEntry entry)
    {
        _stream = entry.Stream;
        Headline = entry.Headline;
        IsNewcomer = entry.IsNewcomer;
        BaselineViewers = entry.BaselineViewers;
        HasMomentum = true;
    }

    public StreamPlatform Platform => _stream.Platform;
    public string DisplayName => _stream.DisplayName;
    public string? Title => _stream.Title;
    public string? Category => _stream.Category;
    public string? Language => _stream.Language;
    public int ViewerCount => _stream.ViewerCount;
    public string? ThumbnailUrl => _stream.ThumbnailUrl;
    public TimeSpan? Uptime => _stream.Uptime;
    public string Url => _stream.Url;
    public bool IsTracked => _stream.IsTracked;
    public IReadOnlyList<string> MatchedRules => _stream.MatchedRules;

    public string RuleLabel => _stream.MatchedRules.Count > 0 ? string.Join(" · ", _stream.MatchedRules) : "";

    public string Headline { get; } = "";
    public bool IsNewcomer { get; }
    public bool HasMomentum { get; }
    public int? BaselineViewers { get; }

    public string BaselineNote => BaselineViewers is { } baseline ? $"usually ~{baseline:N0}" : "";
}

public sealed class KickCandidateViewModel
{
    private readonly KickCandidate _candidate;

    public KickCandidateViewModel(KickCandidate candidate) => _candidate = candidate;

    public string Slug => _candidate.Slug;
    public string DisplayName => _candidate.DisplayName;
    public int Followers => _candidate.Followers;
    public bool IsLive => _candidate.IsLive;
    public string Url => _candidate.Url;
    public string FollowerNote => $"{_candidate.Followers:N0} followers";
}
