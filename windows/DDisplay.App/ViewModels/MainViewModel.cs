using System.ComponentModel;
using System.Runtime.CompilerServices;
using DDisplay.Core;
using DDisplay.Core.Transport;
using DDisplay.VddControl;

namespace DDisplay.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IVirtualDisplayService _vddService;
    private readonly TransportManager _transportManager;
    private SessionCoordinator? _sessionCoordinator;

    private string _statusText = "Waiting for connection...";
    private string _transportLabel = "No transport";
    private bool _isConnected;
    private bool _isVddInstalled;
    private bool _isDisplayEnabled;
    private bool _isStreaming;

    public MainViewModel(IVirtualDisplayService vddService, TransportManager transportManager)
    {
        _vddService = vddService;
        _transportManager = transportManager;

        _isVddInstalled = vddService.IsDriverInstalled;
        _isDisplayEnabled = vddService.IsDisplayEnabled;

        StatusText = _isVddInstalled
            ? (_isDisplayEnabled ? "Virtual Display active. Ready to connect." : "Virtual Display standby. Connect mobile app to activate.")
            : "Virtual Display Driver not found. Please install VDD.";

        _transportManager.TransportChanged += OnTransportChanged;
        _transportManager.StartMonitoring();
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string TransportLabel
    {
        get => _transportLabel;
        set => SetField(ref _transportLabel, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    public bool IsVddInstalled
    {
        get => _isVddInstalled;
        set => SetField(ref _isVddInstalled, value);
    }

    public bool IsDisplayEnabled
    {
        get => _isDisplayEnabled;
        set => SetField(ref _isDisplayEnabled, value);
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetField(ref _isStreaming, value);
    }

    public async Task ConnectAsync()
    {
        StatusText = "Connecting to device...";
        try
        {
            await _transportManager.ForceTransportAsync(TransportType.AdbUsb);
        }
        catch (Exception ex)
        {
            StatusText = $"Connection failed: {ex.Message}";
        }
    }

    public async Task DisconnectAsync()
    {
        StatusText = "Disconnecting...";
        if (_sessionCoordinator != null)
        {
            await _sessionCoordinator.DisposeAsync();
            _sessionCoordinator = null;
        }

        await _transportManager.StopMonitoringAsync();
        await _vddService.DisableDisplayAsync();

        IsConnected = false;
        IsStreaming = false;
        IsDisplayEnabled = false;
        TransportLabel = "No transport";
        StatusText = "Disconnected.";

        _transportManager.StartMonitoring();
    }

    public async Task ToggleDisplayAsync()
    {
        if (IsDisplayEnabled)
        {
            await _vddService.DisableDisplayAsync();
            IsDisplayEnabled = false;
            StatusText = "Virtual Display disconnected.";
        }
        else
        {
            await _vddService.EnableDisplayAsync();
            IsDisplayEnabled = true;
            StatusText = "Virtual Display connected.";
        }
    }

    public async Task ShutdownAsync()
    {
        if (_sessionCoordinator != null)
        {
            await _sessionCoordinator.DisposeAsync();
            _sessionCoordinator = null;
        }

        if (IsDisplayEnabled && !IsConnected)
        {
            await _vddService.DisableDisplayAsync();
        }
        await _transportManager.StopMonitoringAsync();
    }

    private void OnTransportChanged(object? sender, TransportChangedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            IsConnected = e.Transport.IsConnected;
            TransportLabel = e.Transport.DisplayName;

            if (IsConnected)
            {
                StatusText = $"Connected via {e.Transport.DisplayName}. Streaming desktop...";
                IsDisplayEnabled = true;

                // Start Session Coordinator for live screen capture & encoding
                if (_sessionCoordinator != null)
                {
                    await _sessionCoordinator.DisposeAsync();
                }

                _sessionCoordinator = new SessionCoordinator(e.Transport, _vddService);
                _sessionCoordinator.TestDataProgress += (_, stats) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusText = $"Connected ({TransportLabel}) | Packets: {stats.Packets} | Loss: {stats.PacketLoss} | Data: {stats.Bytes / 1024.0:F1} KB | Latency: {stats.RttMs}ms";
                    });
                };
                _sessionCoordinator.StreamingStateChanged += (_, streaming) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsStreaming = streaming;
                        if (streaming)
                        {
                            StatusText = $"Connected ({TransportLabel}) - Exchanging test data...";
                        }
                    });
                };
            }
            else
            {
                StatusText = "Waiting for connection...";
                IsStreaming = false;

                if (_sessionCoordinator != null)
                {
                    await _sessionCoordinator.DisposeAsync();
                    _sessionCoordinator = null;
                }

                if (IsDisplayEnabled)
                {
                    await _vddService.DisableDisplayAsync();
                    IsDisplayEnabled = false;
                }
            }
        });
    }

    // ---- INotifyPropertyChanged ----
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
