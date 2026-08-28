using Microsoft.Win32;

namespace DDisplay.VddControl;

/// <summary>
/// Checks whether the Virtual Display Driver is installed and active in the system.
/// </summary>
public static class VddInstallChecker
{
    public const string SettingsFilePath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
    public const string DriverDirectory = @"C:\VirtualDisplayDriver";

    /// <summary>
    /// Checks if the VDD device is present in the system device tree via the registry.
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
    /// Returns true if the driver device is registered on the system.
    /// </summary>
    public static bool IsFullyInstalled() => IsDriverDevicePresent() || File.Exists(SettingsFilePath);
}
