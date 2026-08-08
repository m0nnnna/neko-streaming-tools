namespace NekoChat.Core.Models;

/// <summary>
/// A single feed entry — either a normal chat message (Kind == Message, Body is
/// the chat text) or an alert (follow/sub/gift/raid/superchat/etc, Body is a
/// human-readable description already formatted for display, e.g. "gifted 5 subs"
/// or "raided with 120 viewers").
/// </summary>
public sealed record ChatMessage(
    ChatPlatform Platform,
    string AuthorName,
    string Body,
    DateTimeOffset Timestamp,
    string? AuthorColor = null,
    ChatMessageKind Kind = ChatMessageKind.Message);
