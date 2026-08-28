using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDisplay.Core.Protocol;

// ---------------------------------------------------------------------------
// Shared JSON serialization options used throughout the control channel.
// ---------------------------------------------------------------------------

public static class ControlChannelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

// ---------------------------------------------------------------------------
// Base type for all control messages.
// ---------------------------------------------------------------------------

public abstract class ControlMessage
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = 1;
}

// ---------------------------------------------------------------------------
// Android -> Windows
// ---------------------------------------------------------------------------

public sealed class HelloMessage : ControlMessage
{
    public override string Type => "hello";

    [JsonPropertyName("deviceModel")]
    public string DeviceModel { get; set; } = string.Empty;

    [JsonPropertyName("screenWidthPx")]
    public int ScreenWidthPx { get; set; }

    [JsonPropertyName("screenHeightPx")]
    public int ScreenHeightPx { get; set; }

    [JsonPropertyName("densityDpi")]
    public int DensityDpi { get; set; }

    [JsonPropertyName("supportedCodecs")]
    public List<string> SupportedCodecs { get; set; } = new();

    [JsonPropertyName("maxDecodeWidthPx")]
    public int MaxDecodeWidthPx { get; set; }

    [JsonPropertyName("maxDecodeHeightPx")]
    public int MaxDecodeHeightPx { get; set; }
}

public sealed class StartStreamMessage : ControlMessage
{
    public override string Type => "start-stream";

    [JsonPropertyName("screenWidthPx")]
    public int ScreenWidthPx { get; set; }

    [JsonPropertyName("screenHeightPx")]
    public int ScreenHeightPx { get; set; }
}

public sealed class StopStreamMessage : ControlMessage
{
    public override string Type => "stop-stream";
}

public sealed class ScreenshotMessage : ControlMessage
{
    public override string Type => "screenshot";

    [JsonPropertyName("imageBase64")]
    public string ImageBase64 { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }
}

public sealed class RequestScreenshotMessage : ControlMessage
{
    public override string Type => "request-screenshot";
}

public sealed class HelloAckMessage : ControlMessage
{
    public override string Type => "hello-ack";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("virtualDisplayWidthPx")]
    public int VirtualDisplayWidthPx { get; set; }

    [JsonPropertyName("virtualDisplayHeightPx")]
    public int VirtualDisplayHeightPx { get; set; }

    [JsonPropertyName("refreshRateHz")]
    public int RefreshRateHz { get; set; } = 60;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = "video/avc";

    [JsonPropertyName("bitrateKbps")]
    public int BitrateKbps { get; set; } = 8000;

    [JsonPropertyName("keyframeIntervalSec")]
    public int KeyframeIntervalSec { get; set; } = 2;
}

// ---------------------------------------------------------------------------
// Pairing (Wi-Fi path)
// ---------------------------------------------------------------------------

public sealed class PairRequestMessage : ControlMessage
{
    public override string Type => "pair-request";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class PairConfirmMessage : ControlMessage
{
    public override string Type => "pair-confirm";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("deviceFingerprint")]
    public string DeviceFingerprint { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Touch input
// ---------------------------------------------------------------------------

public sealed class TouchMessage : ControlMessage
{
    public override string Type => "touch";

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty; // "down" | "move" | "up" | "cancel"

    [JsonPropertyName("pointerId")]
    public int PointerId { get; set; }

    [JsonPropertyName("normalizedX")]
    public double NormalizedX { get; set; }

    [JsonPropertyName("normalizedY")]
    public double NormalizedY { get; set; }

    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }
}

// ---------------------------------------------------------------------------
// Heartbeat
// ---------------------------------------------------------------------------

public sealed class HeartbeatMessage : ControlMessage
{
    public override string Type => "heartbeat";

    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }
}

public sealed class HeartbeatAckMessage : ControlMessage
{
    public override string Type => "heartbeat-ack";

    [JsonPropertyName("echoTimestampMs")]
    public long EchoTimestampMs { get; set; }

    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }
}

// ---------------------------------------------------------------------------
// Test data / Debug
// ---------------------------------------------------------------------------

public sealed class TestDataMessage : ControlMessage
{
    public override string Type => "test-data";

    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }
}

public sealed class TestDataAckMessage : ControlMessage
{
    public override string Type => "test-data-ack";

    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("echoTimestampMs")]
    public long EchoTimestampMs { get; set; }

    [JsonPropertyName("bytesReceived")]
    public long BytesReceived { get; set; }
}

// ---------------------------------------------------------------------------
// Session end
// ---------------------------------------------------------------------------

public sealed class ByeMessage : ControlMessage
{
    public override string Type => "bye";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "user-disconnect";
}

public sealed class ErrorMessage : ControlMessage
{
    public override string Type => "error";

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Real-Time Change-Detection & Cursor Messages
// ---------------------------------------------------------------------------

public sealed class CursorUpdateMessage : ControlMessage
{
    public override string Type => "cursor";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("shapeBase64")]
    public string? ShapeBase64 { get; set; }

    [JsonPropertyName("shapeWidth")]
    public int ShapeWidth { get; set; }

    [JsonPropertyName("shapeHeight")]
    public int ShapeHeight { get; set; }

    [JsonPropertyName("hotspotX")]
    public int HotspotX { get; set; }

    [JsonPropertyName("hotspotY")]
    public int HotspotY { get; set; }
}

public sealed class TilePatchMessage : ControlMessage
{
    public override string Type => "tile-patch";

    [JsonPropertyName("tileX")]
    public int TileX { get; set; }

    [JsonPropertyName("tileY")]
    public int TileY { get; set; }

    [JsonPropertyName("tileWidth")]
    public int TileWidth { get; set; }

    [JsonPropertyName("tileHeight")]
    public int TileHeight { get; set; }

    [JsonPropertyName("imageBase64")]
    public string ImageBase64 { get; set; } = string.Empty;

    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }
}
