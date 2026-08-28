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
    public void MockVirtualDisplayService_AddMonitor_Persists()
    {
        var svc = new MockVirtualDisplayService();
        var entry = new MonitorEntry { WidthPx = 1280, HeightPx = 720, RefreshRateHz = 60 };
        svc.AddOrUpdateMonitorAsync(entry).Wait();

        var monitors = svc.GetMonitors();
        Assert.Single(monitors);
        Assert.Equal(1280, monitors[0].WidthPx);
    }

    [Fact]
    public void MockVirtualDisplayService_RemoveMonitor_Removes()
    {
        var svc = new MockVirtualDisplayService();
        var entry = new MonitorEntry { WidthPx = 1920, HeightPx = 1080, RefreshRateHz = 60 };
        svc.AddOrUpdateMonitorAsync(entry).Wait();
        svc.RemoveMonitorAsync(0).Wait();

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
}
