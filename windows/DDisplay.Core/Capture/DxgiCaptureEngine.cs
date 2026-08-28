using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using System.Runtime.InteropServices;

namespace DDisplay.Core.Capture;

/// <summary>
/// Captures the virtual monitor's framebuffer using DXGI Desktop Duplication API.
///
/// This is the primary capture path for Phase 2. It creates a D3D11 device on the
/// same adapter as the virtual monitor and acquires IDXGIOutputDuplication on the
/// specific output matching the requested device name.
///
/// TODO: Phase 0 must confirm that IDXGIOutputDuplication works on the VDD's headless
/// adapter. If it does not, fall back to Windows.Graphics.Capture (see plan.md section 7.2).
/// </summary>
public sealed class DxgiCaptureEngine : ICaptureEngine
{
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private ID3D11Texture2D? _stagingTexture;
    private bool _capturing;
    private string? _monitorDeviceName;

    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public bool IsCapturing => _capturing;

    public event EventHandler<CaptureFrameEventArgs>? FrameAvailable;

    public Task InitializeAsync(string monitorDeviceName, CancellationToken cancellationToken = default)
    {
        _monitorDeviceName = monitorDeviceName;

        // Create D3D11 device.
        var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.None,
            featureLevels,
            out _d3dDevice!,
            out _d3dContext!);

        // Find the output matching the monitor device name.
        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();

        IDXGIOutput? targetOutput = null;
        for (int i = 0; ; i++)
        {
            if (adapter.EnumOutputs(i, out var output).Failure)
                break;

            var desc = output.Description;
            if (string.IsNullOrEmpty(monitorDeviceName) ||
                desc.DeviceName.Equals(monitorDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                targetOutput = output;
                WidthPx = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                HeightPx = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;
                break;
            }

            output.Dispose();
        }

        if (targetOutput is null)
            throw new InvalidOperationException(
                $"Monitor '{monitorDeviceName}' not found in DXGI output enumeration.");

        using var output1 = targetOutput.QueryInterface<IDXGIOutput1>();
        _duplication = output1.DuplicateOutput(_d3dDevice);
        targetOutput.Dispose();

        // Create a staging (CPU-readable) texture.
        var texDesc = new Texture2DDescription
        {
            Width = WidthPx,
            Height = HeightPx,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
        };
        _stagingTexture = _d3dDevice.CreateTexture2D(texDesc);

        return Task.CompletedTask;
    }

    public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (_duplication is null)
            throw new InvalidOperationException("Call InitializeAsync before StartCaptureAsync.");

        _capturing = true;

        await Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested && _capturing)
            {
                try
                {
                    var result = _duplication!.AcquireNextFrame(
                        100, // timeout ms
                        out var frameInfo,
                        out var desktopResource);

                    if (result.Failure)
                    {
                        // DXGI_ERROR_WAIT_TIMEOUT is normal when screen is idle.
                        if (result.Code == unchecked((int)0x887A0027)) continue;
                        throw new InvalidOperationException($"AcquireNextFrame failed: 0x{result.Code:X8}");
                    }

                    if (frameInfo.LastPresentTime > 0)
                    {
                        using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
                        _d3dContext!.CopyResource(_stagingTexture!, texture);

                        var mapped = _d3dContext.Map(_stagingTexture!, 0, MapMode.Read, MapFlags.None);
                        try
                        {
                            var bgraData = new byte[WidthPx * HeightPx * 4];
                            for (int row = 0; row < HeightPx; row++)
                            {
                                var srcOffset = mapped.RowPitch * row;
                                Marshal.Copy(
                                    IntPtr.Add(mapped.DataPointer, srcOffset),
                                    bgraData,
                                    row * WidthPx * 4,
                                    WidthPx * 4);
                            }

                            FrameAvailable?.Invoke(this, new CaptureFrameEventArgs
                            {
                                BgraData = bgraData,
                                WidthPx = WidthPx,
                                HeightPx = HeightPx,
                                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            });
                        }
                        finally
                        {
                            _d3dContext.Unmap(_stagingTexture!, 0);
                        }
                    }

                    desktopResource.Dispose();
                    _duplication!.ReleaseFrame();
                }
                catch (OperationCanceledException)
                {
                    break;
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
        _stagingTexture?.Dispose();
        _d3dContext?.Dispose();
        _d3dDevice?.Dispose();
        return ValueTask.CompletedTask;
    }
}
