using System.Diagnostics;
using System.Net;

namespace DDisplay.Core.Transport;

/// <summary>
/// ADB-USB transport. Sets up an `adb reverse` tunnel so the Android device can connect
/// to the Windows TCP server on localhost.
/// </summary>
public sealed class AdbUsbTransport : TcpLanTransport
{
    public const int DefaultPort = 7878;

    private readonly string _adbPath;
    private readonly int _port;
    private string? _deviceSerial;

    public AdbUsbTransport(string? adbPath = null, int port = DefaultPort)
    {
        _adbPath = ResolveAdbPath(adbPath);
        _port = port;
    }

    public static string ResolveAdbPath(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath)) return customPath;

        // 1. Check local application tools/adb.exe
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var bundledAdb = Path.Combine(baseDir, @"tools\adb.exe");
        if (File.Exists(bundledAdb)) return bundledAdb;

        var localToolsAdb = Path.Combine(baseDir, @"..\..\..\..\..\windows\DDisplay.App\tools\adb.exe");
        if (File.Exists(localToolsAdb)) return Path.GetFullPath(localToolsAdb);

        // 2. Check LocalAppData Android SDK
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sdkAdb = Path.Combine(localAppData, @"Android\Sdk\platform-tools\adb.exe");
        if (File.Exists(sdkAdb)) return sdkAdb;

        // 3. Check ANDROID_HOME / ANDROID_SDK_ROOT
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(androidHome))
        {
            var homeAdb = Path.Combine(androidHome, @"platform-tools\adb.exe");
            if (File.Exists(homeAdb)) return homeAdb;
        }

        return "adb";
    }

    public override string DisplayName => "USB (ADB)";

    protected override IPEndPoint GetListenEndPoint() =>
        new(IPAddress.Any, _port);

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var devices = await ListDevicesAsync(cancellationToken);
            if (devices.Count > 0)
            {
                _deviceSerial = devices[0];
                foreach (var dev in devices)
                {
                    await SetupReverseAsync(dev, cancellationToken);
                }
            }
            else
            {
                await RunAdbAsync($"reverse tcp:{_port} tcp:{_port}", cancellationToken);
            }
        }
        catch
        {
            // If adb reverse fails temporarily, continue starting the TCP listener so connections still work
        }

        await base.ConnectAsync(cancellationToken);
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await base.DisconnectAsync(cancellationToken);
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

    private async Task SetupReverseAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            await RunAdbAsync($"-s {serial} reverse tcp:{_port} tcp:{_port}", cancellationToken);
        }
        catch { }
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
