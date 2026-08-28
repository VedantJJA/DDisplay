using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DDisplay.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private int _port = 7878;
    private int _bitrateKbps = 8000;
    private int _refreshRateHz = 60;
    private string _codec = "video/avc";
    private bool _startOnBoot;
    private string _adbPath = "adb";

    public int Port
    {
        get => _port;
        set => SetField(ref _port, Math.Clamp(value, 1024, 65535));
    }

    public int BitrateKbps
    {
        get => _bitrateKbps;
        set => SetField(ref _bitrateKbps, Math.Clamp(value, 500, 50000));
    }

    public int RefreshRateHz
    {
        get => _refreshRateHz;
        set => SetField(ref _refreshRateHz, value);
    }

    public string Codec
    {
        get => _codec;
        set => SetField(ref _codec, value);
    }

    public bool StartOnBoot
    {
        get => _startOnBoot;
        set => SetField(ref _startOnBoot, value);
    }

    public string AdbPath
    {
        get => _adbPath;
        set => SetField(ref _adbPath, value);
    }

    public IReadOnlyList<string> AvailableCodecs { get; } = new[] { "video/avc", "video/hevc" };
    public IReadOnlyList<int> RefreshRateOptions { get; } = new[] { 30, 60, 90, 120 };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
