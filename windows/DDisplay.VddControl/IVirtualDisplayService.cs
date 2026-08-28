using DDisplay.VddControl.Models;

namespace DDisplay.VddControl;

/// <summary>
/// Abstraction over the Virtual Display Driver control layer.
/// Implementations: VddXmlControlService (real), MockVirtualDisplayService (tests).
/// </summary>
public interface IVirtualDisplayService
{
    /// <summary>
    /// Returns true if the VDD driver is installed and the settings file is accessible.
    /// </summary>
    bool IsDriverInstalled { get; }

    /// <summary>
    /// Reads all monitor entries currently in vdd_settings.xml.
    /// </summary>
    IReadOnlyList<MonitorEntry> GetMonitors();

    /// <summary>
    /// Adds or updates a virtual monitor entry in vdd_settings.xml and triggers the
    /// driver to pick up the change.
    /// </summary>
    /// <param name="entry">The monitor entry to add or update.</param>
    /// <returns>The index assigned to this monitor entry.</returns>
    Task<int> AddOrUpdateMonitorAsync(MonitorEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a virtual monitor entry and reloads the driver.
    /// </summary>
    Task RemoveMonitorAsync(int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers the VDD driver to reload vdd_settings.xml and apply any pending changes.
    /// The exact mechanism is determined by Phase 0 findings (pnputil, signal file, etc.).
    /// </summary>
    Task ReloadDriverAsync(CancellationToken cancellationToken = default);
}
