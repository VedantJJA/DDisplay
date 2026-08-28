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
    /// Raised for each captured frame. The frame data is a CPU-side BGRA byte array.
    /// For GPU-to-encoder pipelines this will be replaced with a GPU texture handle
    /// to avoid a CPU round-trip, but the interface keeps it simple for Phase 2.
    /// </summary>
    event EventHandler<CaptureFrameEventArgs>? FrameAvailable;
}

public sealed class CaptureFrameEventArgs : EventArgs
{
    /// <summary>BGRA pixel data, width * height * 4 bytes.</summary>
    public required byte[] BgraData { get; init; }

    public required int WidthPx { get; init; }
    public required int HeightPx { get; init; }
    public required long TimestampMs { get; init; }
}
