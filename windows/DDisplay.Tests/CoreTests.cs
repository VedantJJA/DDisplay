using DDisplay.Core.Capture;
using DDisplay.Core.Protocol;
using DDisplay.Tests.Transport;
using DDisplay.Tests.VddControl;
using DDisplay.VddControl.Models;
using Xunit;

namespace DDisplay.Tests;

public class CoreTests
{
    [Fact]
    public async Task MockTransport_Connect_IsConnected()
    {
        var transport = new MockTransport();
        await transport.ConnectAsync();
        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task MockTransport_Disconnect_IsNotConnected()
    {
        var transport = new MockTransport();
        await transport.ConnectAsync();
        await transport.DisconnectAsync();
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task MockTransport_SendControlMessage_RecordsMessage()
    {
        var transport = new MockTransport();
        await transport.ConnectAsync();

        var msg = new HelloAckMessage
        {
            SessionId = "test-session",
            VirtualDisplayWidthPx = 1080,
            VirtualDisplayHeightPx = 1920,
            RefreshRateHz = 60,
            Codec = "video/avc",
            BitrateKbps = 8000,
        };

        await transport.SendControlMessageAsync(msg);
        Assert.True(transport.WasSent("hello-ack"));
    }

    [Fact]
    public async Task MockTransport_SendFrame_RecordsFrame()
    {
        var transport = new MockTransport();
        await transport.ConnectAsync();
        await transport.SendMediaFrameAsync(new byte[] { 0x00, 0x00, 0x01, 0x65 }, true, 1000);
        Assert.Equal(1, transport.SentFrameCount);
    }

    [Fact]
    public async Task MockVirtualDisplayService_AddMonitor_Persists()
    {
        var svc = new MockVirtualDisplayService();
        var entry = new MonitorEntry { WidthPx = 1280, HeightPx = 720, RefreshRateHz = 60 };
        await svc.AddOrUpdateMonitorAsync(entry);

        var monitors = svc.GetMonitors();
        Assert.NotEmpty(monitors);
        Assert.Equal(1280, monitors[0].WidthPx);
    }

    [Fact]
    public async Task MockVirtualDisplayService_RemoveMonitor_Removes()
    {
        var svc = new MockVirtualDisplayService();
        var entry = new MonitorEntry { WidthPx = 1920, HeightPx = 1080, RefreshRateHz = 60 };
        await svc.AddOrUpdateMonitorAsync(entry);
        await svc.RemoveMonitorAsync(0);

        Assert.Empty(svc.GetMonitors());
    }

    [Fact]
    public void ControlMessages_HelloMessage_HasCorrectType()
    {
        var msg = new HelloMessage();
        Assert.Equal("hello", msg.Type);
        Assert.Equal(1, msg.ProtocolVersion);
    }

    [Fact]
    public void ControlMessages_TouchMessage_HasCorrectType()
    {
        var msg = new TouchMessage
        {
            EventType = "down",
            NormalizedX = 0.5,
            NormalizedY = 0.5,
            TimestampMs = 1000,
        };
        Assert.Equal("touch", msg.Type);
    }

    [Fact]
    public void ControlMessages_CursorUpdateMessage_HasCorrectType()
    {
        var msg = new CursorUpdateMessage
        {
            X = 100,
            Y = 200,
            Visible = true,
        };
        Assert.Equal("cursor", msg.Type);
    }

    [Fact]
    public void ControlMessages_TilePatchMessage_HasCorrectType()
    {
        var msg = new TilePatchMessage
        {
            TileX = 64,
            TileY = 128,
            TileWidth = 64,
            TileHeight = 64,
            ImageBase64 = "test",
        };
        Assert.Equal("tile-patch", msg.Type);
    }

    [Fact]
    public void TilePatchCompressor_SnapToTileGrid_AlignedTo64Px()
    {
        var (x, y, w, h) = TilePatchCompressor.SnapToTileGrid(10, 15, 75, 80, 1920, 1080);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(128, w);
        Assert.Equal(128, h);
    }

    [Fact]
    public void TilePatchCompressor_CalculateChangeRatio_AccuratePercentage()
    {
        // 1920x1080 = 2,073,600 px. 192x108 = 20,736 px (1%)
        var rects = new[] { (0, 0, 192, 108) };
        double ratio = TilePatchCompressor.CalculateChangeRatio(rects, 1920, 1080);
        Assert.InRange(ratio, 0.009, 0.011);
    }

    [Fact]
    public void TilePatchCompressor_ExtractTilePatch_ProducesValidJpeg()
    {
        byte[] fakeBgra = new byte[1920 * 1080 * 4];
        // Fill some pixels
        for (int i = 0; i < fakeBgra.Length; i += 4)
        {
            fakeBgra[i] = 255;     // B
            fakeBgra[i + 1] = 128; // G
            fakeBgra[i + 2] = 64;  // R
            fakeBgra[i + 3] = 255; // A
        }

        var patch = TilePatchCompressor.ExtractTilePatch(fakeBgra, 1920, 1080, 0, 0, 64, 64);
        Assert.NotNull(patch);
        Assert.Equal(0, patch.X);
        Assert.Equal(0, patch.Y);
        Assert.Equal(64, patch.Width);
        Assert.Equal(64, patch.Height);
        Assert.False(string.IsNullOrEmpty(patch.ImageBase64));
    }
}
