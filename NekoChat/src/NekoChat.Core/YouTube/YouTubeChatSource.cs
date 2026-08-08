using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using NekoChat.Core.Models;
using NekoChat.Core.Sources;

namespace NekoChat.Core.YouTube;

/// <summary>
/// Watches the authenticated user's own active YouTube live broadcast chat. Unlike
/// Twitch/Kick there's no push option available to a third-party app — this polls
/// liveChatMessages.list at whatever interval YouTube's response tells us to use.
/// </summary>
public sealed class YouTubeChatSource : IChatSource
{
    private readonly YouTubeService _service;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public ChatPlatform Platform => ChatPlatform.YouTube;

    public bool IsConnected { get; private set; }

    public event Action<ChatMessage>? MessageReceived;
    public event Action<Exception>? Faulted;

    public YouTubeChatSource(UserCredential credential)
    {
        _service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "NekoChat",
        });
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // mine, broadcastStatus, and id are mutually exclusive filters — YouTube
        // rejects the request if more than one is set. broadcastStatus alone is
        // enough: liveBroadcasts.list is inherently scoped to the authenticated
        // caller's own channel, unlike a public search endpoint.
        var listRequest = _service.LiveBroadcasts.List("snippet");
        listRequest.BroadcastStatus = LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active;

        var response = await listRequest.ExecuteAsync(ct);
        var liveChatId = response.Items?.FirstOrDefault()?.Snippet?.LiveChatId
            ?? throw new InvalidOperationException("No active YouTube live broadcast found — start streaming on YouTube first.");

        IsConnected = true;
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(liveChatId, _pollCts.Token), CancellationToken.None);
    }

    private async Task PollLoopAsync(string liveChatId, CancellationToken ct)
    {
        try
        {
            string? pageToken = null;
            while (!ct.IsCancellationRequested)
            {
                var request = _service.LiveChatMessages.List(liveChatId, "snippet,authorDetails");
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(ct);

                foreach (var item in response.Items)
                {
                    var message = ToChatMessage(item);
                    if (message is not null)
                        MessageReceived?.Invoke(message);
                }

                pageToken = response.NextPageToken;
                var delay = TimeSpan.FromMilliseconds(response.PollingIntervalMillis ?? 5000);
                await Task.Delay(delay, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    private static ChatMessage? ToChatMessage(Google.Apis.YouTube.v3.Data.LiveChatMessage item)
    {
        var snippet = item.Snippet;
        if (snippet is null)
            return null;

        var author = item.AuthorDetails?.DisplayName ?? "unknown";
        var timestamp = snippet.PublishedAtDateTimeOffset ?? DateTimeOffset.Now;

        switch (snippet.Type)
        {
            case "textMessageEvent":
                return new ChatMessage(ChatPlatform.YouTube, author, snippet.DisplayMessage ?? "", timestamp);

            case "superChatEvent" when snippet.SuperChatDetails is { } sc:
                return new ChatMessage(ChatPlatform.YouTube, author,
                    $"Super Chat {sc.AmountDisplayString}{(string.IsNullOrEmpty(sc.UserComment) ? "" : $": {sc.UserComment}")}",
                    timestamp, Kind: ChatMessageKind.SuperChat);

            case "superStickerEvent" when snippet.SuperStickerDetails is { } ss:
                return new ChatMessage(ChatPlatform.YouTube, author,
                    $"Super Sticker {ss.AmountDisplayString}", timestamp, Kind: ChatMessageKind.SuperSticker);

            case "newSponsorEvent" when snippet.NewSponsorDetails is { } ns:
                return new ChatMessage(ChatPlatform.YouTube, author,
                    $"became a member ({ns.MemberLevelName})", timestamp, Kind: ChatMessageKind.NewMember);

            case "memberMilestoneChatEvent" when snippet.MemberMilestoneChatDetails is { } mm:
                return new ChatMessage(ChatPlatform.YouTube, author,
                    $"member for {mm.MemberMonth} month(s) ({mm.MemberLevelName})", timestamp, Kind: ChatMessageKind.MemberMilestone);

            default:
                // Deletions, bans, poll/tombstone events, etc. — nothing to show in the feed.
                return null;
        }
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
}
