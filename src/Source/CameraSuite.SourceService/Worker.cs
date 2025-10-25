using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Messaging;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Security;
using Microsoft.Extensions.Options;

namespace CameraSuite.SourceService;

public sealed class Worker : BackgroundService
{
    private readonly INatsJsonMessenger _messenger;
    private readonly SourceState _state;
    private readonly FfmpegProcessManager _ffmpeg;
    private readonly ILogger<Worker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SourceOptions _options;

    public Worker(
        INatsJsonMessenger messenger,
        SourceState state,
        FfmpegProcessManager ffmpeg,
        ILogger<Worker> logger,
        IOptions<CameraSuiteOptions> options,
        TimeProvider timeProvider)
    {
        _messenger = messenger;
        _state = state;
        _ffmpeg = ffmpeg;
        _logger = logger;
        _timeProvider = timeProvider;
        _options = options.Value.Source;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registration = await _state.WaitForRegistrationAsync(stoppingToken);
        _logger.LogInformation("使用认证端 {Host}:{Port}", registration.AuthHost, registration.AuthPort);

        var authResponse = await RequestAuthorizationAsync(registration, stoppingToken);
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

        var session = new StreamSession(
            registration.ChannelName,
            authResponse.StreamKey,
            authResponse.SrtHost,
            authResponse.SrtPort,
            authResponse.AesKey,
            authResponse.AesIv,
            _options.LocalRtmpUrl,
            _options.FfmpegExecutable);

        _state.SetSession(session);

        try
        {
            await RunStreamingLoopAsync(session, stoppingToken);
        }
        finally
        {
            await PublishStateAsync(session.ChannelName, StreamLifecycle.Stopped, null, CancellationToken.None);
            await _ffmpeg.DisposeAsync();
        }
    }

    private async Task<AuthResponse> RequestAuthorizationAsync(SourceRegistration registration, CancellationToken cancellationToken)
    {
        var responseSubject = NatsSubjects.AuthResponses(_state.SourceId);
        await using var subscription = await _messenger.Connection.SubscribeCoreAsync<AuthResponse>(
            responseSubject,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var request = new AuthRequest(
            SourceId: _state.SourceId,
            ChannelName: registration.ChannelName,
            AuthCode: registration.AuthCode,
            ViewerAddress: $"{registration.AuthHost}:{registration.AuthPort}");

        await _messenger.PublishAsync(NatsSubjects.AuthRequests, request, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("已向认证端发送认证请求");

        await foreach (var message in subscription.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (message.Data is { } data)
            {
                _logger.LogInformation("收到认证响应: {Status}", data.Accepted ? "Accepted" : "Rejected");
                return data;
            }
        }

        throw new InvalidOperationException("认证响应流已结束");
    }

    private async Task RunStreamingLoopAsync(StreamSession session, CancellationToken cancellationToken)
    {
        var passphrase = AesKeyMaterial.DerivePassphrase(session.AesKey);
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            await PublishStateAsync(session.ChannelName, StreamLifecycle.Starting, null, cancellationToken);

            try
            {
                var exitCode = await _ffmpeg.RunOnceAsync(session, passphrase, async () =>
                {
                    await PublishStateAsync(session.ChannelName, StreamLifecycle.Ready, null, cancellationToken);
                }, cancellationToken).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    _logger.LogInformation("推流正常结束");
                    break;
                }

                var message = $"FFmpeg exit code {exitCode}";
                _logger.LogWarning("推流失败: {Message}", message);
                await PublishStateAsync(session.ChannelName, StreamLifecycle.Failed, message, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("推流任务取消");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推流过程中出现异常");
                await PublishStateAsync(session.ChannelName, StreamLifecycle.Failed, ex.Message, cancellationToken);
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
        var update = new StreamStateUpdate(
            SourceId: _state.SourceId,
            ChannelName: channel,
            State: state,
            RecordingPath: null,
            ErrorMessage: error,
            Timestamp: _timeProvider.GetUtcNow());

        await _messenger.PublishAsync(NatsSubjects.StreamsState, update, cancellationToken).ConfigureAwait(false);
    }
}
