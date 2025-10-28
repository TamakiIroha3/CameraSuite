# CameraSuite Deployment Guide

This guide targets production or pre-production environments. ViewerHost requires Windows (WPF), while AuthService and SourceService can run on Windows or Linux.

## 1. Prerequisites

| Component | Recommended version | Notes |
| --- | --- | --- |
| Operating system | Windows 10/11, Windows Server 2019+, Ubuntu 22.04+ | ViewerHost must run on Windows; Auth/Source are cross-platform. |
| .NET Runtime | .NET 8 Runtime / ASP.NET Core Runtime | Install `dotnet-hosting-8.0-win.exe` on Windows or `dotnet-hosting-8.0` on Linux. |
| MediaMTX | Latest stable release | Download the official binary and place it in a fixed folder. |
| FFmpeg | 4.4 or later | Required by SourceService; ensure `ffmpeg` is in PATH. |
| LibVLC | LTS version matching LibVLCSharp | Bundle `libvlc.dll` and the `plugins` directory with ViewerHost. |
| SRT tooling | Bundled with MediaMTX/FFmpeg | For TLS, prepare certificates via OpenSSL/certutil if necessary. |

### Sample directory layout

```
C:\CameraSuite\
  auth\
  source\
  viewer\
  media\
    mediamtx.exe
    mediamtx.yaml
  ffmpeg\
  libvlc\
  recordings\
  config\
```

Keep `recordings` and `config` on storage that can be backed up and monitored.

## 2. Build & Publish

```powershell
dotnet publish src/Auth/CameraSuite.AuthService     -c Release -r win-x64 --self-contained false -o publish/auth
dotnet publish src/Source/CameraSuite.SourceService -c Release -r win-x64 --self-contained false -o publish/source
dotnet publish src/Viewer/CameraSuite.ViewerHost     -c Release -r win-x64 --self-contained false -o publish/viewer
```

Copy the published folders to the target machine and include:

- `infra/mediamtx/mediamtx.exe` and its base configuration.
- FFmpeg binaries.
- LibVLC runtime files.

If you prefer self-contained deployments, add `--self-contained true` (and optionally `--p:PublishSingleFile=true`).

## 3. Configuration

Edit the following configuration files in the deployment directory:

- `auth/appsettings.json`
- `source/appsettings.json`
- `viewer/appsettings.json`

### Auth (`CameraSuite:Auth`)

| Key | Description |
| --- | --- |
| `ControlPort` | WebSocket control plane port (default 5051). |
| `SrtPortRangeStart/End` | Range of SRT listener ports; open them in the firewall. |
| `UseTls` | Enable HTTPS/WSS if required. |
| `AutoGenerateCertificate` | Create a self-signed certificate when none is supplied. |
| `CertificatePath` / `CertificatePassword` | External PFX certificate. |

### Viewer (`CameraSuite:Viewer`)

| Key | Description |
| --- | --- |
| `MediamtxExecutable` / `MediamtxConfigPath` | Paths to the MediaMTX binary and config. |
| `PreallocatedSrtListeners` | Number of SRT listener ports to pre-start (must not exceed the auth SRT range). |
| `ControlPlaneUri` | URI of the auth control plane (e.g. `ws://auth-host:5051/ws/viewer`). |
| `TrustAllCertificates` | Set to true if using self-signed certificates. |
| `Recording.RootDirectory` | Recording destination with write permissions. |

### Source (`CameraSuite:Source`)

| Key | Description |
| --- | --- |
| `LocalRtmpUrl` | Local RTMP ingest entry (default `rtmp://127.0.0.1/live`). |
| `FfmpegExecutable` | Path to FFmpeg. |
| `ControlPlanePath` | Path portion of the auth control plane (`/ws/source` by default). |
| `UseTls` / `TrustAllCertificates` | Mirror the auth configuration. |

For production, place overrides in `appsettings.Production.json` and set `DOTNET_ENVIRONMENT=Production`.

## 4. Service Hosting

### 4.1 AuthService (Windows service)

```powershell
sc create CameraSuiteAuth binPath= "C:\CameraSuite\auth\CameraSuite.AuthService.exe" start= auto
sc start CameraSuiteAuth
```

Or with NSSM:

```powershell
nssm install CameraSuiteAuth C:\CameraSuite\auth\CameraSuite.AuthService.exe
nssm set CameraSuiteAuth AppDirectory C:\CameraSuite\auth
nssm start CameraSuiteAuth
```

### 4.2 SourceService (systemd example)

`/etc/systemd/system/camerasuite-source.service`:

```ini
[Unit]
Description=CameraSuite Source Service
After=network.target

[Service]
WorkingDirectory=/opt/camerasuite/source
ExecStart=/usr/bin/dotnet CameraSuite.SourceService.dll
Restart=on-failure
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now camerasuite-source
```

### 4.3 ViewerHost (Windows)

ViewerHost is a WPF desktop app. Use Task Scheduler to auto-start after login:

```powershell
Register-ScheduledTask -TaskName "CameraSuiteViewer" `
  -Trigger (New-ScheduledTaskTrigger -AtLogOn) `
  -Action (New-ScheduledTaskAction -Execute "C:\CameraSuite\viewer\CameraSuite.ViewerHost.exe") `
  -RunLevel Highest
```

## 5. TLS

- With `UseTls=true`, AuthService loads the PFX indicated by `CertificatePath`. If none is provided and `AutoGenerateCertificate=true`, a self-signed certificate is created (suitable for lab environments).
- SourceService and ViewerHost can accept self-signed certificates by setting `TrustAllCertificates=true`, or you can import the CA certificate into the OS trust store.
- After renewing certificates, restart AuthService.

## 6. Networking & Storage

- Firewall: open the control plane port (default 5051/TCP) and the SRT range (default 6000–6999/UDP).
- Monitor available storage under the recording root. Rotate or archive `.ts` files regularly, or mount NAS/SAN storage.

## 7. Operations Notes

- Logs are written to stdout/stderr. Integrate with Serilog, Windows Event Log, or syslog as needed.
- Track MediaMTX and FFmpeg CPU/memory usage; adjust `PreallocatedSrtListeners` based on expected peak load.
- Preallocated listeners allow immediate stream authorisation without restarting MediaMTX. Ensure the auth service has a matching port range configured.

## 8. Upgrade Procedure

1. Validate the new build in a test environment.
2. Stop AuthService, SourceService, and ViewerHost.
3. Back up the current binaries and configuration.
4. Copy over the new publish output, keeping `appsettings*.json` and the recording directory.
5. Restart services and verify WebSocket handshake, SRT push, playback, and recording.
6. For TLS setups, confirm certificate validity and trust chain.

After these steps the new version should be live. For larger deployments consider a configuration service, reverse proxy, or containerisation (ViewerHost remains desktop-bound because of WPF).
