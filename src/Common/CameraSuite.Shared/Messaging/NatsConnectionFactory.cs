using CameraSuite.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;
using System.Threading.Tasks;

namespace CameraSuite.Shared.Messaging;

public static class NatsConnectionFactory
{
    public static NatsConnection CreateConnection(
        NatsOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var connectionOptions = NatsOpts.Default with
        {
            Url = options.Url,
            Name = options.ClientName,
            RequestTimeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)),
            TlsOpts = BuildTlsOptions(options),
            LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance,
            SerializerRegistry = NatsJsonSerializerRegistry.Default,
            RetryOnInitialConnect = true,
        };

        return new NatsConnection(connectionOptions);
    }

    public static async ValueTask<NatsConnection> CreateAndConnectAsync(
        NatsOptions options,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(options, loggerFactory);
        var connectTask = connection.ConnectAsync();
        if (!connectTask.IsCompletedSuccessfully)
        {
            await connectTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            connectTask.GetAwaiter().GetResult();
        }

        return connection;
    }

    private static NatsTlsOpts BuildTlsOptions(NatsOptions options)
    {
        var tls = NatsTlsOpts.Default with
        {
            InsecureSkipVerify = options.TrustAllCertificates,
            CaFile = string.IsNullOrWhiteSpace(options.CaCertificatePath) ? null : options.CaCertificatePath,
        };

#if NETSTANDARD
        if (!string.IsNullOrWhiteSpace(options.ClientCertificatePath))
        {
            tls = tls with
            {
                CertBundleFile = options.ClientCertificatePath,
                CertBundleFilePassword = options.ClientKeyPassword,
            };
        }
#else
        var hasPemPair = !string.IsNullOrWhiteSpace(options.ClientCertificatePath)
            && !string.IsNullOrWhiteSpace(options.ClientKeyPath);

        if (hasPemPair)
        {
            tls = tls with
            {
                CertFile = options.ClientCertificatePath,
                KeyFile = options.ClientKeyPath,
                KeyFilePassword = options.ClientKeyPassword,
            };
        }
        else if (!string.IsNullOrWhiteSpace(options.ClientCertificatePath))
        {
            tls = tls with
            {
                CertBundleFile = options.ClientCertificatePath,
                CertBundleFilePassword = options.ClientKeyPassword,
            };
        }
#endif

        return tls;
    }
}
