# CameraSuite

CameraSuite 是一个基于 .NET 8 的分布式监控套装，包含认证端、影像源端和观看端三部分。

## 组件概览

- **CameraSuite.Shared**：公共模型、配置和 NATS TLS 工具。
- **CameraSuite.AuthService**：控制台认证服务，分发 SRT 流端口与密钥。
- **CameraSuite.SourceService**：控制台影像源，接入本地 RTMP 并通过 FFmpeg 推送到 SRT。
- **CameraSuite.ViewerHost**：WPF + 控制台观看端，使用 LibVLCSharp 播放流并管理 mediamtx。

## 依赖

- [.NET 8 SDK](https://dotnet.microsoft.com/)
- [LibVLC](https://www.videolan.org/vlc/) runtime（LibVLCSharp 运行时依赖）
- [mediamtx](https://github.com/bluenviron/mediamtx) 最新版本
- [FFmpeg](https://ffmpeg.org/)
- [NATS Server](https://nats.io/)

## 构建

`powershell
cd d:\Project\C#\camera
dotnet build CameraSuite.sln
`

## 配置

每个服务有独立的 ppsettings.json：

- src/Auth/CameraSuite.AuthService/appsettings.json
- src/Source/CameraSuite.SourceService/appsettings.json
- src/Viewer/CameraSuite.ViewerHost/appsettings.json

可选参数如 NATS 地址、证书路径、FFmpeg/mediamtx 可执行文件、录像目录等均在配置中提供。

### TLS 与证书

CameraSuite.Shared 提供基础 TLS 封装，支持自签证书或跳过校验。示例 OpenSSL 流程：

`powershell
mkdir certs
openssl genrsa -out certs/ca.key 4096
openssl req -x509 -new -key certs/ca.key -days 365 -out certs/ca.crt -subj "/CN=CameraSuite-CA"
openssl genrsa -out certs/server.key 4096
openssl req -new -key certs/server.key -out certs/server.csr -subj "/CN=nats-server"
openssl x509 -req -in certs/server.csr -CA certs/ca.crt -CAkey certs/ca.key -CAcreateserial -days 365 -out certs/server.crt
openssl genrsa -out certs/client.key 4096
openssl req -new -key certs/client.key -out certs/client.csr -subj "/CN=nats-client"
openssl x509 -req -in certs/client.csr -CA certs/ca.crt -CAkey certs/ca.key -days 365 -out certs/client.crt
`

将 ca.crt 导入各端系统受信任根证书，并在配置中引用生成的客户端证书。

## 运行流程

1. **启动认证服务** (CameraSuite.AuthService)：
   - 自动生成认证码。
   - 提示输入观看端 IP/端口后等待影像源请求。

2. **启动观看端** (CameraSuite.ViewerHost)：
   - 控制台显示日志。
   - WPF UI 可查看所有 stream；mediamtx 自动维护配置并为每个流开启监听端口与录像。

3. **启动影像源** (CameraSuite.SourceService)：
   - 输入认证端地址、端口、认证码与通道名。
   - 在本地 RTMP 输入可用后将通过 FFmpeg 推流为 SRT。

## 录像

mediamtx 录像默认输出为 
ecordings/<channel>/%Y%m%d/%H%M%S.ts，可在 CameraSuite.ViewerHost/appsettings.json 中调整根目录与 segment 长度。

## 后续工作

- 增加自动化测试与 UI 自动化。
- 丰富 Viewer UI（缩放/录制开关等）。
- 引入日志集中与指标上报。
