using System.Net.NetworkInformation;

namespace DDisplay.Core.Transport;

/// <summary>
/// Monitors the system for changes and manages transport listeners with single-instance synchronization.
/// </summary>
public sealed class TransportManager : IAsyncDisposable
{
    private static readonly string[] AndroidRndisVendorSubstrings =
    {
        "Android", "RNDIS", "Remote NDIS", "USB Ethernet",
    };

    private readonly string _adbPath;
    private readonly int _port;
    private readonly SemaphoreSlim _transportLock = new(1, 1);
    private ITransport? _activeTransport;
    private CancellationTokenSource? _monitorCts;

    public TransportManager(string? adbPath = null, int port = WifiTransport.DefaultPort)
    {
        _adbPath = AdbUsbTransport.ResolveAdbPath(adbPath);
        _port = port;
    }

    public ITransport? ActiveTransport => _activeTransport;

    public event EventHandler<TransportChangedEventArgs>? TransportChanged;

    public void StartMonitoring()
    {
        _monitorCts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), _monitorCts.Token);
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    public async Task StopMonitoringAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _monitorCts?.Cancel();

        await _transportLock.WaitAsync();
        try
        {
            if (_activeTransport is not null)
            {
                await _activeTransport.DisconnectAsync();
                await _activeTransport.DisposeAsync();
                _activeTransport = null;
            }
        }
        finally
        {
            _transportLock.Release();
        }
    }

    public async Task ForceTransportAsync(TransportType type, CancellationToken cancellationToken = default)
    {
        await _transportLock.WaitAsync(cancellationToken);
        try
        {
            if (_activeTransport is not null)
            {
                await _activeTransport.DisconnectAsync(cancellationToken);
                await _activeTransport.DisposeAsync();
                _activeTransport = null;
            }

            var transport = CreateTransport(type);
            _activeTransport = transport;
            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.ConnectAsync(cancellationToken);
                    TransportChanged?.Invoke(this, new TransportChangedEventArgs { Transport = transport, Type = type });
                }
                catch
                {
                    // Ignore cancellation
                }
            }, cancellationToken);
        }
        finally
        {
            _transportLock.Release();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var best = await DetectBestTransportTypeAsync(cancellationToken);

                if (best.HasValue)
                {
                    await _transportLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (_activeTransport is null)
                        {
                            var transport = CreateTransport(best.Value);
                            _activeTransport = transport;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await transport.ConnectAsync(cancellationToken);
                                    TransportChanged?.Invoke(this, new TransportChangedEventArgs
                                    {
                                        Transport = transport,
                                        Type = best.Value,
                                    });
                                }
                                catch
                                {
                                    // Transport failed or cancelled
                                }
                            }, cancellationToken);
                        }
                    }
                    finally
                    {
                        _transportLock.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Transient detection exception
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private async Task<TransportType?> DetectBestTransportTypeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var adb = new AdbUsbTransport(_adbPath, _port);
            var devices = await adb.ListDevicesAsync(cancellationToken);
            if (devices.Count > 0) return TransportType.AdbUsb;
        }
        catch { }

        if (IsAndroidRndisAdapterPresent()) return TransportType.UsbTether;

        return TransportType.Wifi;
    }

    private static bool IsAndroidRndisAdapterPresent()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var substring in AndroidRndisVendorSubstrings)
            {
                if (nic.Description.Contains(substring, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private ITransport CreateTransport(TransportType type) =>
        type switch
        {
            TransportType.AdbUsb => new AdbUsbTransport(_adbPath, _port),
            TransportType.UsbTether => new WifiTransport(_port),
            TransportType.Wifi => new WifiTransport(_port),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), _monitorCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await StopMonitoringAsync();
        _monitorCts?.Dispose();
        _transportLock.Dispose();
    }
}

public enum TransportType
{
    AdbUsb,
    UsbTether,
    Wifi,
}

public sealed class TransportChangedEventArgs : EventArgs
{
    public required ITransport Transport { get; init; }
    public required TransportType Type { get; init; }
}
