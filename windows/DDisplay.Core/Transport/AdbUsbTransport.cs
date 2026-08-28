using System.Diagnostics;
using System.Net;

namespace DDisplay.Core.Transport;

/// <summary>
/// ADB-USB transport. Sets up an `adb reverse` tunnel so the Android device can connect
/// to the Windows TCP server on localhost.
///
/// Flow:
///   1. ConnectAsync detects the connected ADB device.
///   2. Runs `adb -s <serial> reverse tcp:PORT tcp:PORT` to forward device localhost to host.
///   3. Starts the TcpLanTransport listener on 127.0.0.1:PORT.
///   4. Android app connects its Socket to 127.0.0.1:PORT -- traffic goes over the USB cable.
/// </summary>
public sealed class AdbUsbTransport : TcpLanTransport
{
    public const int DefaultPort = 7878;

    private readonly string _adbPath;
    private readonly int _port;
    private string? _deviceSerial;

    public AdbUsbTransport(string adbPath = "adb", int port = DefaultPort)
    {
        _adbPath = adbPath;
        _port = port;
    }

    public override string DisplayName => "USB (ADB)";

    protected override IPEndPoint GetListenEndPoint() =>
        new(IPAddress.Loopback, _port);

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _deviceSerial = await DetectDeviceAsync(cancellationToken);
        await SetupReverseAsync(_deviceSerial, cancellationToken);
        await base.ConnectAsync(cancellationToken);
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await base.DisconnectAsync(cancellationToken);
        if (_deviceSerial is not null)
        {
            try
            {
                await RunAdbAsync($"-s {_deviceSerial} reverse --remove tcp:{_port}", cancellationToken);
            }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Returns serials of all authorized, connected ADB devices.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunAdbAsync("devices", cancellationToken);
        var devices = new List<string>();

        foreach (var line in output.Split('\n').Skip(1))
        {
            var parts = line.Trim().Split('\t');
            if (parts.Length == 2 && parts[1].Trim() == "device")
                devices.Add(parts[0].Trim());
        }

        return devices;
    }

    private async Task<string> DetectDeviceAsync(CancellationToken cancellationToken)
    {
        var devices = await ListDevicesAsync(cancellationToken);

        if (devices.Count == 0)
            throw new TransportException("No authorized ADB device found. Connect via USB and authorize debugging.");

        if (devices.Count > 1)
        {
            // TODO: surface device selection to the UI when multiple devices are attached.
            // For now, use the first device and log a warning.
        }

        return devices[0];
    }

    private async Task SetupReverseAsync(string serial, CancellationToken cancellationToken)
    {
        await RunAdbAsync($"-s {serial} reverse tcp:{_port} tcp:{_port}", cancellationToken);
    }

    private async Task<string> RunAdbAsync(string args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(_adbPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new TransportException($"Failed to start: {_adbPath}");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new TransportException($"adb {args} failed (exit {process.ExitCode}): {err}");
        }

        return output;
    }
}
