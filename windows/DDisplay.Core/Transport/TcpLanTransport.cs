using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DDisplay.Core.Protocol;

namespace DDisplay.Core.Transport;

/// <summary>
/// Shared base implementation for TCP-based transports (Wi-Fi and USB tethering).
/// Handles framing, JSON control messages, media frame writing, and message buffering.
/// </summary>
public abstract class TcpLanTransport : ITransport
{
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private MediaFrameWriter? _frameWriter;
    private CancellationTokenSource? _readLoopCts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<ControlMessageReceivedEventArgs> _unhandledMessageQueue = new();
    private EventHandler<ControlMessageReceivedEventArgs>? _controlMessageReceived;
    private bool _isListening;

    protected abstract IPEndPoint GetListenEndPoint();

    public abstract string DisplayName { get; }

    public bool IsConnected => _client?.Connected ?? false;
    public bool IsListening => _isListening;

    public event EventHandler<ControlMessageReceivedEventArgs>? ControlMessageReceived
    {
        add
        {
            _controlMessageReceived += value;
            // Drain any messages that arrived before subscription
            while (_unhandledMessageQueue.TryDequeue(out var queuedMsg))
            {
                value?.Invoke(this, queuedMsg);
            }
        }
        remove
        {
            _controlMessageReceived -= value;
        }
    }

    public event EventHandler<TransportDisconnectedEventArgs>? Disconnected;
    public event EventHandler? Connected;

    public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        StopListenerAndClient();

        _listener = new TcpListener(GetListenEndPoint());
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.ExclusiveAddressUse = false;
        _listener.Start();
        _isListening = true;

        try
        {
            _client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _isListening = false;
        }
        catch (OperationCanceledException)
        {
            _isListening = false;
            try { _listener.Stop(); } catch { }
            throw;
        }
        catch (Exception)
        {
            _isListening = false;
            try { _listener.Stop(); } catch { }
            throw;
        }

        _client.NoDelay = true;
        _stream = _client.GetStream();
        _frameWriter = new MediaFrameWriter(_stream);

        _readLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), _readLoopCts.Token);

        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void StopListenerAndClient()
    {
        _isListening = false;
        _readLoopCts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        try { _client?.Close(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _client = null;
        _stream = null;
    }

    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is not null)
        {
            try
            {
                var json = JsonSerializer.Serialize(new ByeMessage { Reason = "user-disconnect" }, typeof(ByeMessage), ControlChannelJson.Options);
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var header = new byte[5];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, jsonBytes.Length);
                header[4] = FrameReader.ControlChannelTag;
                _stream.Write(header, 0, 5);
                _stream.Write(jsonBytes, 0, jsonBytes.Length);
                _stream.Flush();
            }
            catch { /* best-effort */ }
        }

        StopListenerAndClient();
        return Task.CompletedTask;
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
            await _stream.FlushAsync(cancellationToken);
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
                    RaiseDisconnected("Remote closed connection.", null);
                    return;
                }

                var (tag, payload) = frame.Value;
                if (tag == FrameReader.ControlChannelTag)
                {
                    var json = Encoding.UTF8.GetString(payload);
                    using var doc = JsonDocument.Parse(json);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? string.Empty;

                    var args = new ControlMessageReceivedEventArgs
                    {
                        RawJson = json,
                        MessageType = type,
                    };

                    var handler = _controlMessageReceived;
                    if (handler != null)
                    {
                        handler.Invoke(this, args);
                    }
                    else
                    {
                        // Buffer message until a listener attaches
                        _unhandledMessageQueue.Enqueue(args);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
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
