using System.Windows;
using DDisplay.App.ViewModels;
using DDisplay.Core.Transport;
using DDisplay.VddControl;

namespace DDisplay.App;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainViewModel? _mainVm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show(
                $"An unhandled error occurred: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "DDisplay Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _mainVm = new MainViewModel(
                new VddXmlControlService(),
                new TransportManager());

            // Build tray icon with safe fallback
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "DDisplay",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu(),
            };

            try
            {
                var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute))?.Stream;
                if (iconStream is not null)
                {
                    _trayIcon.Icon = new System.Drawing.Icon(iconStream);
                }
                else
                {
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

            // Show the main window on startup
            ShowMainWindow();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to start DDisplay: {ex.Message}\n\n{ex.StackTrace}",
                "DDisplay Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null || !MainWindow.IsLoaded)
        {
            MainWindow = new Views.MainWindow { DataContext = _mainVm };
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
