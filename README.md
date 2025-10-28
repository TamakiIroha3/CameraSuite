# CameraSuite

CameraSuite 是一套基于 .NET 8 的分布式视频监控软件，由认证端、影像源端、观看端三个独立进程组成。系统通过 WebSocket 控制平面分发指令，使用启用 AES-256 的 SRT 传输视频，MediaMTX 负责中转与录像，观看端使用 WPF + LibVLCSharp 播放所有流。

## 目录结构

```
src/
  Auth/CameraSuite.AuthService        # 认证端
  Source/CameraSuite.SourceService    # 影像源端
  Viewer/CameraSuite.ViewerHost       # 观看端（WPF）
  Common/CameraSuite.Shared           # 公共库
infra/
  mediamtx/                           # MediaMTX 可执行文件与示例配置
```

## 功能模块

- **AuthService**：控制台程序，生成一次性认证码，分配 SRT 端口与 AES 密钥，将流信息广播给观看端。
- **SourceService**：无界面服务，从本地 RTMP 获取视频，通过 FFmpeg 推送加密 SRT 并上报流状态。
- **ViewerHost**：桌面端，负责连接认证端、启动/管理 MediaMTX、平铺播放所有流并记录数据。
- **Shared**：共享的模型、配置、JSON 序列化上下文与 WebSocket 帮助类。

## 核心特性

- WebSocket 控制平面，支持自签证书与 TLS。
- SRT + AES-256 加密链路，认证端统一分配密钥。
- MediaMTX 预分配 listener，`PreallocatedSrtListeners` 可在配置中控制数量，无需因为新通道重启进程。
- 录像以 `.ts` 格式存储，路径满足 `recordings/port-<port>/<yyyyMMdd>/<HHmmss>.ts`。
- Viewer UI 支持平铺、缩放、实时状态提示。

## 环境要求

- ViewerHost：Windows 10/11 或 Windows Server（WPF 环境）。
- AuthService/SourceService：Windows 或 Linux（需安装 .NET 8 Runtime）。
- 工具：MediaMTX、FFmpeg、LibVLC、SRT（随 MediaMTX/FFmpeg 提供）。
- 网络：默认控制平面端口 5051/TCP，SRT 端口区间 6000-6999/UDP。

## 快速开始

```powershell
git clone <repo>
cd d:\Project\C#\camera
dotnet build CameraSuite.sln
```

1. 将 MediaMTX、FFmpeg、LibVLC 放入 PATH 或写入配置。
2. 编辑配置：
   - `src/Auth/CameraSuite.AuthService/appsettings.json`
   - `src/Source/CameraSuite.SourceService/appsettings.json`
   - `src/Viewer/CameraSuite.ViewerHost/appsettings.json`
3. 启动认证端：`dotnet run --project src/Auth/CameraSuite.AuthService`，记录认证码并按提示登记观看端。
4. 启动观看端：`dotnet run --project src/Viewer/CameraSuite.ViewerHost`，程序会连接认证端、启动 MediaMTX 并打开播放 UI。
5. 启动影像源端：`dotnet run --project src/Source/CameraSuite.SourceService`，输入认证端信息及通道名后即可推送本地 RTMP。

开发环境可通过 `appsettings.Development.json` 获取更详细日志；生产环境建议使用环境变量或外部配置统一管理敏感信息。

## 配置速览

| 配置段 | 重要键 | 说明 |
| --- | --- | --- |
| `CameraSuite:Auth` | `ControlPort`、`SrtPortRangeStart/End`、`UseTls`、`CertificatePath` | 控制平面端口、SRT 端口区间、TLS 策略 |
| `CameraSuite:Viewer` | `MediamtxExecutable`、`MediamtxConfigPath`、`PreallocatedSrtListeners`、`ControlPlaneUri` | MediaMTX 配置、预分配 Listener 数量、控制平面地址 |
| `CameraSuite:Source` | `LocalRtmpUrl`、`FfmpegExecutable`、`ControlPlanePath`、`UseTls` | RTMP 输入及 FFmpeg 路径、控制平面路径、TLS 设置 |
| `CameraSuite:Recording` | `RootDirectory`、`SegmentMinutes`、`AutoStartRecording` | 录像目录、切片时长、是否自动录像 |

> `PreallocatedSrtListeners` 必须小于等于认证端 SRT 端口区间容量，否则可能出现端口不足。

## 控制与数据流程

1. 认证端启动后生成随机认证码，等待观看端在 `/ws/viewer` 建立连接。
2. 观看端发送 `viewer_hello`，携带预分配的 Listener（端口 + AES Key/IV），认证端缓存并标记可用。
3. 影像源端连接 `/ws/source`，发送 `auth_request`（认证码 + 通道名）。
4. 认证端验证成功后分配 Listener，将端口和密钥发回影像源端，同时通过 `stream_announce` 广播给观看端。
5. MediaMTX 已监听对应端口，影像源端直接推送加密 SRT；观看端用 LibVLC 播放，并将录像写入指定目录。
6. 影像源端持续上报 `stream_state`（Starting/Ready/Failed/Stopped），认证端在失败或超时时自动回收 Listener。

## 录像策略

- 默认目录：`recordings/port-<port>/<yyyyMMdd>/<HHmmss>.ts`。
- 可通过 `CameraSuite:Recording` 调整根目录、分段时长、自动录像开关。
- Viewing UI 会显示流状态与错误信息，便于排查问题。

## 开发调试

- `dotnet watch run --project <Project>` 可缩短迭代时间。
- 使用 OBS 或 FFmpeg 推送本地 RTMP 测试全链路。
- 发布示例：

  ```powershell
  dotnet publish src/Auth/CameraSuite.AuthService     -c Release -r win-x64 --self-contained false
  dotnet publish src/Source/CameraSuite.SourceService -c Release -r win-x64 --self-contained false
  dotnet publish src/Viewer/CameraSuite.ViewerHost    -c Release -r win-x64 --self-contained false
  ```

  根据目标平台替换 `-r`，如需独立运行可加 `--self-contained true`。

## 部署

完整部署、TLS、安全与运维建议请参考 [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)。
