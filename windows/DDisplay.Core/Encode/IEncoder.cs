namespace DDisplay.Core.Encode;

/// <summary>
/// Encodes raw BGRA frames to H.264 or H.265 Annex-B NAL units.
/// </summary>
public interface IEncoder : IAsyncDisposable
{
    /// <summary>Codec MIME type being used, e.g., "video/avc" or "video/hevc".</summary>
    string CodecMime { get; }

    int WidthPx { get; }
    int HeightPx { get; }
    int TargetBitrateKbps { get; set; }

    /// <summary>
    /// Initializes the encoder for the given resolution and codec.
    /// </summary>
    Task InitializeAsync(int widthPx, int heightPx, int bitrateKbps, int refreshRateHz,
        string codecMime = "video/avc", CancellationToken cancellationToken = default);

    /// <summary>
    /// Encodes one BGRA frame and raises <see cref="FrameEncoded"/> when output is ready.
    /// </summary>
    Task EncodeFrameAsync(byte[] bgraData, long timestampMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the encoder to produce an IDR (keyframe) on the next encode call.
    /// </summary>
    void RequestKeyframe();

    /// <summary>Raised when a NAL unit buffer is ready to be sent.</summary>
    event EventHandler<EncodedFrameEventArgs>? FrameEncoded;
}

public sealed class EncodedFrameEventArgs : EventArgs
{
    public required byte[] NalData { get; init; }
    public required bool IsKeyframe { get; init; }
    public required long TimestampMs { get; init; }
}
