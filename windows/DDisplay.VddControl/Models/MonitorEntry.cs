namespace DDisplay.VddControl.Models;

/// <summary>
/// Represents a single virtual monitor entry in vdd_settings.xml.
/// </summary>
public sealed class MonitorEntry
{
    /// <summary>Unique monitor index within the VDD settings file (0-based).</summary>
    public int Index { get; set; }

    /// <summary>Monitor width in pixels.</summary>
    public int WidthPx { get; set; }

    /// <summary>Monitor height in pixels.</summary>
    public int HeightPx { get; set; }

    /// <summary>Refresh rate in Hz.</summary>
    public int RefreshRateHz { get; set; }

    /// <summary>Optional friendly name shown in Display Settings.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>Whether this entry is currently enabled in the driver.</summary>
    public bool Enabled { get; set; } = true;

    public override string ToString() =>
        $"{WidthPx}x{HeightPx}@{RefreshRateHz}Hz (index={Index}, enabled={Enabled})";
}
