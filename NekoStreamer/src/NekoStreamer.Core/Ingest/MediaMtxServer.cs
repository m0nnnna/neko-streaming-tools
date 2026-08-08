using System.Text.Json;
using NekoStreamer.Core.Binaries;
using NekoStreamer.Core.Processes;

namespace NekoStreamer.Core.Ingest;

/// <summary>
/// Manages the MediaMTX RTMP ingest process. MediaMTX itself never touches ffmpeg or
/// the delay buffer — its only job is accepting OBS's RTMP push and exposing an API
/// we can poll to know when a source is actively publishing.
/// </summary>
public sealed class MediaMtxServer : IAsyncDisposable
{
    private readonly string _vendoredExePath;
    private readonly string _configPath;
    private readonly Uri _apiBaseUrl;
    private readonly HttpClient _httpClient = new();
    private ManagedProcess? _process;

    public MediaMtxServer(string vendoredExePath, string configPath, Uri apiBaseUrl)
    {
        _vendoredExePath = vendoredExePath;
        _configPath = configPath;
        _apiBaseUrl = apiBaseUrl;
    }

    public bool IsRunning => _process?.IsRunning ?? false;

    public event Action<string>? OutputLogReceived;

    public void Start()
    {
        if (IsRunning)
            return;

        var localExePath = LocalBinaryStager.Stage(_vendoredExePath);
        _process = ManagedProcess.Start(localExePath, [_configPath]);
        _process.OutputReceived += line => OutputLogReceived?.Invoke(line);
    }

    /// <summary>
    /// Kills MediaMTX immediately — it's a stateless relay with nothing to flush,
    /// so waiting for a graceful exit only stalls Stop for no benefit.
    /// </summary>
    public Task StopAsync()
    {
        if (_process is not null)
        {
            _process.Kill();
            _process.Dispose();
            _process = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// True once OBS (or any RTMP client) is actively publishing to the given path.
    /// </summary>
    public async Task<bool> IsPathReadyAsync(string pathName, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(new Uri(_apiBaseUrl, $"/v3/paths/get/{pathName}"), ct);
            if (!response.IsSuccessStatusCode)
                return false;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("ready", out var ready) && ready.GetBoolean();
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
    }
}
