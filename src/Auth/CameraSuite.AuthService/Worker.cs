using System.Linq;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Messaging;
using CameraSuite.Shared.Models;
using Microsoft.Extensions.Options;

namespace CameraSuite.AuthService;

public sealed class Worker : BackgroundService
{
    private readonly INatsJsonMessenger _messenger;
    private readonly AuthState _state;
    private readonly ILogger<Worker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _authOptions;

    public Worker(
        INatsJsonMessenger messenger,
        AuthState state,
        ILogger<Worker> logger,
        IOptions<CameraSuiteOptions> options,
        TimeProvider timeProvider)
    {
        _messenger = messenger;
        _state = state;
        _logger = logger;
        _timeProvider = timeProvider;
        _authOptions = options.Value.Auth;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var viewerEndpoint = await _state.WaitForViewerEndpointAsync(stoppingToken);
        _logger.LogInformation("Viewer endpoint ready: {Viewer}", viewerEndpoint);

        var requestTask = HandleAuthRequestsAsync(viewerEndpoint, stoppingToken);
        var lifecycleTask = ObserveStreamLifecycleAsync(stoppingToken);
        var cleanupTask = RunCleanupLoopAsync(stoppingToken);

        await Task.WhenAll(requestTask, lifecycleTask, cleanupTask);
    }

    private async Task HandleAuthRequestsAsync(ViewerEndpoint viewerEndpoint, CancellationToken cancellationToken)
    {
        await foreach (var message in _messenger.SubscribeAsync<AuthRequest>(NatsSubjects.AuthRequests, cancellationToken))
        {
            if (message.Data is not AuthRequest request)
            {
                continue;
            }

            _logger.LogInformation("收到认证请求 Source={Source} Channel={Channel} ViewerHint={ViewerAddress}",
                request.SourceId,
                request.ChannelName,
                request.ViewerAddress);

            if (!_state.IsValidCode(request.AuthCode))
            {
                await _messenger.PublishAsync(
                    NatsSubjects.AuthResponses(request.SourceId),
                    new AuthResponse(
                        Accepted: false,
                        Message: "认证码无效",
                        StreamKey: string.Empty,
                        SrtHost: viewerEndpoint.Host,
                        SrtPort: 0,
                        AesKey: null,
                        AesIv: null,
                        IssuedAt: _timeProvider.GetUtcNow()),
                    cancellationToken);

                _logger.LogWarning("认证失败，认证码无效 Source={Source}", request.SourceId);
                continue;
            }

            if (!_state.TryReserveStream(request.SourceId, request.ChannelName, out var assignment, out var failureReason))
            {
                await _messenger.PublishAsync(
                    NatsSubjects.AuthResponses(request.SourceId),
                    new AuthResponse(
                        Accepted: false,
                        Message: failureReason ?? "SRT 端口资源不足",
                        StreamKey: string.Empty,
                        SrtHost: viewerEndpoint.Host,
                        SrtPort: 0,
                        AesKey: null,
                        AesIv: null,
                        IssuedAt: _timeProvider.GetUtcNow()),
                    cancellationToken);

                _logger.LogWarning("认证失败，端口不足 Source={Source} Channel={Channel}", request.SourceId, request.ChannelName);
                continue;
            }

            var response = new AuthResponse(
                Accepted: true,
                Message: "认证成功",
                StreamKey: assignment.StreamKey,
                SrtHost: viewerEndpoint.Host,
                SrtPort: assignment.Port,
                AesKey: assignment.KeyMaterial.Key.ToArray(),
                AesIv: assignment.KeyMaterial.Iv.ToArray(),
                IssuedAt: _timeProvider.GetUtcNow());

            await _messenger.PublishAsync(
                NatsSubjects.AuthResponses(request.SourceId),
                response,
                cancellationToken);

            await _messenger.PublishAsync(
                NatsSubjects.StreamsAnnounce,
                new StreamAnnouncement(
                    request.SourceId,
                    request.ChannelName,
                    $"{viewerEndpoint.DisplayName}:{request.ChannelName}",
                    assignment.StreamKey,
                    viewerEndpoint.Host,
                    assignment.Port,
                    assignment.KeyMaterial.Key.ToArray(),
                    assignment.KeyMaterial.Iv.ToArray(),
                    _timeProvider.GetUtcNow()),
                cancellationToken);

            _logger.LogInformation(
                "认证成功 Source={Source} Channel={Channel} 分配端口 {Port}",
                request.SourceId,
                request.ChannelName,
                assignment.Port);
        }
    }

    private async Task ObserveStreamLifecycleAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messenger.SubscribeAsync<StreamStateUpdate>(NatsSubjects.StreamsState, cancellationToken))
        {
            if (message.Data is not StreamStateUpdate update)
            {
                continue;
            }

            if (update.State is StreamLifecycle.Stopped or StreamLifecycle.Failed)
            {
                _state.ReleaseStream(update.SourceId, update.ChannelName);
                _logger.LogInformation("释放端口 Source={Source} Channel={Channel} 状态={State}", update.SourceId, update.ChannelName, update.State);
            }
        }
    }

    private async Task RunCleanupLoopAsync(CancellationToken cancellationToken)
    {
        var sweepInterval = TimeSpan.FromSeconds(Math.Max(10, _authOptions.CleanupSweepSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(sweepInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var expired = _state.CleanupExpired(_timeProvider.GetUtcNow());
            foreach (var assignment in expired)
            {
                _logger.LogWarning(
                    "释放超时端口 Source={Source} Channel={Channel} Port={Port}",
                    assignment.SourceId,
                    assignment.ChannelName,
                    assignment.Port);
            }
        }
    }
}
