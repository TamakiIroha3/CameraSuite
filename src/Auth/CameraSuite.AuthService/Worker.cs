using CameraSuite.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace CameraSuite.AuthService;

public sealed class Worker : BackgroundService
{
    private readonly AuthState _state;
    private readonly ILogger<Worker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly AuthOptions _authOptions;

    public Worker(
        AuthState state,
        ILogger<Worker> logger,
        IOptions<CameraSuiteOptions> options,
        TimeProvider timeProvider)
    {
        _state = state;
        _logger = logger;
        _timeProvider = timeProvider;
        _authOptions = options.Value.Auth;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweepInterval = TimeSpan.FromSeconds(Math.Max(10, _authOptions.CleanupSweepSeconds));
        _logger.LogInformation("Cleanup worker started; sweep interval {Interval}", sweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(sweepInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
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
