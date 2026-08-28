using System.Windows;
using DDisplay.App.ViewModels;
using DDisplay.Core.Transport;

namespace DDisplay.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
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

    private void InstallVdd_Click(object sender, RoutedEventArgs e)
    {
        // Open the VDC GitHub releases page instructions in the default browser.
        // TODO: Optionally bundle the VDC installer and invoke it directly.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases",
            UseShellExecute = true,
        });
    }

    private async void ForceUsb_Click(object sender, RoutedEventArgs e)
    {
        // TODO: wire through MainViewModel when TransportManager exposes ForceTransportAsync.
        await Task.CompletedTask;
    }

    private async void ForceWifi_Click(object sender, RoutedEventArgs e)
    {
        await Task.CompletedTask;
    }
}
