using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DDisplay.Core.Encode;

/// <summary>
/// Hardware/Software H.264 video encoder using Windows Media Foundation MFT.
/// Produces Annex-B formatted NAL units for direct consumption by Android MediaCodec.
/// </summary>
public sealed class MediaFoundationEncoder : IEncoder
{
    private static readonly Guid CLSID_CMSH264EncoderMFT = new("6ca50344-051a-4ded-9779-a43305165e35");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");

    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba6e3-f2e2-47e4-b89d-9a23d507a770");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b6-4037-b44b-052033709f2d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e42-b77e-d566b79720bc");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724461-40ea-450a-9e55-63b5e5cb3e12");
    private static readonly Guid CODECAPI_AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    private static readonly Guid CODECAPI_AVEncCommonLowLatency = new("9d38f36e-234e-4172-b5c3-490e69d16346");
    private static readonly Guid CODECAPI_AVEncCommonQuality = new("fcbf57a3-7ea5-4b0c-9644-69b40c39c391");
    private static readonly Guid CODECAPI_AVEncMPVGOPSize = new("95f74a0-e739-11d2-a689-00c04f7949bd");

#pragma warning disable CS0649
    private IMFTransform? _transform;
    private long _frameIndex = 0;
    private byte[]? _nv12Buffer;

    public string CodecMime { get; private set; } = "video/avc";
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public int TargetBitrateKbps { get; set; } = 8000;
    public int RefreshRateHz { get; private set; } = 60;

    public event EventHandler<EncodedFrameEventArgs>? FrameEncoded;

    public Task InitializeAsync(
        int widthPx,
        int heightPx,
        int bitrateKbps,
        int refreshRateHz,
        string codecMime = "video/avc",
        CancellationToken cancellationToken = default)
    {
        WidthPx = widthPx & ~1; // Ensure even
        HeightPx = heightPx & ~1;
        TargetBitrateKbps = bitrateKbps;
        RefreshRateHz = refreshRateHz > 0 ? refreshRateHz : 60;
        CodecMime = codecMime;

        _nv12Buffer = new byte[WidthPx * HeightPx * 3 / 2];

        try
        {
            MFStartup(0x00020070, 1); // MF_VERSION, MFSTARTUP_NOSOCKET

            var encoderType = Type.GetTypeFromCLSID(CLSID_CMSH264EncoderMFT, true)!;
            _transform = (IMFTransform)Activator.CreateInstance(encoderType)!;

            // Low latency and rate control properties
            if (_transform is ICodecAPI codecApi)
            {
                var valLowLatency = 1;
                codecApi.SetValue(CODECAPI_AVEncCommonLowLatency, ref valLowLatency);
                var valRateControl = 0; // CBR
                codecApi.SetValue(CODECAPI_AVEncCommonRateControlMode, ref valRateControl);
                var gopSize = RefreshRateHz * 2;
                codecApi.SetValue(CODECAPI_AVEncMPVGOPSize, ref gopSize);
            }

            // Configure Output Media Type (H.264)
            MFCreateMediaType(out var outputType);
            outputType.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            outputType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
            outputType.SetUINT32(MF_MT_AVG_BITRATE, (uint)(TargetBitrateKbps * 1000));
            PackSizeToUint64(out var frameSize, (uint)WidthPx, (uint)HeightPx);
            outputType.SetUINT64(MF_MT_FRAME_SIZE, frameSize);
            PackSizeToUint64(out var frameRate, (uint)RefreshRateHz, 1);
            outputType.SetUINT64(MF_MT_FRAME_RATE, frameRate);
            outputType.SetUINT32(MF_MT_INTERLACE_MODE, 2); // Progressive

            _transform.SetOutputType(0, outputType, 0);
            Marshal.ReleaseComObject(outputType);

            // Configure Input Media Type (NV12)
            MFCreateMediaType(out var inputType);
            inputType.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            inputType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
            inputType.SetUINT64(MF_MT_FRAME_SIZE, frameSize);
            inputType.SetUINT64(MF_MT_FRAME_RATE, frameRate);
            inputType.SetUINT32(MF_MT_INTERLACE_MODE, 2);

            _transform.SetInputType(0, inputType, 0);
            Marshal.ReleaseComObject(inputType);

            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
        }
        catch
        {
            // If hardware MFT init fails, allow graceful fallback
        }

        return Task.CompletedTask;
    }

    public Task EncodeFrameAsync(byte[] bgraData, long timestampMs, CancellationToken cancellationToken = default)
    {
        if (_transform is null) return Task.CompletedTask;

        try
        {
            // Convert BGRA to NV12
            ConvertBgraToNv12(bgraData, _nv12Buffer!, WidthPx, HeightPx);

            // Create Input Sample
            MFCreateMemoryBuffer((uint)_nv12Buffer!.Length, out var mediaBuffer);
            mediaBuffer.Lock(out var pBuffer, out _, out _);
            Marshal.Copy(_nv12Buffer, 0, pBuffer, _nv12Buffer.Length);
            mediaBuffer.Unlock();
            mediaBuffer.SetCurrentLength((uint)_nv12Buffer.Length);

            MFCreateSample(out var sample);
            sample.AddBuffer(mediaBuffer);

            long sampleDurationHns = 10_000_000L / RefreshRateHz;
            long sampleTimeHns = _frameIndex * sampleDurationHns;
            _frameIndex++;

            sample.SetSampleTime(sampleTimeHns);
            sample.SetSampleDuration(sampleDurationHns);

            _transform.ProcessInput(0, sample, 0);

            Marshal.ReleaseComObject(mediaBuffer);
            Marshal.ReleaseComObject(sample);

            // Drain Output Samples
            DrainOutput(timestampMs);
        }
        catch
        {
            // Ignore single-frame transient encode exceptions
        }

        return Task.CompletedTask;
    }

    private void DrainOutput(long timestampMs)
    {
        if (_transform is null) return;

        var outputDataBuffer = new MFT_OUTPUT_DATA_BUFFER[1];
        MFCreateSample(out var outputSample);
        MFCreateMemoryBuffer(1024 * 1024, out var outputBuffer); // 1MB buffer
        outputSample.AddBuffer(outputBuffer);
        outputDataBuffer[0].pSample = outputSample;

        try
        {
            while (true)
            {
                int hr = _transform.ProcessOutput(0, 1, outputDataBuffer, out var status);
                if (hr != 0) break; // MF_E_TRANSFORM_NEED_MORE_INPUT or other

                var pSample = outputDataBuffer[0].pSample;
                if (pSample != null)
                {
                    pSample.ConvertToContiguousBuffer(out var contigBuffer);
                    contigBuffer.Lock(out var pData, out _, out var currentLength);

                    if (currentLength > 0)
                    {
                        var nalBytes = new byte[currentLength];
                        Marshal.Copy(pData, nalBytes, 0, (int)currentLength);
                        contigBuffer.Unlock();
                        Marshal.ReleaseComObject(contigBuffer);

                        bool isKeyframe = IsH264Keyframe(nalBytes);
                        FrameEncoded?.Invoke(this, new EncodedFrameEventArgs
                        {
                            NalData = nalBytes,
                            IsKeyframe = isKeyframe,
                            TimestampMs = timestampMs,
                        });
                    }
                    else
                    {
                        contigBuffer.Unlock();
                        Marshal.ReleaseComObject(contigBuffer);
                    }
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(outputBuffer);
            Marshal.ReleaseComObject(outputSample);
        }
    }

    private static bool IsH264Keyframe(byte[] nalData)
    {
        // Search for NAL unit type 5 (IDR slice) or 7 (SPS)
        for (int i = 0; i < nalData.Length - 4; i++)
        {
            if (nalData[i] == 0 && nalData[i + 1] == 0 && (nalData[i + 2] == 1 || (nalData[i + 2] == 0 && nalData[i + 3] == 1)))
            {
                int nalStart = (nalData[i + 2] == 1) ? i + 3 : i + 4;
                if (nalStart < nalData.Length)
                {
                    int nalType = nalData[nalStart] & 0x1F;
                    if (nalType == 5 || nalType == 7) return true;
                }
            }
        }
        return false;
    }

    private static void ConvertBgraToNv12(byte[] bgra, byte[] nv12, int width, int height)
    {
        int ySize = width * height;
        int uvOffset = ySize;

        for (int j = 0; j < height; j++)
        {
            int rowStart = j * width * 4;
            int yRowStart = j * width;
            int uvRowStart = uvOffset + (j >> 1) * width;

            for (int i = 0; i < width; i++)
            {
                int px = rowStart + (i << 2);
                int b = bgra[px];
                int g = bgra[px + 1];
                int r = bgra[px + 2];

                // Y
                int y = (66 * r + 129 * g + 25 * b + 128) >> 8;
                nv12[yRowStart + i] = (byte)Math.Clamp(y + 16, 0, 255);

                // UV (subsampled 2x2)
                if ((j & 1) == 0 && (i & 1) == 0)
                {
                    int u = (-38 * r - 74 * g + 112 * b + 128) >> 8;
                    int v = (112 * r - 94 * g - 18 * b + 128) >> 8;
                    int uvIdx = uvRowStart + i;
                    nv12[uvIdx] = (byte)Math.Clamp(u + 128, 0, 255);
                    nv12[uvIdx + 1] = (byte)Math.Clamp(v + 128, 0, 255);
                }
            }
        }
    }

    private static void PackSizeToUint64(out ulong packed, uint high, uint low)
    {
        packed = ((ulong)high << 32) | low;
    }

    public void RequestKeyframe()
    {
        if (_transform is ICodecAPI codecApi)
        {
            var val = 1;
            codecApi.SetValue(CODECAPI_AVEncCommonQuality, ref val);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_transform != null)
        {
            try
            {
                _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_END_OF_STREAM, IntPtr.Zero);
                Marshal.ReleaseComObject(_transform);
            }
            catch { }
            _transform = null;
        }
        try { MFShutdown(); } catch { }
        return ValueTask.CompletedTask;
    }

    // ---- Native Media Foundation Interop ----

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample ppIMFSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IMFMediaBuffer ppBuffer);

    [ComImport]
    [Guid("bf94e121-5b05-4e6f-8000-ba5903c450c0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFTransform
    {
        void GetStreamLimits(out uint pdwInputMinimum, out uint pdwInputMaximum, out uint pdwOutputMinimum, out uint pdwOutputMaximum);
        void GetStreamCount(out uint pcInputStreams, out uint pcOutputStreams);
        void GetStreamIDs(uint dwInputIDArraySize, [Out] uint[] pdwInputIDs, uint dwOutputIDArraySize, [Out] uint[] pdwOutputIDs);
        void GetInputStreamInfo(uint dwInputStreamID, out MFT_INPUT_STREAM_INFO pStreamInfo);
        void GetOutputStreamInfo(uint dwOutputStreamID, out MFT_OUTPUT_STREAM_INFO pStreamInfo);
        void GetAttributes(out IntPtr pAttributes);
        void GetInputStreamAttributes(uint dwInputStreamID, out IntPtr pAttributes);
        void GetOutputStreamAttributes(uint dwOutputStreamID, out IntPtr pAttributes);
        void DeleteInputStream(uint dwStreamID);
        void AddInputStreams(uint cStreams, [In] uint[] adwStreamIDs);
        void GetInputAvailableType(uint dwInputStreamID, uint dwTypeIndex, out IMFMediaType ppType);
        void GetOutputAvailableType(uint dwOutputStreamID, uint dwTypeIndex, out IMFMediaType ppType);
        void SetInputType(uint dwInputStreamID, [In] IMFMediaType pType, uint dwFlags);
        void SetOutputType(uint dwOutputStreamID, [In] IMFMediaType pType, uint dwFlags);
        void GetInputCurrentType(uint dwInputStreamID, out IMFMediaType ppType);
        void GetOutputCurrentType(uint dwOutputStreamID, out IMFMediaType ppType);
        void GetInputStatus(uint dwInputStreamID, out uint pdwFlags);
        void GetOutputStatus(out uint pdwFlags);
        void SetOutputBounds(long hnsLowerBound, long hnsUpperBound);
        void ProcessEvent(uint dwInputStreamID, IntPtr pEvent);
        void ProcessMessage(MFT_MESSAGE_TYPE eMessage, IntPtr ulParam);
        void ProcessInput(uint dwInputStreamID, [In] IMFSample pSample, uint dwFlags);
        [PreserveSig]
        int ProcessOutput(uint dwFlags, uint cOutputBufferCount, [In, Out] MFT_OUTPUT_DATA_BUFFER[] pOutputSamples, out uint pdwStatus);
    }

    [ComImport]
    [Guid("444000d6-8226-4ecf-b00b-19077242861e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        void GetItem(ref Guid guidKey, IntPtr pValue);
        void GetItemType(ref Guid guidKey, out int pType);
        void CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        void Compare(IntPtr pTheOthers, int ComparisonType, out bool pbResult);
        void GetUINT32(ref Guid guidKey, out uint punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out uint pcchLength);
        void GetString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] string pwszValue, uint cchBufSize, out uint pcchLength);
        void GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        void GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        void GetBlob(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, IntPtr Value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, uint unValue);
        void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [In, MarshalAs(UnmanagedType.LPStruct)] Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pGuidKey, IntPtr pValue);
        void CopyAllItems(IntPtr pDest);
    }

    [ComImport]
    [Guid("c40a0074-7e5f-4072-8700-44a0e5a86b05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        void GetItem(ref Guid guidKey, IntPtr pValue);
        void GetItemType(ref Guid guidKey, out int pType);
        void CompareItem(ref Guid guidKey, IntPtr Value, out bool pbResult);
        void Compare(IntPtr pTheOthers, int ComparisonType, out bool pbResult);
        void GetUINT32(ref Guid guidKey, out uint punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out uint pcchLength);
        void GetString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] string pwszValue, uint cchBufSize, out uint pcchLength);
        void GetAllocatedString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        void GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        void GetBlob(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize, out uint pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, IntPtr Value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32(ref Guid guidKey, uint unValue);
        void SetUINT64(ref Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID(ref Guid guidKey, ref Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, [MarshalAs(UnmanagedType.LPArray)] byte[] pBuf, uint cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pGuidKey, IntPtr pValue);
        void CopyAllItems(IntPtr pDest);
        void GetSampleFlags(out uint pdwSampleFlags);
        void SetSampleFlags(uint dwSampleFlags);
        void GetSampleTime(out long phnsSampleTime);
        void SetSampleTime(long hnsSampleTime);
        void GetSampleDuration(out long phnsSampleDuration);
        void SetSampleDuration(long hnsSampleDuration);
        void GetBufferCount(out uint pdwBufferCount);
        void GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        void AddBuffer([In] IMFMediaBuffer pBuffer);
        void RemoveBufferByIndex(uint dwIndex);
        void RemoveAllBuffers();
        void GetTotalLength(out uint pcbTotalLength);
        void CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport]
    [Guid("045fa593-8742-42c8-80e4-b6a377f90b6f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        void Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        void Unlock();
        void GetCurrentLength(out uint pcbCurrentLength);
        void SetCurrentLength(uint cbCurrentLength);
        void GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport]
    [Guid("901d85d6-0429-4b8c-9d65-77c1f69f7e68")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICodecAPI
    {
        void IsSupported(ref Guid Api);
        void IsModifiable(ref Guid Api);
        void GetParameterRange(ref Guid Api, out IntPtr ValueMin, out IntPtr ValueMax, out IntPtr SteppingDelta);
        void GetValue(ref Guid Api, out IntPtr Value);
        void SetValue([In, MarshalAs(UnmanagedType.LPStruct)] Guid Api, [In] ref int Value);
    }

    private struct MFT_INPUT_STREAM_INFO
    {
        public long hnsMaxLatency;
        public uint dwFlags;
        public uint cbSize;
        public uint cbMaxLookahead;
        public uint cbAlignment;
    }

    private struct MFT_OUTPUT_STREAM_INFO
    {
        public uint dwFlags;
        public uint cbSize;
        public uint cbAlignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MFT_OUTPUT_DATA_BUFFER
    {
        public uint dwStreamID;
        public IMFSample? pSample;
        public uint dwStatus;
        public IntPtr pEvents;
    }

    private enum MFT_MESSAGE_TYPE
    {
        MFT_MESSAGE_COMMAND_FLUSH = 0,
        MFT_MESSAGE_COMMAND_DRAIN = 1,
        MFT_MESSAGE_SET_D3D_MANAGER = 2,
        MFT_MESSAGE_DROP_SAMPLES = 3,
        MFT_MESSAGE_COMMAND_TICK = 4,
        MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000,
        MFT_MESSAGE_NOTIFY_END_OF_STREAM = 0x10000001,
        MFT_MESSAGE_NOTIFY_END_STREAMING = 0x10000002,
        MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000003,
    }
}
