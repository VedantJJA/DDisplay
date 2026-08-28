using DDisplay.VddControl.Models;

namespace DDisplay.VddControl;

/// <summary>
/// Abstraction over the Virtual Display Driver control layer.
/// Implementations: VddXmlControlService (real), MockVirtualDisplayService (tests).
/// </summary>
public interface IVirtualDisplayService
{
    /// <summary>
    /// Returns true if the VDD driver is installed and recognized by the system.
    /// </summary>
    bool IsDriverInstalled { get; }

    /// <summary>
    /// Returns true if the virtual display output is currently active/enabled.
    /// </summary>
    bool IsDisplayEnabled { get; }

    /// <summary>
    /// Enables the virtual display device so Windows activates the extended monitor.
    /// </summary>
    Task EnableDisplayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables the virtual display device so Windows disconnects the extended monitor.
    /// </summary>
    Task DisableDisplayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all monitor entries currently in vdd_settings.xml.
    /// </summary>
    IReadOnlyList<MonitorEntry> GetMonitors();

    /// <summary>
    /// Adds or updates a virtual monitor entry in vdd_settings.xml and triggers the
    /// driver to pick up the change.
    /// </summary>
    Task<int> AddOrUpdateMonitorAsync(MonitorEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a virtual monitor entry and reloads the driver.
    /// </summary>
    Task RemoveMonitorAsync(int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers the VDD driver to reload vdd_settings.xml and apply any pending changes.
    /// </summary>
    Task ReloadDriverAsync(CancellationToken cancellationToken = default);
}
