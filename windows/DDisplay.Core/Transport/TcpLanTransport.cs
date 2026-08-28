using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DDisplay.Core.Protocol;

namespace DDisplay.Core.Transport;

/// <summary>
/// Shared base implementation for TCP-based transports (Wi-Fi and USB tethering).
/// Handles framing, JSON control messages, and media frame writing.
/// Subclasses only need to implement the address/listener setup.
/// </summary>
public abstract class TcpLanTransport : ITransport
{
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private MediaFrameWriter? _frameWriter;
    private CancellationTokenSource? _readLoopCts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    protected abstract IPEndPoint GetListenEndPoint();

    public abstract string DisplayName { get; }

    public bool IsConnected => _client?.Connected ?? false;

    public event EventHandler<ControlMessageReceivedEventArgs>? ControlMessageReceived;
    public event EventHandler<TransportDisconnectedEventArgs>? Disconnected;
    public event EventHandler? Connected;

    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _listener = new TcpListener(GetListenEndPoint());
        _listener.Start();

        try
        {
            _client = await _listener.AcceptTcpClientAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _listener.Stop();
            throw;
        }

        _client.NoDelay = true;
        _stream = _client.GetStream();
        _frameWriter = new MediaFrameWriter(_stream);

        _readLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), _readLoopCts.Token);

        Connected?.Invoke(this, EventArgs.Empty);
    }

    public virtual async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _readLoopCts?.Cancel();
        _listener?.Stop();

        if (_stream is not null)
        {
            try
            {
                await SendControlMessageAsync(new ByeMessage { Reason = "user-disconnect" }, cancellationToken);
            }
            catch { /* best-effort */ }
        }

        _client?.Close();
        _stream?.Dispose();
        _client = null;
        _stream = null;
    }

    public async Task SendControlMessageAsync(ControlMessage message, CancellationToken cancellationToken = default)
    {
        if (_stream is null) throw new TransportException("Not connected.");

        var json = JsonSerializer.Serialize(message, message.GetType(), ControlChannelJson.Options);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Frame: [4-byte payload length][1-byte tag 0x01][JSON bytes]
        int payloadLength = jsonBytes.Length;
        var header = new byte[5];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, payloadLength);
        header[4] = FrameReader.ControlChannelTag;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(header, cancellationToken);
            await _stream.WriteAsync(jsonBytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendMediaFrameAsync(
        ReadOnlyMemory<byte> nalUnits,
        bool isKeyframe,
        long presentationTimestampMs,
        CancellationToken cancellationToken = default)
    {
        if (_frameWriter is null) throw new TransportException("Not connected.");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _frameWriter.WriteFrameAsync(nalUnits, isKeyframe, presentationTimestampMs, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        var reader = new FrameReader(_stream);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await reader.ReadFrameAsync(cancellationToken);
                if (frame is null)
                {
                    // Remote closed the connection.
                    RaiseDisconnected("Remote closed connection.", null);
                    return;
                }

                var (tag, payload) = frame.Value;
                if (tag == FrameReader.ControlChannelTag)
                {
                    var json = Encoding.UTF8.GetString(payload);
                    using var doc = JsonDocument.Parse(json);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? string.Empty;
                    ControlMessageReceived?.Invoke(this, new ControlMessageReceivedEventArgs
                    {
                        RawJson = json,
                        MessageType = type,
                    });
                }
                // Media frames from client -> host are not expected in v1 (touch goes via control channel).
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            RaiseDisconnected("Read loop error.", ex);
        }
    }

    private void RaiseDisconnected(string reason, Exception? ex)
    {
        Disconnected?.Invoke(this, new TransportDisconnectedEventArgs
        {
            Reason = reason,
            Exception = ex,
        });
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _readLoopCts?.Dispose();
        _writeLock.Dispose();
    }
}
