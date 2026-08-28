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
    private long _expectedSeq = 1;
    private long _packetLossCount;

    public bool IsStreaming => _isStreaming;
    public int ActiveWidth { get; private set; }
    public int ActiveHeight { get; private set; }
    public long PacketsReceived => _packetsReceived;
    public long BytesTransferred => _bytesTransferred;
    public long LastRttMs => _lastRttMs;
    public long PacketLossCount => _packetLossCount;

    public event EventHandler<bool>? StreamingStateChanged;
    public event EventHandler<(long Packets, long Bytes, long RttMs, long PacketLoss)>? TestDataProgress;

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
            else if (e.MessageType == "start-stream")
            {
                var startMsg = JsonSerializer.Deserialize<StartStreamMessage>(e.RawJson, ControlChannelJson.Options);
                int w = startMsg?.ScreenWidthPx ?? ActiveWidth;
                int h = startMsg?.ScreenHeightPx ?? ActiveHeight;
                await StartLiveStreamAsync(w, h);
            }
            else if (e.MessageType == "stop-stream")
            {
                await StopLiveStreamAsync();
            }
            else if (e.MessageType == "test-data")
            {
                var testMsg = JsonSerializer.Deserialize<TestDataMessage>(e.RawJson, ControlChannelJson.Options);
                if (testMsg != null)
                {
                    _packetsReceived++;
                    _bytesTransferred += (testMsg.Payload?.Length ?? 0);

                    if (testMsg.Sequence > _expectedSeq)
                    {
                        _packetLossCount += (testMsg.Sequence - _expectedSeq);
                    }
                    _expectedSeq = testMsg.Sequence + 1;

                    // Echo back test-data-ack
                    var ack = new TestDataAckMessage
                    {
                        Sequence = testMsg.Sequence,
                        EchoTimestampMs = testMsg.TimestampMs,
                        BytesReceived = _bytesTransferred,
                    };
                    await _transport.SendControlMessageAsync(ack);

                    TestDataProgress?.Invoke(this, (_packetsReceived, _bytesTransferred, _lastRttMs, _packetLossCount));
                }
            }
            else if (e.MessageType == "test-data-ack")
            {
                var testAck = JsonSerializer.Deserialize<TestDataAckMessage>(e.RawJson, ControlChannelJson.Options);
                if (testAck != null && testAck.EchoTimestampMs > 0)
                {
                    _lastRttMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - testAck.EchoTimestampMs;
                    TestDataProgress?.Invoke(this, (_packetsReceived, _bytesTransferred, _lastRttMs, _packetLossCount));
                }
            }
            else if (e.MessageType == "bye")
            {
                await StopLiveStreamAsync();
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
    }

    public async Task StartLiveStreamAsync(int width, int height)
    {
        int targetWidth = Math.Max(width, height);
        int targetHeight = Math.Min(width, height);

        targetWidth = (targetWidth > 0 ? targetWidth : 1920) & ~1;
        targetHeight = (targetHeight > 0 ? targetHeight : 1080) & ~1;

        ActiveWidth = targetWidth;
        ActiveHeight = targetHeight;

        // 1. Configure VDD monitor
        try
        {
            await _vddService.AddOrUpdateMonitorAsync(new MonitorEntry
            {
                Index = 0,
                WidthPx = targetWidth,
                HeightPx = targetHeight,
                RefreshRateHz = 60,
                FriendlyName = "DDisplay Virtual Monitor",
                Enabled = true,
            });
            await _vddService.EnableDisplayAsync();
        }
        catch { }

        // Allow Windows display manager to register virtual monitor
        await Task.Delay(1000);

        // 2. Start Capture & Encoding Pipeline
        _sessionCts?.Cancel();
        _sessionCts = new CancellationTokenSource();
        var token = _sessionCts.Token;

        try
        {
            await _captureEngine.InitializeAsync(string.Empty, token);
            await _encoder.InitializeAsync(targetWidth, targetHeight, 8000, 60, "video/avc", token);

            _captureEngine.FrameAvailable += OnFrameCaptured;
            _encoder.FrameEncoded += OnFrameEncoded;

            _isStreaming = true;
            StreamingStateChanged?.Invoke(this, true);

            _ = Task.Run(() => _captureEngine.StartCaptureAsync(token), token);

            // Send HelloAck confirmation
            var ack = new HelloAckMessage
            {
                VirtualDisplayWidthPx = targetWidth,
                VirtualDisplayHeightPx = targetHeight,
                RefreshRateHz = 60,
                Codec = "video/avc",
                BitrateKbps = 8000,
            };
            await _transport.SendControlMessageAsync(ack, token);
        }
        catch
        {
            _isStreaming = false;
            StreamingStateChanged?.Invoke(this, false);
        }
    }

    private async void OnFrameCaptured(object? sender, CaptureFrameEventArgs e)
    {
        if (!_isStreaming) return;
        try
        {
            await _encoder.EncodeFrameAsync(e.BgraData, e.TimestampMs);
        }
        catch { }
    }

    private async void OnFrameEncoded(object? sender, EncodedFrameEventArgs e)
    {
        if (!_isStreaming || e.NalData.Length == 0) return;
        try
        {
            await _transport.SendMediaFrameAsync(e.NalData, e.IsKeyframe, e.TimestampMs);
        }
        catch { }
    }

    public async Task StopLiveStreamAsync()
    {
        _isStreaming = false;
        StreamingStateChanged?.Invoke(this, false);

        _sessionCts?.Cancel();
        _captureEngine.FrameAvailable -= OnFrameCaptured;
        _encoder.FrameEncoded -= OnFrameEncoded;

        try { await _captureEngine.StopCaptureAsync(); } catch { }
        try { await _encoder.DisposeAsync(); } catch { }
        try { await _captureEngine.DisposeAsync(); } catch { }
        try { await _vddService.DisableDisplayAsync(); } catch { }
    }

    private async void OnTransportDisconnected(object? sender, TransportDisconnectedEventArgs e)
    {
        await StopLiveStreamAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopLiveStreamAsync();
        _transport.ControlMessageReceived -= OnControlMessageReceived;
        _transport.Disconnected -= OnTransportDisconnected;
        _sessionCts?.Dispose();
    }
}
