using System.Runtime.InteropServices;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace DDisplay.Core.Encode;

/// <summary>
/// Hardware H.264/H.265 encoder using Windows Media Foundation Transforms (MFT).
/// Uses whatever hardware encoder is available (NVENC, QuickSync, AMF) through
/// Media Foundation's hardware MFT enumeration -- no vendor-specific SDK required.
///
/// Input: BGRA byte arrays (from DXGI capture staging texture).
/// Output: H.264/H.265 Annex-B NAL units via FrameEncoded event.
///
/// TODO: Phase 3 loopback test will validate the output can be decoded by Android MediaCodec.
/// If the MFT pipeline proves unreliable, consider Windows.Media.Transcoding or an
/// interop to OpenH264 as software fallback.
/// </summary>
public sealed class MediaFoundationEncoder : IEncoder
{
    // Native MF interop -- using Windows SDK MF APIs via P/Invoke.
    // A production implementation would use a managed wrapper (e.g., SharpMediaFoundation).
    // For the scaffold, the P/Invoke signatures are defined inline.

    private IntPtr _transformHandle = IntPtr.Zero;
    private bool _keyframeRequested;
    private int _frameCount;

    public string CodecMime { get; private set; } = "video/avc";
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public int TargetBitrateKbps { get; set; } = 8000;
    public int RefreshRateHz { get; private set; } = 60;

    public event EventHandler<EncodedFrameEventArgs>? FrameEncoded;

    public Task InitializeAsync(int widthPx, int heightPx, int bitrateKbps, int refreshRateHz,
        string codecMime = "video/avc", CancellationToken cancellationToken = default)
    {
        WidthPx = widthPx;
        HeightPx = heightPx;
        TargetBitrateKbps = bitrateKbps;
        RefreshRateHz = refreshRateHz;
        CodecMime = codecMime;

        // TODO: Initialize the actual MFT hardware encoder here.
        // Steps:
        //   1. MFStartup()
        //   2. MFTEnumEx() with MFT_CATEGORY_VIDEO_ENCODER, hardware-preferred flag,
        //      and the target format (MFVideoFormat_H264 or MFVideoFormat_H265).
        //   3. CoCreateInstance() on the first result.
        //   4. Configure IMFMediaType for input (NV12 or RGB32) and output (H.264).
        //   5. Set MF_MT_AVG_BITRATE, MF_MT_FRAME_SIZE, MF_MT_FRAME_RATE.
        //   6. IMFTransform::ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING).
        //
        // Placeholder implementation raises no frames until wired up.

        return Task.CompletedTask;
    }

    public Task EncodeFrameAsync(byte[] bgraData, long timestampMs, CancellationToken cancellationToken = default)
    {
        // TODO: Submit bgraData as an MF input sample, drain output samples, raise FrameEncoded.
        // For Phase 2 (preview-only), no encoding is needed; this is wired in Phase 3.

        // Emit a periodic simulated keyframe notification so the pipeline wiring can be
        // tested without real hardware -- remove this stub when MFT is wired.
        bool isKey = _keyframeRequested || (_frameCount % (RefreshRateHz * 2) == 0);
        _keyframeRequested = false;
        _frameCount++;

        // Stub: no real NAL data. The encoder test in Phase 3 will replace this.
        FrameEncoded?.Invoke(this, new EncodedFrameEventArgs
        {
            NalData = Array.Empty<byte>(),
            IsKeyframe = isKey,
            TimestampMs = timestampMs,
        });

        return Task.CompletedTask;
    }

    public void RequestKeyframe() => _keyframeRequested = true;

    public ValueTask DisposeAsync()
    {
        // TODO: MFShutdown() and release the MFT COM object.
        return ValueTask.CompletedTask;
    }
}
