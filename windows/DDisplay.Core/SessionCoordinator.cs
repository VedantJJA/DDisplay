using System.Text.Json;
using DDisplay.Core.Capture;
using DDisplay.Core.Encode;
using DDisplay.Core.Protocol;
using DDisplay.Core.Transport;
using DDisplay.VddControl;
using DDisplay.VddControl.Models;

namespace DDisplay.Core;

/// <summary>
/// Coordinates the active streaming session between the Android client and Windows host.
/// Manages:
///   1. Dynamic VDD virtual monitor creation matching the client's screen resolution.
///   2. DXGI desktop frame capture.
///   3. MediaFoundation H.264 video encoding.
///   4. Real-time NAL packet transmission over the active transport (USB or Wi-Fi).
/// </summary>
public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly IVirtualDisplayService _vddService;
    private readonly ICaptureEngine _captureEngine;
    private readonly IEncoder _encoder;

    private CancellationTokenSource? _sessionCts;
    private bool _isStreaming;

    public bool IsStreaming => _isStreaming;
    public int ActiveWidth { get; private set; }
    public int ActiveHeight { get; private set; }

    public event EventHandler<bool>? StreamingStateChanged;

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
        // Compute resolution matching device in landscape (or native orientation)
        int targetWidth = Math.Max(hello.ScreenWidthPx, hello.ScreenHeightPx);
        int targetHeight = Math.Min(hello.ScreenWidthPx, hello.ScreenHeightPx);

        // Ensure dimensions are even and within sane bounds
        targetWidth = (targetWidth > 0 ? targetWidth : 1920) & ~1;
        targetHeight = (targetHeight > 0 ? targetHeight : 1080) & ~1;

        ActiveWidth = targetWidth;
        ActiveHeight = targetHeight;

        // 1. Configure the Virtual Display Driver monitor matching the device resolution
        try
        {
            await _vddService.AddOrUpdateMonitorAsync(new MonitorEntry
            {
                Index = 0,
                WidthPx = targetWidth,
                HeightPx = targetHeight,
                RefreshRateHz = 60,
                FriendlyName = "DDisplay Virtual Display",
                Enabled = true,
            });
            await _vddService.EnableDisplayAsync();
        }
        catch
        {
            // Fallback if VDD xml update is non-fatal
        }

        // 2. Reply with HelloAckMessage
        var ack = new HelloAckMessage
        {
            VirtualDisplayWidthPx = targetWidth,
            VirtualDisplayHeightPx = targetHeight,
            RefreshRateHz = 60,
            Codec = "video/avc",
            BitrateKbps = 8000,
        };
        await _transport.SendControlMessageAsync(ack);

        // 3. Start Capture & Encoding Pipeline
        await StartPipelineAsync(targetWidth, targetHeight);
    }

    private async Task StartPipelineAsync(int width, int height)
    {
        await StopPipelineAsync();

        _sessionCts = new CancellationTokenSource();
        var token = _sessionCts.Token;

        try
        {
            // Initialize Capture on virtual/secondary monitor
            await _captureEngine.InitializeAsync(string.Empty, token);

            // Initialize Encoder
            await _encoder.InitializeAsync(width, height, 8000, 60, "video/avc", token);

            // Wire pipeline: Capture -> Encoder -> Transport
            _captureEngine.FrameAvailable += OnFrameCaptured;
            _encoder.FrameEncoded += OnFrameEncoded;

            _isStreaming = true;
            StreamingStateChanged?.Invoke(this, true);

            // Start capture loop
            _ = Task.Run(() => _captureEngine.StartCaptureAsync(token), token);
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
        catch
        {
            // Drop frame on transient encoder error
        }
    }

    private async void OnFrameEncoded(object? sender, EncodedFrameEventArgs e)
    {
        if (!_isStreaming || e.NalData.Length == 0) return;
        try
        {
            await _transport.SendMediaFrameAsync(e.NalData, e.IsKeyframe, e.TimestampMs);
        }
        catch
        {
            // Transport write error
        }
    }

    public async Task StopStreamingAsync()
    {
        _isStreaming = false;
        StreamingStateChanged?.Invoke(this, false);
        await StopPipelineAsync();

        try
        {
            await _vddService.DisableDisplayAsync();
        }
        catch { }
    }

    private async Task StopPipelineAsync()
    {
        _sessionCts?.Cancel();
        _captureEngine.FrameAvailable -= OnFrameCaptured;
        _encoder.FrameEncoded -= OnFrameEncoded;

        try { await _captureEngine.StopCaptureAsync(); } catch { }
        try { await _encoder.DisposeAsync(); } catch { }
        try { await _captureEngine.DisposeAsync(); } catch { }
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
