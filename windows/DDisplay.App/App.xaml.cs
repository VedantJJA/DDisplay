using System.Windows;
using DDisplay.App.ViewModels;
using DDisplay.Core.Transport;
using DDisplay.VddControl;

namespace DDisplay.App;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainViewModel? _mainVm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mainVm = new MainViewModel(
            new VddXmlControlService(),
            new TransportManager());

        // Build tray icon.
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "DDisplay",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };

        // Load the icon from embedded resource.
        var iconStream = GetResourceStream(new Uri("Assets/app.ico", UriKind.Relative))?.Stream;
        if (iconStream is not null)
            _trayIcon.Icon = new System.Drawing.Icon(iconStream);

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        // Show the main window on first launch.
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null || !MainWindow.IsLoaded)
        {
            MainWindow = new Views.MainWindow { DataContext = _mainVm };
        }

        MainWindow.Show();
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
            if (_mainVm is not null)
                await _mainVm.ShutdownAsync();
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
