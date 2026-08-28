using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DDisplay.VddControl;

/// <summary>
/// Checks whether the Virtual Display Driver is installed and active in the system.
/// </summary>
public static class VddInstallChecker
{
    public const string SettingsFilePath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
    public const string DriverDirectory = @"C:\VirtualDisplayDriver";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    public const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

    /// <summary>
    /// Checks if the VDD device is registered in the system device tree via the registry.
    /// </summary>
    public static bool IsDriverDevicePresent()
    {
        try
        {
            const string rootKeyPath = @"SYSTEM\CurrentControlSet\Enum\ROOT";
            using var rootKey = Registry.LocalMachine.OpenSubKey(rootKeyPath);
            if (rootKey is null) return false;

            foreach (var subName in rootKey.GetSubKeyNames())
            {
                if (subName.StartsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                    subName.StartsWith("IDDSAMPL", StringComparison.OrdinalIgnoreCase) ||
                    subName.StartsWith("MttVDD", StringComparison.OrdinalIgnoreCase) ||
                    subName.StartsWith("IddSample", StringComparison.OrdinalIgnoreCase))
                {
                    using var subKey = rootKey.OpenSubKey(subName);
                    if (subKey is null) continue;

                    foreach (var instanceName in subKey.GetSubKeyNames())
                    {
                        using var instanceKey = subKey.OpenSubKey(instanceName);
                        var desc = instanceKey?.GetValue("DeviceDesc") as string;
                        var mfg = instanceKey?.GetValue("Mfg") as string;
                        if ((desc?.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) == true) ||
                            (desc?.Contains("IddSample", StringComparison.OrdinalIgnoreCase) == true) ||
                            (mfg?.Contains("MikeTheTech", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a secondary / virtual display is actively attached to the Windows desktop.
    /// </summary>
    public static bool IsVirtualDisplayActive()
    {
        try
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            uint i = 0;
            while (EnumDisplayDevices(null, i, ref d, 0))
            {
                bool attached = (d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
                bool primary = (d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

                if (attached && !primary)
                {
                    return true;
                }

                if (attached && (d.DeviceString.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) ||
                                 d.DeviceString.Contains("IddSample", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                d.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                i++;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the driver device is registered on the system.
    /// </summary>
    public static bool IsFullyInstalled() => IsDriverDevicePresent() || File.Exists(SettingsFilePath);
}
