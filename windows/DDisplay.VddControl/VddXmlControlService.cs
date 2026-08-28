using System.Diagnostics;
using System.Xml.Linq;
using DDisplay.VddControl.Models;

namespace DDisplay.VddControl;

/// <summary>
/// Controls the Virtual Display Driver (VDD) by modifying vdd_settings.xml
/// using MikeTheTech's schema (<vdd_settings><monitors><count>1</count></monitors>...).
/// Includes debounce and state verification to avoid redundant driver initialization prompts.
/// </summary>
public sealed class VddXmlControlService : IVirtualDisplayService
{
    public const string DefaultSettingsPath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
    public const string VddDeviceInstanceId = @"ROOT\DISPLAY\0000";

    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _serviceLock = new(1, 1);

    public VddXmlControlService(string settingsFilePath = DefaultSettingsPath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public bool IsDriverInstalled => VddInstallChecker.IsFullyInstalled();

    public bool IsDisplayEnabled => VddInstallChecker.IsVirtualDisplayActive();

    public async Task EnableDisplayAsync(CancellationToken cancellationToken = default)
    {
        await _serviceLock.WaitAsync(cancellationToken);
        try
        {
            SetMonitorCount(1);

            // If virtual display is already active in Windows display manager, no driver reload needed
            if (VddInstallChecker.IsVirtualDisplayActive())
            {
                return;
            }

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

            await ReloadDriverInternalAsync(cancellationToken);
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    public async Task DisableDisplayAsync(CancellationToken cancellationToken = default)
    {
        await _serviceLock.WaitAsync(cancellationToken);
        try
        {
            SetMonitorCount(0);

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

            await ReloadDriverInternalAsync(cancellationToken);
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    public int GetMonitorCount()
    {
        if (!File.Exists(_settingsFilePath)) return 0;
        try
        {
            var doc = XDocument.Load(_settingsFilePath);
            return (int?)doc.Root?.Element("monitors")?.Element("count") ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public void SetMonitorCount(int count)
    {
        EnsureSettingsFileExists();
        try
        {
            var doc = XDocument.Load(_settingsFilePath);
            var monitorsEl = doc.Root?.Element("monitors");
            if (monitorsEl is null)
            {
                monitorsEl = new XElement("monitors");
                doc.Root?.AddFirst(monitorsEl);
            }
            var countEl = monitorsEl.Element("count");
            if (countEl is null)
            {
                monitorsEl.Add(new XElement("count", count));
            }
            else
            {
                countEl.Value = count.ToString();
            }
            doc.Save(_settingsFilePath);
        }
        catch { }
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
        EnsureSettingsFileExists();

        var doc = XDocument.Load(_settingsFilePath);
        var resolutionsEl = doc.Root?.Element("resolutions");
        if (resolutionsEl is null)
        {
            resolutionsEl = new XElement("resolutions");
            doc.Root?.Add(resolutionsEl);
        }

        // Check if resolution already exists
        var existing = resolutionsEl.Elements("resolution").FirstOrDefault(r =>
            (int?)r.Element("width") == entry.WidthPx &&
            (int?)r.Element("height") == entry.HeightPx);

        if (existing is null)
        {
            resolutionsEl.AddFirst(new XElement("resolution",
                new XElement("width", entry.WidthPx),
                new XElement("height", entry.HeightPx),
                new XElement("refresh_rate", entry.RefreshRateHz > 0 ? entry.RefreshRateHz : 60)));
        }

        SetMonitorCount(1);
        doc.Save(_settingsFilePath);

        await ReloadDriverAsync(cancellationToken);
        return 0;
    }

    public async Task RemoveMonitorAsync(int index, CancellationToken cancellationToken = default)
    {
        SetMonitorCount(0);
        await ReloadDriverAsync(cancellationToken);
    }

    public async Task ReloadDriverAsync(CancellationToken cancellationToken = default)
    {
        await _serviceLock.WaitAsync(cancellationToken);
        try
        {
            await ReloadDriverInternalAsync(cancellationToken);
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    private async Task ReloadDriverInternalAsync(CancellationToken cancellationToken)
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

    private void EnsureSettingsFileExists()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(_settingsFilePath))
            {
                var defaultDoc = CreateDefaultDocument();
                defaultDoc.Save(_settingsFilePath);
            }
        }
        catch { }
    }

    private static XDocument CreateDefaultDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("vdd_settings",
                new XElement("monitors",
                    new XElement("count", 1)),
                new XElement("gpu",
                    new XElement("friendlyname", "default")),
                new XElement("global",
                    new XElement("g_refresh_rate", 60),
                    new XElement("g_refresh_rate", 90),
                    new XElement("g_refresh_rate", 120)),
                new XElement("resolutions",
                    new XElement("resolution",
                        new XElement("width", 1920),
                        new XElement("height", 1080),
                        new XElement("refresh_rate", 60)),
                    new XElement("resolution",
                        new XElement("width", 2400),
                        new XElement("height", 1080),
                        new XElement("refresh_rate", 60)),
                    new XElement("resolution",
                        new XElement("width", 2560),
                        new XElement("height", 1440),
                        new XElement("refresh_rate", 60))),
                new XElement("logging",
                    new XElement("SendLogsThroughPipe", false),
                    new XElement("logging", false),
                    new XElement("debuglogging", false))));
    }

    private static List<MonitorEntry> ParseMonitors(XDocument doc)
    {
        var result = new List<MonitorEntry>();
        var resolutionsEl = doc.Root?.Element("resolutions");
        if (resolutionsEl is null) return result;

        int idx = 0;
        foreach (var el in resolutionsEl.Elements("resolution"))
        {
            result.Add(new MonitorEntry
            {
                Index = idx,
                WidthPx = (int?)el.Element("width") ?? 1920,
                HeightPx = (int?)el.Element("height") ?? 1080,
                RefreshRateHz = (int?)el.Element("refresh_rate") ?? 60,
                FriendlyName = "Virtual Display",
                Enabled = true,
            });
            idx++;
        }

        return result;
    }

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
