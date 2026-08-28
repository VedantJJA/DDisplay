using System.Text.Json;
using DDisplay.Core.Capture;
using DDisplay.Core.Encode;
using DDisplay.Core.Protocol;
using DDisplay.Core.Transport;
using DDisplay.VddControl;

namespace DDisplay.Core;

/// <summary>
/// Coordinates session communications, handshake, change-detection streaming,
/// cursor tracking, tile patching, and freshest-wins bounded media queueing.
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
    private Task? _screenshotLoopTask;

    // Single-slot freshest-wins pending frame holder
    private EncodedFrameEventArgs? _pendingMediaFrame;
    private int _isSendingMedia = 0;

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
            else if (e.MessageType == "request-screenshot")
            {
                await SendScreenshotAsync();
            }
            else if (e.MessageType == "start-stream")
            {
                var startMsg = JsonSerializer.Deserialize<StartStreamMessage>(e.RawJson, ControlChannelJson.Options);
                int w = startMsg?.ScreenWidthPx ?? ActiveWidth;
                int h = startMsg?.ScreenHeightPx ?? ActiveHeight;
                await StartLiveStreamAsync(w, h, isRemoteInitiated: true);
            }
            else if (e.MessageType == "stop-stream")
            {
                await StopLiveStreamAsync(isRemoteInitiated: true);
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
                await StopLiveStreamAsync(isRemoteInitiated: true);
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

    public async Task SendScreenshotAsync()
    {
        try
        {
            var bounds = GdiScreenshotCapture.GetVirtualOrSecondaryDisplayBounds();
            var jpegBytes = GdiScreenshotCapture.CaptureDesktopJpeg(quality: 70, preferVirtualDisplay: true);
            var base64 = Convert.ToBase64String(jpegBytes);
            var msg = new ScreenshotMessage
            {
                ImageBase64 = base64,
                Width = bounds.Width > 0 ? bounds.Width : 1920,
                Height = bounds.Height > 0 ? bounds.Height : 1080,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            await _transport.SendControlMessageAsync(msg);
        }
        catch { }
    }

    public async Task StartLiveStreamAsync(int width, int height, bool isRemoteInitiated = false)
    {
        int targetWidth = Math.Max(width, height);
        int targetHeight = Math.Min(width, height);

        targetWidth = (targetWidth > 0 ? targetWidth : 1920) & ~1;
        targetHeight = (targetHeight > 0 ? targetHeight : 1080) & ~1;

        ActiveWidth = targetWidth;
        ActiveHeight = targetHeight;

        _sessionCts?.Cancel();
        _sessionCts = new CancellationTokenSource();
        var token = _sessionCts.Token;

        // 1. Enable VDD display cleanly
        try
        {
            await _vddService.EnableDisplayAsync(token);
        }
        catch { }

        _isStreaming = true;
        StreamingStateChanged?.Invoke(this, true);

        // If initiated on PC, notify Android to launch player
        if (!isRemoteInitiated)
        {
            try
            {
                var startCmd = new StartStreamMessage
                {
                    ScreenWidthPx = targetWidth,
                    ScreenHeightPx = targetHeight,
                };
                await _transport.SendControlMessageAsync(startCmd, token);
            }
            catch { }
        }

        // Allow Windows display manager to attach the second monitor
        await Task.Delay(600, token);

        // 2. Send initial full frame
        await SendScreenshotAsync();

        // 3. Start Change-Detection Capture Loop
        try
        {
            await _captureEngine.InitializeAsync(string.Empty, token);
            await _encoder.InitializeAsync(targetWidth, targetHeight, 8000, 60, "video/avc", token);

            _captureEngine.FrameAvailable += OnFrameCaptured;
            _encoder.FrameEncoded += OnFrameEncoded;

            _ = Task.Run(() => _captureEngine.StartCaptureAsync(token), token);
        }
        catch { }

        // 4. Background screenshot stream loop for low-overhead continuous frame refresh
        _screenshotLoopTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _isStreaming)
            {
                await SendScreenshotAsync();
                await Task.Delay(33, token);
            }
        }, token);
    }

    private async void OnFrameCaptured(object? sender, CaptureFrameEventArgs e)
    {
        if (!_isStreaming) return;

        try
        {
            // Path A: CURSOR_ONLY update
            if (e.Classification == FrameClassification.CursorOnly && e.Cursor != null)
            {
                var cursorMsg = new CursorUpdateMessage
                {
                    X = e.Cursor.X,
                    Y = e.Cursor.Y,
                    Visible = e.Cursor.Visible,
                    ShapeBase64 = e.Cursor.ShapeBase64,
                    ShapeWidth = e.Cursor.ShapeWidth,
                    ShapeHeight = e.Cursor.ShapeHeight,
                    HotspotX = e.Cursor.HotspotX,
                    HotspotY = e.Cursor.HotspotY,
                };
                await _transport.SendControlMessageAsync(cursorMsg);
                return;
            }

            // Path B: PATCH update (send small dirty tiles)
            if (e.Classification == FrameClassification.Patch && e.Patches != null && e.Patches.Count > 0)
            {
                foreach (var patch in e.Patches)
                {
                    var patchMsg = new TilePatchMessage
                    {
                        TileX = patch.X,
                        TileY = patch.Y,
                        TileWidth = patch.Width,
                        TileHeight = patch.Height,
                        ImageBase64 = patch.ImageBase64,
                        TimestampMs = e.TimestampMs,
                    };
                    await _transport.SendControlMessageAsync(patchMsg);
                }

                if (e.Cursor != null)
                {
                    await _transport.SendControlMessageAsync(new CursorUpdateMessage
                    {
                        X = e.Cursor.X,
                        Y = e.Cursor.Y,
                        Visible = e.Cursor.Visible,
                    });
                }
                return;
            }

            // Path C: FULL frame video encode
            if (e.Classification == FrameClassification.Full && e.BgraData.Length > 0)
            {
                await _encoder.EncodeFrameAsync(e.BgraData, e.TimestampMs);
            }
        }
        catch { }
    }

    private void OnFrameEncoded(object? sender, EncodedFrameEventArgs e)
    {
        if (!_isStreaming || e.NalData.Length == 0) return;

        // Freshest-wins: store the latest frame in the single-slot buffer
        Interlocked.Exchange(ref _pendingMediaFrame, e);

        // Trigger asynchronous send loop if not already in progress
        if (Interlocked.CompareExchange(ref _isSendingMedia, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (_isStreaming)
                    {
                        // Atomically take the freshest pending frame (dropping any stale intermediate ones)
                        var frameToSend = Interlocked.Exchange(ref _pendingMediaFrame, null);
                        if (frameToSend == null) break;

                        await _transport.SendMediaFrameAsync(frameToSend.NalData, frameToSend.IsKeyframe, frameToSend.TimestampMs);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isSendingMedia, 0);

                    // If a newer frame arrived right at loop exit, trigger send once more
                    if (_pendingMediaFrame != null && Interlocked.CompareExchange(ref _isSendingMedia, 1, 0) == 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            var lastFrame = Interlocked.Exchange(ref _pendingMediaFrame, null);
                            if (lastFrame != null && _isStreaming)
                            {
                                try
                                {
                                    await _transport.SendMediaFrameAsync(lastFrame.NalData, lastFrame.IsKeyframe, lastFrame.TimestampMs);
                                }
                                catch { }
                            }
                            Interlocked.Exchange(ref _isSendingMedia, 0);
                        });
                    }
                }
            });
        }
    }

    public async Task StopLiveStreamAsync(bool isRemoteInitiated = false)
    {
        _isStreaming = false;
        StreamingStateChanged?.Invoke(this, false);

        _sessionCts?.Cancel();
        _captureEngine.FrameAvailable -= OnFrameCaptured;
        _encoder.FrameEncoded -= OnFrameEncoded;

        if (!isRemoteInitiated)
        {
            try
            {
                await _transport.SendControlMessageAsync(new StopStreamMessage());
            }
            catch { }
        }

        try { await _captureEngine.StopCaptureAsync(); } catch { }
        try { await _encoder.DisposeAsync(); } catch { }
        try { await _captureEngine.DisposeAsync(); } catch { }
        try { await _vddService.DisableDisplayAsync(); } catch { }
    }

    private async void OnTransportDisconnected(object? sender, TransportDisconnectedEventArgs e)
    {
        await StopLiveStreamAsync(isRemoteInitiated: true);
    }

    public async ValueTask DisposeAsync()
    {
        await StopLiveStreamAsync(isRemoteInitiated: true);
        _transport.ControlMessageReceived -= OnControlMessageReceived;
        _transport.Disconnected -= OnTransportDisconnected;
        _sessionCts?.Dispose();
    }
}
