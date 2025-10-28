using System.Net.WebSockets;
using System.Text.Json;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Transport;
using Microsoft.Extensions.Logging;

namespace CameraSuite.AuthService;

public sealed class SourceWebSocketEndpoint
{
    private readonly AuthState _state;
    private readonly ViewerConnectionManager _viewerConnections;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SourceWebSocketEndpoint> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public SourceWebSocketEndpoint(
        AuthState state,
        ViewerConnectionManager viewerConnections,
        TimeProvider timeProvider,
        ILogger<SourceWebSocketEndpoint> logger,
        JsonSerializerOptions jsonOptions)
    {
        _state = state;
        _viewerConnections = viewerConnections;
        _timeProvider = timeProvider;
        _logger = logger;
        _jsonOptions = jsonOptions;
    }

    public async Task HandleAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var channel = new WebSocketJsonChannel(webSocket, _jsonOptions);
        StreamAssignment? assignment = null;

        try
        {
            var viewerEndpoint = await _state.WaitForViewerEndpointAsync(cancellationToken).ConfigureAwait(false);
            var initialMessage = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (initialMessage is not AuthRequest request)
            {
                _logger.LogWarning("Source connection rejected: first message must be auth_request");
                await channel.SendAsync(
                    new ErrorNotification("auth_request_required", "First message must be auth_request"),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            _logger.LogInformation("Auth request from {SourceId} channel {Channel}", request.SourceId, request.ChannelName);

            if (!_state.IsValidCode(request.AuthCode))
            {
                await channel.SendAsync(
                    new AuthResponse(
                        Accepted: false,
                        Message: "认证码无效",
                        StreamKey: string.Empty,
                        SrtHost: viewerEndpoint.Host,
                        SrtPort: 0,
                        AesKey: null,
                        AesIv: null,
                        IssuedAt: _timeProvider.GetUtcNow()),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogWarning("Source {Source} failed authentication", request.SourceId);
                return;
            }

            if (!_state.TryReserveStream(request.SourceId, request.ChannelName, out var reserved, out var failure))
            {
                await channel.SendAsync(
                    new AuthResponse(
                        Accepted: false,
                        Message: failure ?? "No available streaming port",
                        StreamKey: string.Empty,
                        SrtHost: viewerEndpoint.Host,
                        SrtPort: 0,
                        AesKey: null,
                        AesIv: null,
                        IssuedAt: _timeProvider.GetUtcNow()),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogWarning("No port available for source {Source}", request.SourceId);
                return;
            }

            assignment = reserved;

            var response = new AuthResponse(
                Accepted: true,
                Message: "认证成功",
                StreamKey: reserved.StreamKey,
                SrtHost: viewerEndpoint.Host,
                SrtPort: reserved.Port,
                AesKey: reserved.KeyMaterial.Key.ToArray(),
                AesIv: reserved.KeyMaterial.Iv.ToArray(),
                IssuedAt: _timeProvider.GetUtcNow());

            await channel.SendAsync(response, cancellationToken).ConfigureAwait(false);

            await _viewerConnections.SendAsync(
                new StreamAnnouncement(
                    request.SourceId,
                    request.ChannelName,
                    $"{viewerEndpoint.DisplayName}:{request.ChannelName}",
                    reserved.StreamKey,
                    viewerEndpoint.Host,
                    reserved.Port,
                    reserved.KeyMaterial.Key.ToArray(),
                    reserved.KeyMaterial.Iv.ToArray(),
                    _timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);

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

                if (message is StreamStateUpdate update)
                {
                    await HandleStateUpdateAsync(update, cancellationToken).ConfigureAwait(false);
                    if (update.State is StreamLifecycle.Stopped or StreamLifecycle.Failed)
                    {
                        _state.ReleaseStream(update.SourceId, update.ChannelName);
                        assignment = null;
                        break;
                    }
                }
            }
        }
        finally
        {
            if (assignment is not null)
            {
                _state.ReleaseStream(assignment.SourceId, assignment.ChannelName);
            }

            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleStateUpdateAsync(StreamStateUpdate update, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Stream update from {Source}/{Channel}: {State}",
            update.SourceId,
            update.ChannelName,
            update.State);

        await _viewerConnections.SendAsync(update, cancellationToken).ConfigureAwait(false);
    }
}
