using System.IO;
using System.Windows;
using DDisplay.App.ViewModels;
using DDisplay.Core.Transport;
using DDisplay.VddControl;

namespace DDisplay.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is null)
        {
            DataContext = new MainViewModel(
                new VddXmlControlService(),
                new TransportManager());
        }

        Loaded += (_, _) =>
        {
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        };
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing.
        e.Cancel = true;
        Hide();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow();
        settings.Owner = this;
        settings.ShowDialog();
    }

    private async void ToggleDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null)
        {
            await Vm.ToggleDisplayAsync();
        }
    }

    private void InstallVdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var batPath = Path.Combine(baseDir, @"..\..\..\..\..\driver\install-vdd.bat");
            var fullBatPath = Path.GetFullPath(batPath);

            if (File.Exists(fullBatPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullBatPath,
                    UseShellExecute = true,
                    Verb = "runas",
                });
                return;
            }
        }
        catch
        {
            // Fallback to web release page
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases",
            UseShellExecute = true,
        });
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null)
        {
            await Vm.ConnectAsync();
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null)
        {
            await Vm.DisconnectAsync();
        }
    }
}
