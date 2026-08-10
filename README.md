# Neko Streaming Tools

Three lightweight, Windows-only desktop tools for solo streamers/VTubers — built to be affordable to run on lower-end hardware, no subscription, no third-party service in the middle.

## NekoStreamer

Accepts a local OBS RTMP stream, buffers it with a configurable delay, and multistreams (stream-copy, no transcoding) out to Twitch/YouTube/Kick/X simultaneously.

- `src/NekoStreamer.App` — WPF control panel (destinations grid, delay, start/stop)
- `src/NekoStreamer.Core` — ingest (MediaMTX), delay buffer, multistream pusher (ffmpeg), Win32 Job Object crash-safety
- `tools/mediamtx/` — vendored MediaMTX binary, committed directly (see **Setup** below for why)

## NekoChat

Unifies Twitch, Kick, and YouTube chat into a single feed, with:
- Follow/sub/gift/raid/host/superchat/donation alerts merged into the same feed
- Donation aggregation from StreamElements, Liberapay, Bitcoin, and Ethereum (no payment processing — reads existing donation platforms' alert feeds)
- Combined cross-platform viewer count
- A transparent, borderless overlay window and a separate Alert Box (GIF/image + sound popups) — both meant to be added as OBS Window Capture sources
- A private control panel (tabbed: Chat Sources / Donations / Alert Box) for setup, separate from what's shown on stream

- `src/NekoChat.App` — WPF control panel + overlay + alert box windows
- `src/NekoChat.Core` — chat sources, donation sources, viewer-count polling, settings

## NekoTrends

A news-style feed of what's trending among VTubers across Twitch, YouTube and Kick — who's live, who's big, and who's unusually big *right now*.

- **Top Now** — straight live viewer ranking across all three platforms, works on first launch.
- **Trending** — momentum: each streamer measured against *their own* recent baseline, so a channel sitting at its normal size doesn't crowd out one that's actually blowing up.
- **Watchlist** — just the channels you track, polled directly whatever their size.
- **Alerts** — fires once when a tracked channel goes live, crosses a viewer count, or starts spiking.

- `src/NekoTrends.App` — WPF feed window and alert popups
- `src/NekoTrends.Core` — per-platform discovery, watchlist polling, local history, trend analysis, alerts

The feed renders instantly at launch from the last stored refresh, then updates in the background. All history is stored locally under `%LOCALAPPDATA%\NekoTrends` and never leaves the machine.

### How each platform is discovered

The three platforms give wildly different amounts of help here, which drives the whole design:

| Platform | VTuber signal | Approach |
|---|---|---|
| **Twitch** | Real, widely-used `VTuber` tag | Automatic. Scans the top ~1,000 live streams and keeps tagged VTubers. |
| **YouTube** | None, but titles are searchable | Automatic, quota-limited. Live search + a batched viewer-count lookup. |
| **Kick** | **None at all** | Watchlist only, with assisted discovery. |

Kick genuinely has nothing to filter on: it has no VTuber category or subcategory, and its live-browse API returns an empty `tags` array for every stream — a keyword sweep of the top 300 live English streams matched zero channels. So Kick is driven entirely by the watchlist. **Scan for VTubers** searches Kick and proposes candidates, but never adds them itself: Kick's search is a bare substring match that happily returns `live2dance4ever` for a "live2d" query, so a human stays in the loop.

### Trackers

Three ways to follow things, all on the **Trackers** tab.

**Tracked channels** are polled directly on every refresh regardless of ranking. This is the only way to follow a VTuber below Twitch's top-1,000 discovery scan, and the only way NekoTrends sees Kick at all. Add by login name (Twitch), channel ID (YouTube), or slug (Kick) — pasted URLs work too.

**Keyword rules** follow a group without naming every channel — an agency, a game, a language. Terms are comma-separated and match against title, category, tags and channel name. Matching streams get a labelled chip and can be filtered in the feed. A rule can optionally also be pushed into YouTube search; that's off by default because each extra term costs 100 quota units.

**Alert rules** are edge-triggered — you're told *once* when something happens, not on every refresh while it stays true. They're scoped to tracked channels by default, since an unscoped "went live" rule across all of discovery would be pure noise. Alerts that fire while the app is closed are still waiting on the Alerts tab when you open it.

### Background collection

Momentum compares a channel against its own recent baseline, so gaps in sampling make it noisier — and a baseline built only from the hours you happened to have the app open is biased toward your own timezone.

**Collect in the background** (Sources tab) registers a Windows Scheduled Task that runs the app headlessly on the same interval, so history keeps accumulating while NekoTrends is closed. It runs interactive-only — that is, only while you're logged in — because the stored tokens are DPAPI-encrypted to your Windows account and can't be decrypted otherwise. The button unregisters the task again.

## Setup

All three apps target **.NET 9, Windows only** (WPF). Build with `dotnet build` from each app's own directory (each has its own `.sln`).

Secrets (OAuth tokens, donation platform tokens) are encrypted at rest via Windows DPAPI, scoped to the current Windows user account — never stored in plaintext.

**NekoStreamer only**: `tools/mediamtx/mediamtx.exe` (~53MB) is committed directly rather than gitignored — excluding it made a fresh clone fail to build, since the app's `.csproj` copies it as a Content item and MSBuild hard-errors when that file is missing. If you ever need to update it, grab the latest Windows build from [MediaMTX releases](https://github.com/bluenviron/mediamtx/releases) and replace `NekoStreamer/tools/mediamtx/mediamtx.exe`.

## Releases

Tagged releases include prebuilt, ready-to-run binaries for all three apps (no .NET SDK required, just Windows + the .NET 9 desktop runtime).

## Registering Your Own Apps (Twitch / YouTube)

NekoChat's Twitch and YouTube integrations use OAuth, which means **each person running NekoChat needs their own app registration** with Twitch/Google — the Client ID (and for YouTube, Client Secret) aren't something that can be baked into the app or shared between users. This only takes a few minutes and both are free.

Kick needs no registration at all — just your channel name. StreamElements/Liberapay/Bitcoin/Ethereum donation sources are explained inline in the app's Donations tab.

**NekoTrends needs less than NekoChat does.** Its Twitch setup is identical to the steps below, except it requests **no scopes at all** — it only reads public stream listings, so the token exists purely to satisfy Helix's authentication requirement. Its YouTube setup is much lighter: it needs only a plain **API key** (**APIs & Services → Credentials → Create Credentials → API key**, with YouTube Data API v3 enabled), *not* the OAuth consent screen and Client Secret described in the YouTube section below — those are public reads, not access to your own account. YouTube is optional for NekoTrends entirely; Twitch and Kick work without it.

### Twitch

1. Go to the [Twitch Developer Console](https://dev.twitch.tv/console) and log in (you'll need 2FA enabled on your Twitch account).
2. **Applications** tab → **Register Your Application**.
3. Pick any name, and a **Category** (e.g. "Application Integration" / "Chat Bot").
4. For **OAuth Redirect URLs**: NekoChat uses Twitch's [Device Code Grant flow](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/#device-code-grant-flow), which never actually redirects anywhere — but Twitch's registration form requires *some* non-empty value here regardless. `http://localhost` is the commonly-used placeholder for this exact situation (this isn't documented by Twitch, just the standard community workaround for apps that only use Device Code Grant).
5. Create the app, open it, and copy the **Client ID** — that's the only thing NekoChat needs. No client secret required.
6. Paste the Client ID into NekoChat's Twitch tab and hit Connect — it'll show you a code and a link to twitch.tv/activate to approve it.

### YouTube

1. Go to the [Google Cloud Console](https://console.cloud.google.com/) and create a new project (or reuse one).
2. **APIs & Services → Library** — search for and enable the **YouTube Data API v3**.
3. **APIs & Services → OAuth consent screen** — set it up as **External**, fill in the required fields. It'll stay in **Testing** status, which is fine.
4. Still on the consent screen, add **your own Google account** under **Test users** — without this step, sign-in will fail with an "Access blocked: has not completed the Google verification process" error, even though it's your own app.
5. **APIs & Services → Credentials → Create Credentials → OAuth client ID**, application type **Desktop app**.
6. Copy the **Client ID** and **Client Secret** into NekoChat's YouTube tab and hit Connect — it opens your browser for Google sign-in.
7. You need to actually be live-streaming on YouTube for chat messages to start appearing (YouTube only exposes a live chat ID for an active broadcast).
