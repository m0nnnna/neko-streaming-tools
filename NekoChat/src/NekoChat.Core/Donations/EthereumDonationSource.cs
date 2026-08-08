using System.Text.Json;
using System.Text.Json.Serialization;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;

namespace NekoChat.Core.Donations;

/// <summary>
/// Polls Etherscan's v2 API for a wallet address's recent transactions, alerting
/// on new incoming ones. Unlike Kick/Liberapay this endpoint is not behind
/// aggressive bot protection — plain HttpClient works fine (confirmed empirically).
/// Etherscan requires a free API key (V1's keyless access was deprecated) — this
/// is real, if small, extra setup versus Bitcoin's zero-config address watching.
/// A blockchain transaction has no text field, so alerts show amount only, never
/// a donor name or message.
/// </summary>
public sealed class EthereumDonationSource : IChatSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly string _address;
    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly HashSet<string> _seenHashes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public ChatPlatform Platform => ChatPlatform.Ethereum;

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public EthereumDonationSource(string address, string apiKey, HttpClient? httpClient = null)
    {
        _address = address;
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient();
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Baseline off the current recent-tx hashes so connecting doesn't replay
        // history as fresh alerts.
        var txs = await FetchRecentTransactionsAsync(ct);
        foreach (var tx in txs)
            _seenHashes.Add(tx.Hash);

        IsConnected = true;
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token), CancellationToken.None);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);

                var txs = await FetchRecentTransactionsAsync(ct);
                foreach (var tx in txs)
                {
                    if (!_seenHashes.Add(tx.Hash))
                        continue;

                    if (!string.Equals(tx.To, _address, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (tx.IsError == "1")
                        continue;
                    if (!decimal.TryParse(tx.Value, out var wei) || wei <= 0)
                        continue;

                    var eth = wei / 1_000_000_000_000_000_000m;
                    MessageReceived?.Invoke(new ChatMessage(
                        ChatPlatform.Ethereum, "anonymous", $"received {eth:0.########} ETH", DateTimeOffset.Now, Kind: ChatMessageKind.Donation));
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                // A single failed poll shouldn't permanently kill the feed.
                Faulted?.Invoke(ex);
            }
        }
    }

    private async Task<List<EtherscanTx>> FetchRecentTransactionsAsync(CancellationToken ct)
    {
        var url = $"https://api.etherscan.io/v2/api?chainid=1&module=account&action=txlist&address={_address}&sort=desc&page=1&offset=25&apikey={_apiKey}";
        var json = await _http.GetStringAsync(url, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var resultProp) || resultProp.ValueKind != JsonValueKind.Array)
        {
            // On error, Etherscan's actually-useful detail (e.g. "Missing/Invalid
            // API Key") is in "result", not "message" (usually just "NOTOK").
            var detail = resultProp.ValueKind == JsonValueKind.String
                ? resultProp.GetString()
                : root.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";

            if (detail == "No transactions found")
                return [];

            throw new InvalidOperationException($"Etherscan API error: {detail}");
        }

        return JsonSerializer.Deserialize<List<EtherscanTx>>(resultProp.GetRawText()) ?? [];
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;

        if (_pollCts is not null)
        {
            await _pollCts.CancelAsync();
            if (_pollTask is not null)
                await _pollTask.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });
            _pollCts.Dispose();
            _pollCts = null;
        }
    }

    private sealed class EtherscanTx
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = "";

        [JsonPropertyName("to")]
        public string To { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "0";

        [JsonPropertyName("isError")]
        public string IsError { get; set; } = "0";
    }
}
