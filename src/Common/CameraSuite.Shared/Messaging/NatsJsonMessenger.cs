using NATS.Client.Core;

namespace CameraSuite.Shared.Messaging;

public interface INatsJsonMessenger : IAsyncDisposable
{
    NatsConnection Connection { get; }

    ValueTask PublishAsync<T>(string subject, T payload, CancellationToken cancellationToken = default);

    IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, CancellationToken cancellationToken = default);

    ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(string subject, TRequest payload, CancellationToken cancellationToken = default);
}

public sealed class NatsJsonMessenger : INatsJsonMessenger
{
    private readonly bool _ownsConnection;

    public NatsJsonMessenger(NatsConnection connection, bool ownsConnection = false)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = ownsConnection;
    }

    public NatsConnection Connection { get; }

    public ValueTask PublishAsync<T>(string subject, T payload, CancellationToken cancellationToken = default)
        => Connection.PublishAsync(subject, payload, cancellationToken: cancellationToken);

    public IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, CancellationToken cancellationToken = default)
        => Connection.SubscribeAsync<T>(subject, cancellationToken: cancellationToken);

    public ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest payload,
        CancellationToken cancellationToken = default)
        => Connection.RequestAsync<TRequest, TReply>(subject, payload, cancellationToken: cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection)
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
