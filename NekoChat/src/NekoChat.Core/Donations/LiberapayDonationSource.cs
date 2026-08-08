using System.Text.Json;
using System.Text.Json.Serialization;
using NekoChat.Core.Http;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;

namespace NekoChat.Core.Donations;

/// <summary>
/// Polls Liberapay's public (undocumented but stable) payments feed for a creator's
/// account. Liberapay has no webhook or push API — this is the only way to observe
/// new donations without a public endpoint of our own. Fetches go through
/// <see cref="CurlHttpClient"/> — confirmed empirically that Liberapay (behind
/// Cloudflare) reliably 403s .NET's HttpClient while curl succeeds consistently.
/// Liberapay also carries no donor message field at all, only a name (often
/// null/anonymous) and an amount.
/// </summary>
public sealed class LiberapayDonationSource : IChatSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly string _username;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private long _lastSeenTransferId = -1;

    public ChatPlatform Platform => ChatPlatform.Liberapay;

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public LiberapayDonationSource(string username)
    {
        _username = username;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Baseline off the current max transfer id so connecting doesn't replay
        // the account's entire payment history as fresh alerts. A couple of
        // retries here since failing this once would permanently prevent connecting.
        var payments = await FetchPaymentsWithRetryAsync(ct);
        _lastSeenTransferId = payments
            .SelectMany(p => p.Transfers)
            .Select(t => t.Id)
            .DefaultIfEmpty(-1)
            .Max();

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

                var payments = await FetchPaymentsAsync(ct);
                var newTransfers = payments
                    .SelectMany(p => p.Transfers.Select(t => (Payment: p, Transfer: t)))
                    .Where(x => x.Transfer.Id > _lastSeenTransferId)
                    .OrderBy(x => x.Transfer.Id)
                    .ToList();

                foreach (var (payment, transfer) in newTransfers)
                {
                    var name = payment.PayerPublicName ?? payment.PayerUsername ?? "anonymous";
                    var body = $"donated {transfer.Amount.Amount} {transfer.Amount.Currency}";

                    MessageReceived?.Invoke(new ChatMessage(
                        ChatPlatform.Liberapay, name, body, DateTimeOffset.Now, Kind: ChatMessageKind.Donation));
                }

                if (newTransfers.Count > 0)
                    _lastSeenTransferId = newTransfers.Max(x => x.Transfer.Id);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                // A single failed poll (e.g. a transient Cloudflare 403) shouldn't
                // permanently kill the feed — report it and try again next interval.
                Faulted?.Invoke(ex);
            }
        }
    }

    private async Task<List<LiberapayPayment>> FetchPaymentsWithRetryAsync(CancellationToken ct, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await FetchPaymentsAsync(ct);
            }
            catch when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2) * attempt, ct);
            }
        }
    }

    private async Task<List<LiberapayPayment>> FetchPaymentsAsync(CancellationToken ct)
    {
        var json = await CurlHttpClient.GetStringAsync($"https://liberapay.com/{_username}/income/payments.json", ct);
        return JsonSerializer.Deserialize<List<LiberapayPayment>>(json) ?? [];
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

    private sealed class LiberapayPayment
    {
        [JsonPropertyName("payer_username")]
        public string? PayerUsername { get; set; }

        [JsonPropertyName("payer_public_name")]
        public string? PayerPublicName { get; set; }

        [JsonPropertyName("transfers")]
        public List<LiberapayTransfer> Transfers { get; set; } = [];
    }

    private sealed class LiberapayTransfer
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("amount")]
        public LiberapayAmount Amount { get; set; } = new();
    }

    private sealed class LiberapayAmount
    {
        [JsonPropertyName("amount")]
        public string Amount { get; set; } = "0";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "";
    }
}
