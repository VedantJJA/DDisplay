using System.Diagnostics;
using System.IO;
using System.Windows;
using DDisplay.App.ViewModels;

namespace DDisplay.App;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register process exit and unhandled exception hooks to auto-disconnect virtual display
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupDisplay();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => CleanupDisplay();
        DispatcherUnhandledException += (_, _) => CleanupDisplay();

        try
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "DDisplay",
                Visible = true,
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenuStrip = BuildTrayMenu(),
            };

            _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        }
        catch
        {
            // Tray icon is a non-critical feature; continue if unavailable
        }
    }

    public void ShowMainWindow()
    {
        if (MainWindow is null || !MainWindow.IsLoaded)
        {
            MainWindow = new Views.MainWindow();
        }

        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }
        MainWindow.Activate();
    }

    private System.Windows.Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var showItem = new System.Windows.Forms.ToolStripMenuItem("Open DDisplay");
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quit");
        quitItem.Click += async (_, _) =>
        {
            if (MainWindow?.DataContext is MainViewModel vm)
            {
                await vm.ShutdownAsync();
            }
            _trayIcon?.Dispose();
            CleanupDisplay();
            Shutdown();
        };
        menu.Items.Add(quitItem);

        return menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        CleanupDisplay();
        base.OnExit(e);
    }

    private static void CleanupDisplay()
    {
        try
        {
            // 1. Direct XML zeroing (fastest & most reliable)
            const string settingsPath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
            if (File.Exists(settingsPath))
            {
                try
                {
                    var doc = System.Xml.Linq.XDocument.Load(settingsPath);
                    var countEl = doc.Root?.Element("monitors")?.Element("count");
                    if (countEl != null)
                    {
                        countEl.Value = "0";
                        doc.Save(settingsPath);
                    }
                }
                catch { }
            }

            // 2. Direct pnputil disable
            try
            {
                var psi = new ProcessStartInfo("pnputil.exe", "/disable-device \"ROOT\\DISPLAY\\0000\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            catch { }

            // 3. Fallback to disable script
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var batPath = Path.Combine(baseDir, @"..\..\..\..\..\driver\disable-display.bat");
            var fullPath = Path.GetFullPath(batPath);
            if (File.Exists(fullPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{fullPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
