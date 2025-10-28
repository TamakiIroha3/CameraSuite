using System.Net.WebSockets;
using System.Text.Json;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Security;
using CameraSuite.Shared.Transport;
using Microsoft.Extensions.Options;

namespace CameraSuite.SourceService;

public sealed class Worker : BackgroundService
{
    private readonly SourceState _state;
    private readonly FfmpegProcessManager _ffmpeg;
    private readonly ILogger<Worker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SourceOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private WebSocketJsonChannel? _controlChannel;

    public Worker(
        SourceState state,
        FfmpegProcessManager ffmpeg,
        ILogger<Worker> logger,
        IOptions<CameraSuiteOptions> options,
        TimeProvider timeProvider,
        JsonSerializerOptions jsonOptions)
    {
        _state = state;
        _ffmpeg = ffmpeg;
        _logger = logger;
        _timeProvider = timeProvider;
        _options = options.Value.Source;
        _jsonOptions = jsonOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registration = await _state.WaitForRegistrationAsync(stoppingToken);
        _logger.LogInformation("连接认证端 {Host}:{Port}", registration.AuthHost, registration.AuthPort);

        (WebSocketJsonChannel Channel, AuthResponse Response)? session = null;
        try
        {
            session = await EstablishSessionAsync(registration, stoppingToken).ConfigureAwait(false);
            if (session is null)
            {
                _logger.LogError("无法与认证端建立 WebSocket 会话");
                return;
            }

            var (channel, authResponse) = session.Value;
            _controlChannel = channel;

            if (!authResponse.Accepted)
            {
                _logger.LogError("认证失败: {Message}", authResponse.Message);
                return;
            }

            if (authResponse.AesKey is null || authResponse.AesIv is null)
            {
                _logger.LogError("认证响应缺少 AES 密钥");
                return;
            }

            var streamSession = new StreamSession(
                registration.ChannelName,
                authResponse.StreamKey,
                authResponse.SrtHost,
                authResponse.SrtPort,
                authResponse.AesKey,
                authResponse.AesIv,
                _options.LocalRtmpUrl,
                _options.FfmpegExecutable);

            _state.SetSession(streamSession);

            var controlTask = ListenControlPlaneAsync(channel, stoppingToken);

            try
            {
                await RunStreamingLoopAsync(streamSession, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                await PublishStateAsync(streamSession.ChannelName, StreamLifecycle.Stopped, null, CancellationToken.None)
                    .ConfigureAwait(false);
                await controlTask.ConfigureAwait(false);
            }
        }
        finally
        {
            if (session?.Channel is { } control)
            {
                await control.DisposeAsync().ConfigureAwait(false);
            }

            await _ffmpeg.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<(WebSocketJsonChannel Channel, AuthResponse Response)?> EstablishSessionAsync(
        SourceRegistration registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildControlPlaneUri(registration);
            var socket = new ClientWebSocket();

            if ((_options.UseTls || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)) &&
                _options.TrustAllCertificates)
            {
                socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            var channel = new WebSocketJsonChannel(socket, _jsonOptions);

            var request = new AuthRequest(
                SourceId: _state.SourceId,
                ChannelName: registration.ChannelName,
                AuthCode: registration.AuthCode,
                ViewerAddress: registration.ViewerAddress ?? $"{registration.AuthHost}:{registration.AuthPort}");

            await channel.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("已向认证端发送认证请求");

            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                switch (message)
                {
                    case AuthResponse authResponse:
                        _logger.LogInformation("收到认证响应: {Status}", authResponse.Accepted ? "已通过" : "被拒绝");
                        return (channel, authResponse);
                    case ErrorNotification error:
                        _logger.LogWarning("控制通道错误: {Message} {Detail}", error.Message, error.Detail);
                        break;
                    case null:
                        _logger.LogWarning("控制通道被远端关闭");
                        return null;
                    default:
                        _logger.LogDebug("收到未处理的控制消息 {Type}", message.GetType().Name);
                        break;
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接认证端时发生异常");
            return null;
        }
    }

    private Uri BuildControlPlaneUri(SourceRegistration registration)
    {
        var scheme = _options.UseTls ? "wss" : "ws";
        var path = _options.ControlPlanePath?.TrimStart('/') ?? "ws/source";

        return new UriBuilder
        {
            Scheme = scheme,
            Host = registration.AuthHost,
            Port = registration.AuthPort,
            Path = path,
        }.Uri;
    }

    private async Task RunStreamingLoopAsync(StreamSession session, CancellationToken cancellationToken)
    {
        var passphrase = AesKeyMaterial.DerivePassphrase(session.AesKey);
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            await PublishStateAsync(session.ChannelName, StreamLifecycle.Starting, null, cancellationToken).ConfigureAwait(false);

            try
            {
                var exitCode = await _ffmpeg.RunOnceAsync(session, passphrase, async () =>
                    {
                        await PublishStateAsync(session.ChannelName, StreamLifecycle.Ready, null, cancellationToken)
                            .ConfigureAwait(false);
                    }, cancellationToken)
                    .ConfigureAwait(false);

                if (exitCode == 0)
                {
                    _logger.LogInformation("推流正常结束");
                    break;
                }

                var message = $"FFmpeg exit code {exitCode}";
                _logger.LogWarning("推流失败: {Message}", message);
                await PublishStateAsync(session.ChannelName, StreamLifecycle.Failed, message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("推流任务取消");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推流过程中出现异常");
                await PublishStateAsync(session.ChannelName, StreamLifecycle.Failed, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
            }

            var delaySeconds = Math.Min(_options.RetryDelaySeconds * attempt, _options.RetryDelaySeconds * 6);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishStateAsync(string channel, StreamLifecycle state, string? error, CancellationToken cancellationToken)
    {
        if (_controlChannel is null)
        {
            return;
        }

        var update = new StreamStateUpdate(
            SourceId: _state.SourceId,
            ChannelName: channel,
            State: state,
            RecordingPath: null,
            ErrorMessage: error,
            Timestamp: _timeProvider.GetUtcNow());

        await _controlChannel.SendAsync(update, cancellationToken).ConfigureAwait(false);
    }

    private async Task ListenControlPlaneAsync(WebSocketJsonChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message is ErrorNotification error)
                {
                    _logger.LogWarning("认证端提示: {Message} {Detail}", error.Message, error.Detail);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "控制通道监听结束");
        }
    }
}
