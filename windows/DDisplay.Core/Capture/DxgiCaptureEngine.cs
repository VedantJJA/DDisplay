using System.Drawing;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DDisplay.Core.Capture;

/// <summary>
/// Captures the virtual/secondary monitor's framebuffer using DXGI Desktop Duplication API.
/// Employs change-detection (GetFrameDirtyRects and cursor tracking) to classify updates
/// into CursorOnly, Dirty Tile Patches, or Full-Frame Video.
/// </summary>
public sealed class DxgiCaptureEngine : ICaptureEngine
{
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private ID3D11Texture2D? _stagingTexture;
    private byte[]? _lastFrameBgra;
    private bool _capturing;

    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public bool IsCapturing => _capturing;

    public event EventHandler<CaptureFrameEventArgs>? FrameAvailable;

    public Task InitializeAsync(string monitorDeviceName, CancellationToken cancellationToken = default)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? selectedAdapter = null;
        IDXGIOutput? targetOutput = null;

        var allOutputs = new List<(IDXGIAdapter1 Adapter, IDXGIOutput Output, OutputDescription Desc)>();

        // Enumerate all outputs across all adapters
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
            {
                allOutputs.Add((adapter, output, output.Description));
            }
        }

        if (allOutputs.Count == 0)
        {
            throw new InvalidOperationException("No display outputs found on any DXGI adapter.");
        }

        // 1. If monitor name specified, match it
        if (!string.IsNullOrEmpty(monitorDeviceName))
        {
            var match = allOutputs.FirstOrDefault(x => x.Desc.DeviceName.Equals(monitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match.Output != null)
            {
                selectedAdapter = match.Adapter;
                targetOutput = match.Output;
            }
        }

        // 2. Otherwise prefer the second/extended monitor (non-primary), or output 0 if only 1 exists
        if (targetOutput is null)
        {
            if (allOutputs.Count > 1)
            {
                selectedAdapter = allOutputs[1].Adapter;
                targetOutput = allOutputs[1].Output;
            }
            else
            {
                selectedAdapter = allOutputs[0].Adapter;
                targetOutput = allOutputs[0].Output;
            }
        }

        var desc = targetOutput.Description;
        WidthPx = Math.Abs(desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left);
        HeightPx = Math.Abs(desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top);

        // Clean up unused outputs
        foreach (var item in allOutputs)
        {
            if (item.Output != targetOutput) item.Output.Dispose();
            if (item.Adapter != selectedAdapter) item.Adapter.Dispose();
        }

        // Create D3D11 device on the chosen adapter
        var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        D3D11.D3D11CreateDevice(
            selectedAdapter,
            DriverType.Unknown,
            DeviceCreationFlags.None,
            featureLevels,
            out _d3dDevice!,
            out _d3dContext!);

        using var output1 = targetOutput!.QueryInterface<IDXGIOutput1>();
        _duplication = output1.DuplicateOutput(_d3dDevice);
        targetOutput.Dispose();
        selectedAdapter!.Dispose();

        // Create staging (CPU-readable) texture
        var texDesc = new Texture2DDescription
        {
            Width = (uint)WidthPx,
            Height = (uint)HeightPx,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        };
        _stagingTexture = _d3dDevice.CreateTexture2D(texDesc);
        _lastFrameBgra = new byte[WidthPx * HeightPx * 4];

        return Task.CompletedTask;
    }

    public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_duplication is null)
            throw new InvalidOperationException("Call InitializeAsync before StartCaptureAsync.");

        _capturing = true;

        await Task.Run(() =>
        {
            var dirtyRectsBuffer = new Vortice.RawRect[1024];

            while (!cancellationToken.IsCancellationRequested && _capturing)
            {
                try
                {
                    // Block up to 100ms when screen is static (OS sleeps capture thread for free)
                    var result = _duplication!.AcquireNextFrame(
                        100,
                        out var frameInfo,
                        out var desktopResource);

                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    if (!result.Success || desktopResource == null)
                    {
                        // Screen genuinely static; zero encode or network overhead
                        continue;
                    }

                    // 1. Process pointer / cursor info
                    CursorInfo? cursorInfo = null;
                    if (frameInfo.LastMouseUpdateTime > 0 && frameInfo.PointerPosition.Visible)
                    {
                        cursorInfo = new CursorInfo
                        {
                            X = frameInfo.PointerPosition.Position.X,
                            Y = frameInfo.PointerPosition.Position.Y,
                            Visible = frameInfo.PointerPosition.Visible,
                        };
                    }

                    // 2. Query Dirty Rectangles
                    var dirtyRectList = new List<(int Left, int Top, int Right, int Bottom)>();
                    int dirtyRectsCount = 0;

                    if (frameInfo.TotalMetadataBufferSize > 0)
                    {
                        try
                        {
                            _duplication.GetFrameDirtyRects((uint)(dirtyRectsBuffer.Length * Marshal.SizeOf<Vortice.RawRect>()), dirtyRectsBuffer, out uint dirtyBytesWritten);
                            dirtyRectsCount = (int)dirtyBytesWritten / Marshal.SizeOf<Vortice.RawRect>();

                            for (int i = 0; i < dirtyRectsCount; i++)
                            {
                                var r = dirtyRectsBuffer[i];
                                dirtyRectList.Add((r.Left, r.Top, r.Right, r.Bottom));
                            }
                        }
                        catch { }
                    }

                    // 3. Copy resource to staging texture
                    using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    _d3dContext!.CopyResource(_stagingTexture!, texture);

                    var mapped = _d3dContext.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    byte[] currentBgra;
                    try
                    {
                        currentBgra = new byte[WidthPx * HeightPx * 4];
                        for (int row = 0; row < HeightPx; row++)
                        {
                            var srcOffset = (int)mapped.RowPitch * row;
                            Marshal.Copy(
                                IntPtr.Add(mapped.DataPointer, srcOffset),
                                currentBgra,
                                row * WidthPx * 4,
                                WidthPx * 4);
                        }
                    }
                    finally
                    {
                        _d3dContext.Unmap(_stagingTexture!, 0);
                        desktopResource.Dispose();
                        _duplication.ReleaseFrame();
                    }

                    _lastFrameBgra = currentBgra;

                    // 4. Classify frame
                    double changeRatio = TilePatchCompressor.CalculateChangeRatio(dirtyRectList, WidthPx, HeightPx);

                    if (changeRatio == 0 && cursorInfo != null)
                    {
                        // Path A: CURSOR_ONLY update
                        FrameAvailable?.Invoke(this, new CaptureFrameEventArgs
                        {
                            BgraData = Array.Empty<byte>(),
                            WidthPx = WidthPx,
                            HeightPx = HeightPx,
                            TimestampMs = now,
                            Classification = FrameClassification.CursorOnly,
                            Cursor = cursorInfo,
                        });
                    }
                    else if (changeRatio > 0 && changeRatio < TilePatchCompressor.SmallChangeThreshold && dirtyRectList.Count > 0)
                    {
                        // Path B: PATCH update (small dirty tiles)
                        var snappedTiles = TilePatchCompressor.MergeAndSnapRectangles(dirtyRectList, WidthPx, HeightPx);
                        var patches = new List<TilePatch>();

                        foreach (var (tx, ty, tw, th) in snappedTiles)
                        {
                            var patch = TilePatchCompressor.ExtractTilePatch(currentBgra, WidthPx, HeightPx, tx, ty, tw, th);
                            if (patch != null)
                            {
                                patches.Add(patch);
                            }
                        }

                        FrameAvailable?.Invoke(this, new CaptureFrameEventArgs
                        {
                            BgraData = Array.Empty<byte>(),
                            WidthPx = WidthPx,
                            HeightPx = HeightPx,
                            TimestampMs = now,
                            Classification = FrameClassification.Patch,
                            Patches = patches,
                            Cursor = cursorInfo,
                        });
                    }
                    else
                    {
                        // Path C: FULL frame video update
                        FrameAvailable?.Invoke(this, new CaptureFrameEventArgs
                        {
                            BgraData = currentBgra,
                            WidthPx = WidthPx,
                            HeightPx = HeightPx,
                            TimestampMs = now,
                            Classification = FrameClassification.Full,
                            Cursor = cursorInfo,
                        });
                    }
                }
                catch (SharpGen.Runtime.SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost || ex.ResultCode == Vortice.DXGI.ResultCode.InvalidCall)
                {
                    // Display mode or orientation changed; recreate duplication
                    try
                    {
                        _duplication?.Dispose();
                        _duplication = null;
                        Thread.Sleep(500);
                        InitializeAsync(string.Empty, cancellationToken).Wait(cancellationToken);
                    }
                    catch { }
                }
                catch
                {
                    // Ignore single transient capture errors
                }
            }
        }, cancellationToken);
    }

    public Task StopCaptureAsync()
    {
        _capturing = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _capturing = false;
        _duplication?.Dispose();
        _duplication = null;
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _d3dContext?.Dispose();
        _d3dContext = null;
        _d3dDevice?.Dispose();
        _d3dDevice = null;
        return ValueTask.CompletedTask;
    }
}
