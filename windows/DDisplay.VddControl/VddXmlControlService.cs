using System.Diagnostics;
using System.Xml.Linq;
using DDisplay.VddControl.Models;

namespace DDisplay.VddControl;

/// <summary>
/// Controls the Virtual Display Driver by reading and writing vdd_settings.xml,
/// then triggering a driver reload via pnputil or device disable/enable.
///
/// TODO: Phase 0 must confirm which reload mechanism (pnputil restart-device, disable/enable
/// via Device Manager COM, or a VDC-internal signal) is correct and reliable. The
/// ReloadDriverAsync method currently uses pnputil as the primary attempt based on available
/// documentation. Update after Phase 0 findings.
/// </summary>
public sealed class VddXmlControlService : IVirtualDisplayService
{
    private readonly string _settingsFilePath;

    // TODO: Confirm the exact device instance ID from Phase 0 Device Manager inspection.
    // This value is a placeholder based on expected IddCx driver naming.
    private const string VddDeviceInstanceIdPrefix = "ROOT\\IDDSAMPL";

    public VddXmlControlService(string settingsFilePath = VddInstallChecker.SettingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public bool IsDriverInstalled => VddInstallChecker.IsFullyInstalled();

    public IReadOnlyList<MonitorEntry> GetMonitors()
    {
        if (!File.Exists(_settingsFilePath))
            return Array.Empty<MonitorEntry>();

        var doc = XDocument.Load(_settingsFilePath);
        return ParseMonitors(doc);
    }

    public async Task<int> AddOrUpdateMonitorAsync(MonitorEntry entry, CancellationToken cancellationToken = default)
    {
        var doc = File.Exists(_settingsFilePath)
            ? XDocument.Load(_settingsFilePath)
            : CreateEmptyDocument();

        var monitors = ParseMonitors(doc).ToList();

        // Find existing entry by index, or use the next available index.
        var existingIndex = monitors.FindIndex(m => m.Index == entry.Index);
        if (existingIndex >= 0)
            monitors[existingIndex] = entry;
        else
        {
            entry.Index = monitors.Count > 0 ? monitors.Max(m => m.Index) + 1 : 0;
            monitors.Add(entry);
        }

        WriteMonitors(doc, monitors);
        doc.Save(_settingsFilePath);

        await ReloadDriverAsync(cancellationToken);
        return entry.Index;
    }

    public async Task RemoveMonitorAsync(int index, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath)) return;

        var doc = XDocument.Load(_settingsFilePath);
        var monitors = ParseMonitors(doc).Where(m => m.Index != index).ToList();

        WriteMonitors(doc, monitors);
        doc.Save(_settingsFilePath);

        await ReloadDriverAsync(cancellationToken);
    }

    public async Task ReloadDriverAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Phase 0 must determine the correct reload mechanism. Trying pnputil first.
        // Alternative: disable/enable the device via Device Manager COM automation.
        // Alternative: check if VDC exposes a named-pipe command or file signal.

        var deviceId = await FindVddDeviceInstanceIdAsync(cancellationToken);
        if (deviceId is null)
        {
            throw new InvalidOperationException(
                "VDD device not found in the system. Is the Virtual Display Driver installed?");
        }

        await RunPnputilAsync($"/restart-device \"{deviceId}\"", cancellationToken);
    }

    // -- Private helpers --

    private static XDocument CreateEmptyDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("VirtualDisplayDriverSettings",
                new XElement("Monitors")));
    }

    private static List<MonitorEntry> ParseMonitors(XDocument doc)
    {
        var result = new List<MonitorEntry>();
        var monitorsEl = doc.Root?.Element("Monitors");
        if (monitorsEl is null) return result;

        int idx = 0;
        foreach (var el in monitorsEl.Elements("Monitor"))
        {
            result.Add(new MonitorEntry
            {
                Index = (int?)el.Attribute("index") ?? idx,
                WidthPx = (int?)el.Element("Width") ?? 1920,
                HeightPx = (int?)el.Element("Height") ?? 1080,
                RefreshRateHz = (int?)el.Element("RefreshRate") ?? 60,
                FriendlyName = (string?)el.Element("FriendlyName"),
                Enabled = ((string?)el.Attribute("enabled") ?? "true")
                    .Equals("true", StringComparison.OrdinalIgnoreCase),
            });
            idx++;
        }

        return result;
    }

    private static void WriteMonitors(XDocument doc, IEnumerable<MonitorEntry> monitors)
    {
        var monitorsEl = doc.Root?.Element("Monitors");
        if (monitorsEl is null)
        {
            monitorsEl = new XElement("Monitors");
            doc.Root!.Add(monitorsEl);
        }

        monitorsEl.RemoveAll();

        foreach (var m in monitors)
        {
            var el = new XElement("Monitor",
                new XAttribute("index", m.Index),
                new XAttribute("enabled", m.Enabled ? "true" : "false"),
                new XElement("Width", m.WidthPx),
                new XElement("Height", m.HeightPx),
                new XElement("RefreshRate", m.RefreshRateHz));

            if (!string.IsNullOrEmpty(m.FriendlyName))
                el.Add(new XElement("FriendlyName", m.FriendlyName));

            monitorsEl.Add(el);
        }
    }

    private static async Task<string?> FindVddDeviceInstanceIdAsync(CancellationToken cancellationToken)
    {
        // Use pnputil to enumerate all devices and look for the VDD hardware ID prefix.
        var output = await RunPnputilAsync("/enum-devices /connected", cancellationToken);
        var lines = output.Split('\n');

        string? currentInstanceId = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Instance ID:", StringComparison.OrdinalIgnoreCase))
                currentInstanceId = trimmed["Instance ID:".Length..].Trim();

            if (trimmed.StartsWith("Hardware IDs:", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains(VddDeviceInstanceIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return currentInstanceId;
            }
        }

        return null;
    }

    private static async Task<string> RunPnputilAsync(string args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("pnputil.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pnputil.exe");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(
                $"pnputil.exe exited with code {process.ExitCode}: {err}");
        }

        return output;
    }
}
