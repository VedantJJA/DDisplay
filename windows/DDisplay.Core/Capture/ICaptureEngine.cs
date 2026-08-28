namespace DDisplay.Core.Capture;

/// <summary>
/// Provides frames from the virtual monitor's framebuffer.
/// </summary>
public interface ICaptureEngine : IAsyncDisposable
{
    /// <summary>Width of the captured output in pixels.</summary>
    int WidthPx { get; }

    /// <summary>Height of the captured output in pixels.</summary>
    int HeightPx { get; }

    /// <summary>True when the capture loop is running.</summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Initializes the capture on the virtual monitor identified by <paramref name="monitorDeviceName"/>.
    /// </summary>
    Task InitializeAsync(string monitorDeviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the capture loop and raises <see cref="FrameAvailable"/> for each frame.
    /// </summary>
    Task StartCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the capture loop.</summary>
    Task StopCaptureAsync();

    /// <summary>
    /// Raised for each captured frame or update (Full, Patch, or CursorOnly).
    /// </summary>
    event EventHandler<CaptureFrameEventArgs>? FrameAvailable;
}

public sealed class CursorInfo
{
    public int X { get; set; }
    public int Y { get; set; }
    public bool Visible { get; set; } = true;
    public string? ShapeBase64 { get; set; }
    public int ShapeWidth { get; set; }
    public int ShapeHeight { get; set; }
    public int HotspotX { get; set; }
    public int HotspotY { get; set; }
}

public sealed class CaptureFrameEventArgs : EventArgs
{
    /// <summary>BGRA pixel data, width * height * 4 bytes.</summary>
    public required byte[] BgraData { get; init; }

    public required int WidthPx { get; init; }
    public required int HeightPx { get; init; }
    public required long TimestampMs { get; init; }
    public FrameClassification Classification { get; init; } = FrameClassification.Full;
    public List<TilePatch>? Patches { get; init; }
    public CursorInfo? Cursor { get; init; }
}
