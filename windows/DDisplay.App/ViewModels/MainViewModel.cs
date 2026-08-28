using System.ComponentModel;
using System.Runtime.CompilerServices;
using DDisplay.Core.Transport;
using DDisplay.VddControl;

namespace DDisplay.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IVirtualDisplayService _vddService;
    private readonly TransportManager _transportManager;

    private string _statusText = "Waiting for connection...";
    private string _transportLabel = "No transport";
    private bool _isConnected;
    private bool _isVddInstalled;
    private bool _isStreaming;

    public MainViewModel(IVirtualDisplayService vddService, TransportManager transportManager)
    {
        _vddService = vddService;
        _transportManager = transportManager;

        _isVddInstalled = vddService.IsDriverInstalled;
        StatusText = _isVddInstalled
            ? "Virtual Display Driver detected. Ready to connect."
            : "Virtual Display Driver not found. Please install VDC.";

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

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetField(ref _isStreaming, value);
    }

    public async Task ShutdownAsync()
    {
        await _transportManager.StopMonitoringAsync();
    }

    private void OnTransportChanged(object? sender, TransportChangedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = e.Transport.IsConnected;
            TransportLabel = e.Transport.DisplayName;
            StatusText = $"Connected via {e.Transport.DisplayName}.";
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
