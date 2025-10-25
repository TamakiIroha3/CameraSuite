using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using System.Threading.Tasks;

namespace CameraSuite.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCameraSuiteCore(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<CameraSuiteOptions>()
            .Bind(configuration.GetSection(CameraSuiteOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CameraSuiteOptions>>().Value;
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return NatsConnectionFactory.CreateConnection(options.Nats, loggerFactory);
        });

        services.AddSingleton<INatsJsonMessenger>(sp =>
        {
            var connection = sp.GetRequiredService<NatsConnection>();
            return new NatsJsonMessenger(connection);
        });

        services.AddHostedService<NatsConnectionInitializer>();

        return services;
    }
}

internal sealed class NatsConnectionInitializer : IHostedService
{
    private readonly NatsConnection _connection;
    private readonly ILogger<NatsConnectionInitializer> _logger;

    public NatsConnectionInitializer(NatsConnection connection, ILogger<NatsConnectionInitializer> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connectTask = _connection.ConnectAsync();
            if (!connectTask.IsCompletedSuccessfully)
            {
                await connectTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                connectTask.GetAwaiter().GetResult();
            }

            _logger.LogInformation("Connected to NATS at {Url}", _connection.Opts.Url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to NATS at {Url}", _connection.Opts.Url);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _logger.LogInformation("Disconnected from NATS");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while disposing NATS connection");
        }
    }
}
