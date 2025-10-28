using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Serialization;

namespace CameraSuite.Shared.Transport;

/// <summary>
/// Helper that serializes and deserializes <see cref="ControlMessage"/> instances over a WebSocket connection.
/// </summary>
public sealed class WebSocketJsonChannel : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public WebSocketJsonChannel(WebSocket socket, JsonSerializerOptions? options = null)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _jsonOptions = options ?? JsonOptionsFactory.CreateDefault();
        _jsonOptions.TypeInfoResolver ??= MessagingJsonContext.Default;
    }

    public WebSocket State => _socket;

    public async Task SendAsync(ControlMessage message, CancellationToken cancellationToken)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), _jsonOptions);
            var segment = new ArraySegment<byte>(payload);
            await _socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<ControlMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            var writer = new ArrayBufferWriter<byte>();
            while (true)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = await _socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                writer.Write(segment.AsSpan(0, result.Count));

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            if (writer.WrittenCount == 0)
            {
                return null;
            }

            var span = writer.WrittenSpan;
            return (ControlMessage?)JsonSerializer.Deserialize(span, typeof(ControlMessage), _jsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WebSocketException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }
    }
}
