# CameraSuite

CameraSuite is a distributed video monitoring toolkit built on .NET 8. It runs three independent processes:

- **AuthService** - console host that issues one-time auth codes, allocates SRT listener ports and AES secrets, and coordinates the other components through WebSocket control messages.
- **SourceService** - headless agent that ingests a local RTMP feed, republishes it over encrypted SRT via FFmpeg, and reports stream state changes.
- **ViewerHost** - WPF + console application that embeds LibVLCSharp for playback, manages a local MediaMTX instance for relay/recording, and displays all incoming streams in a tiled layout.

## Repository Layout

```
src/
  Auth/CameraSuite.AuthService
  Source/CameraSuite.SourceService
  Viewer/CameraSuite.ViewerHost
  Common/CameraSuite.Shared
infra/
  mediamtx/
```

## Requirements

- Windows 10/11 (ViewerHost requires WPF), Windows Server 2019+, or Linux distributions that support .NET 8 (Auth/Source can run cross-platform).
- [.NET 8 SDK](https://dotnet.microsoft.com/) for build/debug; runtime only for deployment.
- [MediaMTX](https://github.com/bluenviron/mediamtx) latest stable release.
- [FFmpeg](https://ffmpeg.org/) 4.4 or later.
- [LibVLC](https://www.videolan.org/vlc/) runtime files bundled with ViewerHost.
- Network access to the Auth control plane (default TCP 5051) and the SRT listener range (default UDP 6000-6999).

## Quick Start

```powershell
git clone <repo>
cd d:\Project\C#\camera
dotnet build CameraSuite.sln
```

1. Install MediaMTX, FFmpeg, and LibVLC (ensure executables are in PATH or configured in `appsettings`).
2. Configure each service:
   - `src/Auth/CameraSuite.AuthService/appsettings.json`
   - `src/Source/CameraSuite.SourceService/appsettings.json`
   - `src/Viewer/CameraSuite.ViewerHost/appsettings.json`
3. Start auth: `dotnet run --project src/Auth/CameraSuite.AuthService`, note the auth code, and register the viewer endpoint.
4. Start viewer: `dotnet run --project src/Viewer/CameraSuite.ViewerHost`; it connects to auth, boots MediaMTX, and opens the tiled UI.
5. Start each source: `dotnet run --project src/Source/CameraSuite.SourceService`, provide auth endpoint/code/channel, and begin pushing RTMP locally.

Use `appsettings.Development.json` for verbose logging in development; use environment variables or external configuration for production secrets.

## Configuration Highlights

| Section | Purpose |
| --- | --- |
| `CameraSuite:Auth` | Control-plane port (HTTP/WSS), SRT port range, TLS certificate settings. |
| `CameraSuite:Viewer` | MediaMTX executable/config, control-plane URI, viewer identifier, `PreallocatedSrtListeners` (number of SRT listeners started at boot), certificate trust policy. |
| `CameraSuite:Source` | Default channel, local RTMP ingest URL, FFmpeg path, control-plane path, TLS and retry policy. |
| `CameraSuite:Recording` | Recording root, auto-record toggle, segment length (minutes). Files are named `channel_yyyyMMdd_HHmmss.ts`. |

`PreallocatedSrtListeners` defines how many MediaMTX SRT listeners are created ahead of time (must not exceed the auth SRT range) to avoid restarting MediaMTX when new streams are authorised.

## Control & Data Flow

1. AuthService starts, generates an auth code, and waits for ViewerHost on `/ws/viewer`.
2. ViewerHost sends `viewer_hello` with the list of preallocated listeners (port + AES material). AuthState stores the slots.
3. SourceService connects to `/ws/source`, sending `auth_request` (auth code + channel).
4. AuthService selects a free slot, returns the port and AES material to the source, and announces the stream to the viewer.
5. MediaMTX is already listening on the allocated port; the encrypted SRT feed is accepted without restarting. ViewerHost plays via LibVLC using the same port/passphrase, and recordings are stored under `recordings/port-<port>/...`.
6. SourceService publishes lifecycle updates (`stream_state`). AuthService reclaims the slot on failure or timeout.

## Recording

- Default location: `recordings/port-<port>/<yyyyMMdd>/<HHmmss>.ts`.
- Configure the root directory and segment length via `CameraSuite:Recording`.
- ViewerHost shows stream state and allows focusing on individual feeds.

## Development Tips

- Use `dotnet watch run --project <Project>` for quick iterations.
- Enable `appsettings.Development.json` for detailed logs.
- Use OBS/FFmpeg to push local RTMP streams when testing end-to-end.
- Publish for deployment, e.g.:

  ```powershell
  dotnet publish src/Auth/CameraSuite.AuthService    -c Release -r win-x64 --self-contained false
  dotnet publish src/Source/CameraSuite.SourceService -c Release -r win-x64 --self-contained false
  dotnet publish src/Viewer/CameraSuite.ViewerHost    -c Release -r win-x64 --self-contained false
  ```

  Swap the Runtime Identifier (`-r`) for other platforms; add `--self-contained true` if you need standalone binaries.

## Deployment Guide

Detailed deployment, TLS, and service-hosting instructions are available in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
