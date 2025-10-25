# CameraSuite

CameraSuite ��һ������ .NET 8 �ķֲ�ʽ�����װ��������֤�ˡ�Ӱ��Դ�˺͹ۿ��������֡�

## �������

- **CameraSuite.Shared**������ģ�͡����ú� NATS TLS ���ߡ�
- **CameraSuite.AuthService**������̨��֤���񣬷ַ� SRT ���˿�����Կ��
- **CameraSuite.SourceService**������̨Ӱ��Դ�����뱾�� RTMP ��ͨ�� FFmpeg ���͵� SRT��
- **CameraSuite.ViewerHost**��WPF + ����̨�ۿ��ˣ�ʹ�� LibVLCSharp ������������ mediamtx��

## ����

- [.NET 8 SDK](https://dotnet.microsoft.com/)
- [LibVLC](https://www.videolan.org/vlc/) runtime��LibVLCSharp ����ʱ������
- [mediamtx](https://github.com/bluenviron/mediamtx) ���°汾
- [FFmpeg](https://ffmpeg.org/)
- [NATS Server](https://nats.io/)

## ����

`powershell
cd d:\Project\C#\camera
dotnet build CameraSuite.sln
`

## ����

ÿ�������ж����� ppsettings.json��

- src/Auth/CameraSuite.AuthService/appsettings.json
- src/Source/CameraSuite.SourceService/appsettings.json
- src/Viewer/CameraSuite.ViewerHost/appsettings.json

��ѡ������ NATS ��ַ��֤��·����FFmpeg/mediamtx ��ִ���ļ���¼��Ŀ¼�Ⱦ����������ṩ��

### TLS ��֤��

CameraSuite.Shared �ṩ���� TLS ��װ��֧����ǩ֤�������У�顣ʾ�� OpenSSL ���̣�

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

�� ca.crt �������ϵͳ�����θ�֤�飬�����������������ɵĿͻ���֤�顣

## ��������

1. **������֤����** (CameraSuite.AuthService)��
   - �Զ�������֤�롣
   - ��ʾ����ۿ��� IP/�˿ں�ȴ�Ӱ��Դ����

2. **�����ۿ���** (CameraSuite.ViewerHost)��
   - ����̨��ʾ��־��
   - WPF UI �ɲ鿴���� stream��mediamtx �Զ�ά�����ò�Ϊÿ�������������˿���¼��

3. **����Ӱ��Դ** (CameraSuite.SourceService)��
   - ������֤�˵�ַ���˿ڡ���֤����ͨ������
   - �ڱ��� RTMP ������ú�ͨ�� FFmpeg ����Ϊ SRT��

## ¼��

mediamtx ¼��Ĭ�����Ϊ 
ecordings/<channel>/%Y%m%d/%H%M%S.ts������ CameraSuite.ViewerHost/appsettings.json �е�����Ŀ¼�� segment ���ȡ�

## ��������

- �����Զ��������� UI �Զ�����
- �ḻ Viewer UI������/¼�ƿ��صȣ���
- ������־������ָ���ϱ���
