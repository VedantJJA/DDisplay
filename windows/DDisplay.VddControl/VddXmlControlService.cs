using System.Diagnostics;
using System.Xml.Linq;
using DDisplay.VddControl.Models;

namespace DDisplay.VddControl;

/// <summary>
/// Controls the Virtual Display Driver (VDD) by modifying vdd_settings.xml
/// and dynamically enabling/disabling the display with zero UAC popups when elevated.
/// </summary>
public sealed class VddXmlControlService : IVirtualDisplayService
{
    public const string DefaultSettingsPath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
    public const string VddDeviceInstanceId = @"ROOT\DISPLAY\0000";

    private readonly string _settingsFilePath;

    public VddXmlControlService(string settingsFilePath = DefaultSettingsPath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public bool IsDriverInstalled => VddInstallChecker.IsFullyInstalled();

    public bool IsDisplayEnabled => VddInstallChecker.IsDriverDevicePresent();

    public async Task EnableDisplayAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await RunPnputilAsync($"/enable-device \"{VddDeviceInstanceId}\"", cancellationToken);
            if (output.Contains("Failed to enable") || output.Contains("Access is denied"))
            {
                throw new InvalidOperationException($"pnputil failed: {output}");
            }
        }
        catch
        {
            await RunDriverScriptAsync("enable-display.bat", cancellationToken);
        }
    }

    public async Task DisableDisplayAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await RunPnputilAsync($"/disable-device \"{VddDeviceInstanceId}\"", cancellationToken);
            if (output.Contains("Failed to disable") || output.Contains("Access is denied"))
            {
                throw new InvalidOperationException($"pnputil failed: {output}");
            }
        }
        catch
        {
            await RunDriverScriptAsync("disable-display.bat", cancellationToken);
        }
    }

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
        try
        {
            var output = await RunPnputilAsync($"/restart-device \"{VddDeviceInstanceId}\"", cancellationToken);
            if (output.Contains("Failed to restart") || output.Contains("Access is denied"))
            {
                throw new InvalidOperationException($"pnputil failed: {output}");
            }
        }
        catch
        {
            await RunDriverScriptAsync("enable-display.bat", cancellationToken);
        }
    }

    // -- Private helpers --

    private static async Task RunDriverScriptAsync(string scriptName, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var scriptPath = Path.Combine(baseDir, @"..\..\..\..\..\driver", scriptName);
            var fullScriptPath = Path.GetFullPath(scriptPath);

            if (File.Exists(fullScriptPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{fullScriptPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
            }
        }, cancellationToken);
    }

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
            doc.Root?.Add(monitorsEl);
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

        return output;
    }
}
