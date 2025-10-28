using System.Text.Json;
using CameraSuite.Shared.Configuration;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;

namespace CameraSuite.AuthService;

public sealed class ViewerConnectionManager
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly ILogger<ViewerConnectionManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private ViewerSession? _current;
    private readonly AuthState _state;

    public ViewerConnectionManager(
        ILogger<ViewerConnectionManager> logger,
        IOptions<CameraSuiteOptions> options,
        JsonSerializerOptions jsonOptions,
        AuthState state)
    {
        _logger = logger;
        _jsonOptions = jsonOptions;
        _state = state;
    }

    public bool IsConnected => _current is not null;

    public async Task RunSessionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var channel = new WebSocketJsonChannel(socket, _jsonOptions);
        ViewerSession? session = null;

        try
        {
            var helloMessage = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (helloMessage is not ViewerHello hello)
            {
                _logger.LogWarning("Viewer connection rejected: first message must be viewer_hello");
                await channel.SendAsync(
                    new ErrorNotification("viewer_hello_required", "First message must be viewer_hello"),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            session = new ViewerSession(channel, hello, _logger);
            _state.SetListenerSlots(hello.Slots);

            await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_current is not null)
                {
                    await _current.DisposeAsync().ConfigureAwait(false);
                }

                _current = session;
            }
            finally
            {
                _mutex.Release();
            }

            _logger.LogInformation("Viewer connected: {ViewerId} ({Host})", hello.ViewerId, hello.Host);

            await channel.SendAsync(
                new ViewerHelloAck(true, _state.AuthCode, null),
                cancellationToken).ConfigureAwait(false);

            await session.ListenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _mutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_current == session)
                {
                    _current = null;
                }
            }
            finally
            {
                _mutex.Release();
            }

            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("Viewer disconnected");
        }
    }

    public async Task SendAsync(ControlMessage message, CancellationToken cancellationToken)
    {
        ViewerSession? target;
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            target = _current;
        }
        finally
        {
            _mutex.Release();
        }

        if (target is null)
        {
            return;
        }

        try
        {
            await target.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send message to viewer");
        }
    }
}

internal sealed class ViewerSession : IAsyncDisposable
{
    private readonly WebSocketJsonChannel _channel;
    private readonly ILogger _logger;
    private readonly ViewerHello _hello;

    public ViewerSession(WebSocketJsonChannel channel, ViewerHello hello, ILogger logger)
    {
        _channel = channel;
        _hello = hello;
        _logger = logger;
    }

    public ViewerHello Metadata => _hello;

    public Task SendAsync(ControlMessage message, CancellationToken cancellationToken)
        => _channel.SendAsync(message, cancellationToken);

    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ControlMessage? message;
            try
            {
                message = await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
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
                _logger.LogDebug("Viewer reported {Channel} => {State}", update.ChannelName, update.State);
            }
        }
    }

    public ValueTask DisposeAsync() => _channel.DisposeAsync();
}
