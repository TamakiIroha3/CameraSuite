# CameraSuite ����ָ��

���Ľ��� CameraSuite ��������׼���������Ĳ��𷽷���ViewerHost ���� WPF���������� Windows��AuthService �� SourceService �ɲ����� Windows �� Linux ��������

## 1. ����׼��

| ��� | ����汾 | ˵�� |
| --- | --- | --- |
| ����ϵͳ | Windows 10/11��Windows Server 2019+��Ubuntu 22.04+ | ViewerHost ����ʹ�� Windows��Auth/Source �ɿ�ƽ̨���� |
| .NET Runtime | .NET 8 Runtime / ASP.NET Core Runtime | Windows ��װ `dotnet-hosting-8.0-win.exe`��Linux ��װ `dotnet-hosting-8.0`�� |
| MediaMTX | �����ȶ��� | ʹ�ùٷ������ư��������ڹ̶�Ŀ¼�� |
| FFmpeg | 4.4 ������ | SourceService ��Ҫ `ffmpeg` ��ִ���ļ��� |
| LibVLC | �� LibVLCSharp ���װ汾 | ViewerHost ����Ӧ�÷ַ� `libvlc.dll` �� `plugins` Ŀ¼�� |
| SRT ���� | MediaMTX / FFmpeg ���� | ������ TLS���ɽ��� OpenSSL �� certutil ����֤�顣 |

### Ŀ¼ʾ��

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

���齫 `recordings`��`config` ���ڿɱ����Ҿ߱��㹻 IOPS �Ĵ洢�ϡ�

## 2. �����뷢��

```powershell
dotnet publish src/Auth/CameraSuite.AuthService     -c Release -r win-x64 --self-contained false -o publish/auth
dotnet publish src/Source/CameraSuite.SourceService -c Release -r win-x64 --self-contained false -o publish/source
dotnet publish src/Viewer/CameraSuite.ViewerHost    -c Release -r win-x64 --self-contained false -o publish/viewer
```

���� `publish/*` ��Ŀ�����������ͬ����

- `infra/mediamtx/mediamtx.exe` �������ã�
- FFmpeg ��ִ���ļ���
- LibVLC ����ʱ�ļ���

������ȫ�԰������𣬿�׷�� `--self-contained true`����ѡ `--p:PublishSingleFile=true`����

## 3. �����ļ�

����Ŀ¼�������

- `auth/appsettings.json`
- `source/appsettings.json`
- `viewer/appsettings.json`

### `CameraSuite:Auth`

| �� | ˵�� |
| --- | --- |
| `ControlPort` | ����ƽ��˿ڣ�Ĭ�� 5051/TCP���� |
| `SrtPortRangeStart/End` | SRT �����˿����䣨�迪�ŷ���ǽ���� |
| `UseTls` | �Ƿ����� HTTPS/WSS�� |
| `AutoGenerateCertificate` | ��֤��ʱ�Զ�������ǩ֤�顣 |
| `CertificatePath` / `CertificatePassword` | �ⲿ PFX ֤��·������ |

### `CameraSuite:Viewer`

| �� | ˵�� |
| --- | --- |
| `MediamtxExecutable` / `MediamtxConfigPath` | MediaMTX ��ִ���ļ�������·���� |
| `PreallocatedSrtListeners` | Ԥ����� SRT listener ���������� �� ��֤�� SRT �˿ڳ��������� |
| `ControlPlaneUri` | ָ����֤�˿���ƽ�棬���� `ws://auth-host:5051/ws/viewer`�� |
| `TrustAllCertificates` | ʹ����ǩ֤��ʱ��Ϊ true�� |
| `Recording.RootDirectory` | ¼��Ŀ¼����߱�дȨ�ޡ� |

### `CameraSuite:Source`

| �� | ˵�� |
| --- | --- |
| `LocalRtmpUrl` | ���� RTMP ������ڣ�Ĭ�� `rtmp://127.0.0.1/live`�� |
| `FfmpegExecutable` | FFmpeg ·���� |
| `ControlPlanePath` | ����ƽ��·����Ĭ�� `/ws/source`�� |
| `UseTls` / `TrustAllCertificates` | ����֤�����ñ���һ�¡� |

����������ʹ�� `appsettings.Production.json` ������ `DOTNET_ENVIRONMENT=Production`��

## 4. ����ʾ��

### 4.1 Windows ����AuthService��

```powershell
sc create CameraSuiteAuth binPath= "C:\CameraSuite\auth\CameraSuite.AuthService.exe" start= auto
sc start CameraSuiteAuth
```

��ʹ�� NSSM��

```powershell
nssm install CameraSuiteAuth C:\CameraSuite\auth\CameraSuite.AuthService.exe
nssm set CameraSuiteAuth AppDirectory C:\CameraSuite\auth
nssm start CameraSuiteAuth
```

### 4.2 Linux systemd��SourceService��

`/etc/systemd/system/camerasuite-source.service`��

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

### 4.3 ViewerHost ������

ViewerHost Ϊ����Ӧ�ã���ͨ������ƻ��ڵ�¼���Զ�������

```powershell
Register-ScheduledTask -TaskName "CameraSuiteViewer" `
  -Trigger (New-ScheduledTaskTrigger -AtLogOn) `
  -Action (New-ScheduledTaskAction -Execute "C:\CameraSuite\viewer\CameraSuite.ViewerHost.exe") `
  -RunLevel Highest
```

## 5. TLS ����

- `UseTls=true` ʱ����֤�����ȼ��� `CertificatePath` ָ���� PFX����ȱʧ�� `AutoGenerateCertificate=true`�����Զ�������ǩ֤�飨�������ڱ��ܵ�������������
- ViewerHost��SourceService ��ͨ�� `TrustAllCertificates=true` ������ǩ֤�飬�� CA ����ϵͳ֤����������
- ����֤���������֤�˼�����Ч��

## 6. ������洢

- ����ǽ���п���ƽ��˿ڣ�Ĭ�� TCP 5051���� SRT �˿����䣨Ĭ�� UDP 6000-6999����
- ����������鵵¼��Ŀ¼���ɲ��� NAS/SAN �ȹ����洢��

## 7. ��ά����

- ��־Ĭ�����������̨������� Serilog��Windows Event Log �� syslog �ۺϡ�
- ��� MediaMTX��FFmpeg �� CPU/�ڴ�ռ�ã�����ҵ���ֵ���� `PreallocatedSrtListeners` �� SRT �˿ڷ�Χ��
- Ԥ���� Listener ����ͨ���������� MediaMTX�����豣֤��֤����ۿ�������һ�¡�

## 8. ��������

1. �ڲ��Ի�����֤�°汾��
2. ֹͣ AuthService��SourceService��ViewerHost��
3. ���ݷ���Ŀ¼�������ļ���
4. �����°汾������ `appsettings*.json` �� `recordings`����
5. ����������֤ WebSocket ���֡�����/���š�¼���Ƿ�������
6. ������ TLS��ȷ����֤����Ч����·���š�

������ϲ��輴�������������Ҫ���ģ���𣬿ɿ��������������ġ����������������������ViewerHost ���� WPF �����Խ��鲿���� Windows ���滷������
