# Neko Streaming Tools

A pair of lightweight, Windows-only desktop tools for solo streamers/VTubers — built to be affordable to run on lower-end hardware, no subscription, no third-party service in the middle.

## NekoStreamer

Accepts a local OBS RTMP stream, buffers it with a configurable delay, and multistreams (stream-copy, no transcoding) out to Twitch/YouTube/Kick/X simultaneously.

- `src/NekoStreamer.App` — WPF control panel (destinations grid, delay, start/stop)
- `src/NekoStreamer.Core` — ingest (MediaMTX), delay buffer, multistream pusher (ffmpeg), Win32 Job Object crash-safety
- `tools/mediamtx/` — vendored MediaMTX binary (gitignored — see **Setup** below)

## NekoChat

Unifies Twitch, Kick, and YouTube chat into a single feed, with:
- Follow/sub/gift/raid/host/superchat/donation alerts merged into the same feed
- Donation aggregation from StreamElements, Liberapay, Bitcoin, and Ethereum (no payment processing — reads existing donation platforms' alert feeds)
- Combined cross-platform viewer count
- A transparent, borderless overlay window and a separate Alert Box (GIF/image + sound popups) — both meant to be added as OBS Window Capture sources
- A private control panel (tabbed: Chat Sources / Donations / Alert Box) for setup, separate from what's shown on stream

- `src/NekoChat.App` — WPF control panel + overlay + alert box windows
- `src/NekoChat.Core` — chat sources, donation sources, viewer-count polling, settings

## Setup

Both apps target **.NET 9, Windows only** (WPF). Build with `dotnet build` from each app's own directory (each has its own `.sln`).

Secrets (OAuth tokens, donation platform tokens) are encrypted at rest via Windows DPAPI, scoped to the current Windows user account — never stored in plaintext.

**NekoStreamer only**: `tools/mediamtx/mediamtx.exe` is vendored but gitignored (it's a ~53MB third-party binary). Download the latest Windows build from [MediaMTX releases](https://github.com/bluenviron/mediamtx/releases) and place `mediamtx.exe` at `NekoStreamer/tools/mediamtx/mediamtx.exe` before building — or use the binaries already bundled in a [tagged release](../../releases) of this repo.

## Releases

Tagged releases include prebuilt, ready-to-run binaries for both apps (no .NET SDK required, just Windows + the .NET 9 desktop runtime).
