using System.Net.WebSockets;
using System.Text.Json;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraSuite.ViewerHost;

public sealed class ViewerControlPlaneClient : BackgroundService
{
    private readonly ViewerState _state;
    private readonly PlaybackService _playback;
    private readonly MediamtxManager _mediamtx;
    private readonly ViewerOptions _viewerOptions;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<ViewerControlPlaneClient> _logger;

    public ViewerControlPlaneClient(
        ViewerState state,
        PlaybackService playback,
        MediamtxManager mediamtx,
        IOptions<CameraSuiteOptions> options,
        JsonSerializerOptions jsonOptions,
        ILogger<ViewerControlPlaneClient> logger)
    {
        _state = state;
        _playback = playback;
        _mediamtx = mediamtx;
        _viewerOptions = options.Value.Viewer;
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "控制平面连接失败，5 秒后重试");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_viewerOptions.ControlPlaneUri);
        using var socket = new ClientWebSocket();

        if (string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase) && _viewerOptions.TrustAllCertificates)
        {
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _logger.LogInformation("连接认证端控制平面: {Uri}", uri);
        await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

        var channel = new WebSocketJsonChannel(socket, _jsonOptions);
        try
        {
            var hello = new ViewerHello(
                ViewerId: _viewerOptions.ViewerId,
                Host: Environment.MachineName,
                MediamtxApiPort: _viewerOptions.MediamtxApiPort,
                DisplayName: _viewerOptions.ViewerId,
                UseTls: string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase),
                TrustAllCertificates: _viewerOptions.TrustAllCertificates,
                Slots: _mediamtx.GetSlotSnapshot());

            await channel.SendAsync(hello, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("已发送 viewer_hello 握手");

            var ack = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (ack is not ViewerHelloAck { Accepted: true } helloAck)
            {
                _logger.LogWarning("认证端未接受 viewer_hello 握手");
                return;
            }

            _logger.LogInformation("认证端确认连接，当前认证码 {Code}", helloAck.AuthCode);

            await ListenAsync(channel, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            _logger.LogInformation("控制平面连接断开");
        }
    }

    private async Task ListenAsync(WebSocketJsonChannel channel, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ControlMessage? message;
            try
            {
                message = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (message is null)
            {
                break;
            }

            switch (message)
            {
                case StreamAnnouncement announcement:
                    await HandleAnnouncementAsync(announcement, cancellationToken).ConfigureAwait(false);
                    break;
                case StreamStateUpdate update:
                    HandleStateUpdate(update);
                    break;
                case ErrorNotification error:
                    _logger.LogWarning("认证端提示: {Message} {Detail}", error.Message, error.Detail);
                    break;
                default:
                    _logger.LogDebug("忽略未知控制消息 {Type}", message.GetType().Name);
                    break;
            }
        }
    }

    private async Task HandleAnnouncementAsync(StreamAnnouncement announcement, CancellationToken cancellationToken)
    {
        _logger.LogInformation("收到流公告: {Channel} ({Source})", announcement.ChannelName, announcement.SourceId);

        var viewModel = _state.UpsertStream(announcement, () => new StreamViewModel());
        _playback.CreateOrUpdate(announcement, viewModel);
        await _mediamtx.RegisterStreamAsync(viewModel, cancellationToken).ConfigureAwait(false);
    }

    private void HandleStateUpdate(StreamStateUpdate update)
    {
        _logger.LogDebug("Stream 状态更新 {Channel}: {State}", update.ChannelName, update.State);
        _state.UpdateState(update);
    }
}
