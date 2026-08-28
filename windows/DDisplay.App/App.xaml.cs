using System.Windows;
using DDisplay.App.ViewModels;

namespace DDisplay.App;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            Shutdown();
        };
        menu.Items.Add(quitItem);

        return menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
