using DDisplay.VddControl;
using DDisplay.VddControl.Models;

namespace DDisplay.Tests.VddControl;

/// <summary>
/// In-memory IVirtualDisplayService for unit testing components that depend on it.
/// </summary>
public sealed class MockVirtualDisplayService : IVirtualDisplayService
{
    private readonly List<MonitorEntry> _monitors = new();

    public bool IsDriverInstalled { get; set; } = true;

    public IReadOnlyList<MonitorEntry> GetMonitors() => _monitors.AsReadOnly();

    public Task<int> AddOrUpdateMonitorAsync(MonitorEntry entry, CancellationToken cancellationToken = default)
    {
        var existing = _monitors.FindIndex(m => m.Index == entry.Index);
        if (existing >= 0)
            _monitors[existing] = entry;
        else
        {
            entry.Index = _monitors.Count;
            _monitors.Add(entry);
        }
        return Task.FromResult(entry.Index);
    }

    public Task RemoveMonitorAsync(int index, CancellationToken cancellationToken = default)
    {
        _monitors.RemoveAll(m => m.Index == index);
        return Task.CompletedTask;
    }

    public Task ReloadDriverAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
