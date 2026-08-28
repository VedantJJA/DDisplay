using System.Buffers.Binary;

namespace DDisplay.Core.Protocol;

/// <summary>
/// Writes binary media frames to a stream using the DDisplay wire protocol framing.
///
/// Frame format (SPEC.md section 2):
///   [4-byte total payload length big-endian]
///   [1-byte channel tag = 0x02]
///   [1-byte flags]
///   [4-byte presentation timestamp ms, big-endian]
///   [N bytes: H.264/H.265 Annex-B NAL units]
/// </summary>
public sealed class MediaFrameWriter
{
    private const byte MediaChannelTag = 0x02;
    public const byte FlagKeyframe = 0x01;
    public const byte FlagEndOfStream = 0x02;

    private readonly Stream _stream;
    private readonly byte[] _headerBuffer = new byte[10]; // 4 len + 1 tag + 1 flags + 4 pts

    public MediaFrameWriter(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Writes a single encoded frame to the underlying stream.
    /// </summary>
    /// <param name="nalUnits">Annex-B encoded H.264 or H.265 NAL units.</param>
    /// <param name="isKeyframe">True if this is an IDR/keyframe.</param>
    /// <param name="presentationTimestampMs">Presentation timestamp in milliseconds.</param>
    public async Task WriteFrameAsync(
        ReadOnlyMemory<byte> nalUnits,
        bool isKeyframe,
        long presentationTimestampMs,
        CancellationToken cancellationToken = default)
    {
        // Payload = 1-byte flags + 4-byte PTS + NAL data.
        int payloadLength = 1 + 4 + nalUnits.Length;

        // Full frame = 4-byte length + 1-byte channel tag + payload.
        int totalLength = 4 + 1 + payloadLength;

        // Write into header buffer.
        BinaryPrimitives.WriteInt32BigEndian(_headerBuffer.AsSpan(0), payloadLength);
        _headerBuffer[4] = MediaChannelTag;
        _headerBuffer[5] = isKeyframe ? FlagKeyframe : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(_headerBuffer.AsSpan(6), (int)presentationTimestampMs);

        await _stream.WriteAsync(_headerBuffer.AsMemory(0, 10), cancellationToken);
        await _stream.WriteAsync(nalUnits, cancellationToken);
    }

    /// <summary>
    /// Writes an end-of-stream sentinel frame.
    /// </summary>
    public async Task WriteEndOfStreamAsync(CancellationToken cancellationToken = default)
    {
        int payloadLength = 1 + 4; // flags + PTS, no NAL data.
        BinaryPrimitives.WriteInt32BigEndian(_headerBuffer.AsSpan(0), payloadLength);
        _headerBuffer[4] = MediaChannelTag;
        _headerBuffer[5] = FlagEndOfStream;
        BinaryPrimitives.WriteInt32BigEndian(_headerBuffer.AsSpan(6), 0);
        await _stream.WriteAsync(_headerBuffer.AsMemory(0, 10), cancellationToken);
    }
}

/// <summary>
/// Reads control and media frames from a stream.
/// </summary>
public sealed class FrameReader
{
    public const byte ControlChannelTag = 0x01;
    public const byte MediaChannelTag = 0x02;

    private readonly Stream _stream;

    public FrameReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Reads the next frame from the stream. Returns (channelTag, payload) or null on EOF.
    /// </summary>
    public async Task<(byte channelTag, byte[] payload)?> ReadFrameAsync(
        CancellationToken cancellationToken = default)
    {
        var lengthBuf = new byte[4];
        if (!await ReadExactAsync(lengthBuf, cancellationToken))
            return null;

        int payloadLength = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(lengthBuf);

        var tagBuf = new byte[1];
        if (!await ReadExactAsync(tagBuf, cancellationToken))
            return null;

        var payload = new byte[payloadLength];
        if (!await ReadExactAsync(payload, cancellationToken))
            return null;

        return (tagBuf[0], payload);
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
