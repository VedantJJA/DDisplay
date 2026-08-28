using System.Text.Json;
using DDisplay.Core.Capture;
using DDisplay.Core.Encode;
using DDisplay.Core.Protocol;
using DDisplay.Core.Transport;
using DDisplay.VddControl;
using DDisplay.VddControl.Models;

namespace DDisplay.Core;

/// <summary>
/// Coordinates session communications, handshake, test data transfer, and stream pipeline.
/// </summary>
public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly IVirtualDisplayService _vddService;
    private readonly ICaptureEngine _captureEngine;
    private readonly IEncoder _encoder;

    private CancellationTokenSource? _sessionCts;
    private bool _isStreaming;
    private long _packetsReceived;
    private long _bytesTransferred;
    private long _lastRttMs;

    public bool IsStreaming => _isStreaming;
    public int ActiveWidth { get; private set; }
    public int ActiveHeight { get; private set; }
    public long PacketsReceived => _packetsReceived;
    public long BytesTransferred => _bytesTransferred;
    public long LastRttMs => _lastRttMs;

    public event EventHandler<bool>? StreamingStateChanged;
    public event EventHandler<(long Packets, long Bytes, long RttMs)>? TestDataProgress;

    public SessionCoordinator(
        ITransport transport,
        IVirtualDisplayService vddService,
        ICaptureEngine? captureEngine = null,
        IEncoder? encoder = null)
    {
        _transport = transport;
        _vddService = vddService;
        _captureEngine = captureEngine ?? new DxgiCaptureEngine();
        _encoder = encoder ?? new MediaFoundationEncoder();
        _sessionCts = new CancellationTokenSource();

        _transport.ControlMessageReceived += OnControlMessageReceived;
        _transport.Disconnected += OnTransportDisconnected;
    }

    private async void OnControlMessageReceived(object? sender, ControlMessageReceivedEventArgs e)
    {
        try
        {
            if (e.MessageType == "hello")
            {
                var hello = JsonSerializer.Deserialize<HelloMessage>(e.RawJson, ControlChannelJson.Options);
                if (hello != null)
                {
                    await HandleHelloAsync(hello);
                }
            }
            else if (e.MessageType == "test-data")
            {
                var testMsg = JsonSerializer.Deserialize<TestDataMessage>(e.RawJson, ControlChannelJson.Options);
                if (testMsg != null)
                {
                    _packetsReceived++;
                    _bytesTransferred += (testMsg.Payload?.Length ?? 0);

                    // Echo back test-data-ack
                    var ack = new TestDataAckMessage
                    {
                        Sequence = testMsg.Sequence,
                        EchoTimestampMs = testMsg.TimestampMs,
                        BytesReceived = _bytesTransferred,
                    };
                    await _transport.SendControlMessageAsync(ack);

                    TestDataProgress?.Invoke(this, (_packetsReceived, _bytesTransferred, _lastRttMs));
                }
            }
            else if (e.MessageType == "test-data-ack")
            {
                var testAck = JsonSerializer.Deserialize<TestDataAckMessage>(e.RawJson, ControlChannelJson.Options);
                if (testAck != null && testAck.EchoTimestampMs > 0)
                {
                    _lastRttMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - testAck.EchoTimestampMs;
                    TestDataProgress?.Invoke(this, (_packetsReceived, _bytesTransferred, _lastRttMs));
                }
            }
            else if (e.MessageType == "bye")
            {
                await StopStreamingAsync();
            }
        }
        catch (Exception)
        {
            // Log/ignore protocol parse errors
        }
    }

    private async Task HandleHelloAsync(HelloMessage hello)
    {
        int targetWidth = Math.Max(hello.ScreenWidthPx, hello.ScreenHeightPx);
        int targetHeight = Math.Min(hello.ScreenWidthPx, hello.ScreenHeightPx);

        targetWidth = (targetWidth > 0 ? targetWidth : 1920) & ~1;
        targetHeight = (targetHeight > 0 ? targetHeight : 1080) & ~1;

        ActiveWidth = targetWidth;
        ActiveHeight = targetHeight;

        // Respond with HelloAck
        var ack = new HelloAckMessage
        {
            VirtualDisplayWidthPx = targetWidth,
            VirtualDisplayHeightPx = targetHeight,
            RefreshRateHz = 60,
            Codec = "video/avc",
            BitrateKbps = 8000,
        };
        await _transport.SendControlMessageAsync(ack);

        // Notify that session is active with test data
        _isStreaming = true;
        StreamingStateChanged?.Invoke(this, true);
    }

    public async Task StopStreamingAsync()
    {
        _isStreaming = false;
        StreamingStateChanged?.Invoke(this, false);
        _sessionCts?.Cancel();
    }

    private async void OnTransportDisconnected(object? sender, TransportDisconnectedEventArgs e)
    {
        await StopStreamingAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopStreamingAsync();
        _transport.ControlMessageReceived -= OnControlMessageReceived;
        _transport.Disconnected -= OnTransportDisconnected;
        _sessionCts?.Dispose();
    }
}
