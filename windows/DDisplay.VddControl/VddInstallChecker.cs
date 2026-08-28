using Microsoft.Win32;

namespace DDisplay.VddControl;

/// <summary>
/// Checks whether the Virtual Display Driver and its settings file are present.
/// </summary>
public static class VddInstallChecker
{
    public const string SettingsFilePath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
    public const string DriverDirectory = @"C:\VirtualDisplayDriver";

    // WMI hardware ID prefix used by the VDD (IddSampleDriver-based).
    // TODO: Confirm exact hardware ID from Phase 0 Device Manager inspection.
    private const string VddHardwareIdPrefix = "Root\\IddSampleDriver";

    /// <summary>
    /// Returns true if the VDD settings file exists on disk.
    /// This is the minimum check for whether VDD was ever installed.
    /// </summary>
    public static bool IsSettingsFilePresent() =>
        File.Exists(SettingsFilePath);

    /// <summary>
    /// Checks if the VDD device is present in the system device tree via the registry.
    /// A more reliable signal than just the settings file.
    /// </summary>
    public static bool IsDriverDevicePresent()
    {
        try
        {
            const string enumKey = @"SYSTEM\CurrentControlSet\Enum";
            using var hklm = Registry.LocalMachine.OpenSubKey(enumKey);
            if (hklm is null) return false;

            // Walk Root\ subkeys looking for the VDD hardware ID.
            using var rootKey = hklm.OpenSubKey("Root");
            if (rootKey is null) return false;

            foreach (var subName in rootKey.GetSubKeyNames())
            {
                if (subName.StartsWith("IDDSAMPL", StringComparison.OrdinalIgnoreCase) ||
                    subName.StartsWith("IddSample", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // If we cannot read the registry, assume not installed.
            return false;
        }
    }

    /// <summary>
    /// Returns a composite check: settings file AND device present.
    /// </summary>
    public static bool IsFullyInstalled() =>
        IsSettingsFilePresent() && IsDriverDevicePresent();
}
