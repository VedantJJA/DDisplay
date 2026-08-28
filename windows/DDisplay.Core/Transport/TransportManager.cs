using System.Net.NetworkInformation;

namespace DDisplay.Core.Transport;

/// <summary>
/// Monitors the system for changes and selects the best available transport
/// in priority order: ADB-USB > USB-tethering > Wi-Fi.
///
/// Raises TransportChanged when the active transport switches.
/// </summary>
public sealed class TransportManager : IAsyncDisposable
{
    // Known Android RNDIS/NCM USB vendor names (partial list -- expand from Phase 0 findings).
    private static readonly string[] AndroidRndisVendorSubstrings =
    {
        "Android", "RNDIS", "Remote NDIS", "USB Ethernet",
    };

    private readonly string _adbPath;
    private readonly int _port;
    private ITransport? _activeTransport;
    private CancellationTokenSource? _monitorCts;

    public TransportManager(string adbPath = "adb", int port = WifiTransport.DefaultPort)
    {
        _adbPath = adbPath;
        _port = port;
    }

    public ITransport? ActiveTransport => _activeTransport;

    /// <summary>Raised when a new transport becomes active (connected or switched).</summary>
    public event EventHandler<TransportChangedEventArgs>? TransportChanged;

    /// <summary>
    /// Starts the background monitor loop that watches for device/network changes
    /// and auto-selects the best available transport.
    /// </summary>
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

        if (_activeTransport is not null)
        {
            await _activeTransport.DisconnectAsync();
            await _activeTransport.DisposeAsync();
            _activeTransport = null;
        }
    }

    /// <summary>
    /// Overrides automatic selection and forces a specific transport type.
    /// Used by the UI debug controls.
    /// </summary>
    public async Task ForceTransportAsync(TransportType type, CancellationToken cancellationToken = default)
    {
        if (_activeTransport is not null)
        {
            await _activeTransport.DisconnectAsync(cancellationToken);
            await _activeTransport.DisposeAsync();
        }

        _activeTransport = CreateTransport(type);
        await _activeTransport.ConnectAsync(cancellationToken);
        TransportChanged?.Invoke(this, new TransportChangedEventArgs { Transport = _activeTransport, Type = type });
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var best = await DetectBestTransportTypeAsync(cancellationToken);

            if (best.HasValue && (_activeTransport is null || !_activeTransport.IsConnected))
            {
                try
                {
                    var transport = CreateTransport(best.Value);
                    await transport.ConnectAsync(cancellationToken);
                    _activeTransport = transport;
                    TransportChanged?.Invoke(this, new TransportChangedEventArgs
                    {
                        Transport = _activeTransport,
                        Type = best.Value,
                    });
                }
                catch (TransportException)
                {
                    // Transport unavailable -- try again next cycle.
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private async Task<TransportType?> DetectBestTransportTypeAsync(CancellationToken cancellationToken)
    {
        // 1. Try ADB.
        try
        {
            var adb = new AdbUsbTransport(_adbPath, _port);
            var devices = await adb.ListDevicesAsync(cancellationToken);
            if (devices.Count > 0) return TransportType.AdbUsb;
        }
        catch { /* ADB not available or no device */ }

        // 2. Try USB tethering (detect RNDIS adapter).
        if (IsAndroidRndisAdapterPresent()) return TransportType.UsbTether;

        // 3. Wi-Fi fallback -- always available if the listener can bind.
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
            TransportType.UsbTether => new WifiTransport(_port), // Reuses TCP/LAN logic on the RNDIS interface.
            TransportType.Wifi => new WifiTransport(_port),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        // Trigger immediate re-evaluation on network change.
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), _monitorCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await StopMonitoringAsync();
        _monitorCts?.Dispose();
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
