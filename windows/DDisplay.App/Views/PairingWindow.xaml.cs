using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace DDisplay.App.Views;

public partial class PairingWindow : Window
{
    private readonly string _sessionId;
    private readonly string _pairingCode;
    private readonly int _port;

    public PairingWindow(string sessionId, string pairingCode, int port = 7878)
    {
        InitializeComponent();
        _sessionId = sessionId;
        _pairingCode = pairingCode;
        _port = port;

        PairingCodeText.Text = pairingCode;
        IpAddressBox.Text = GetLocalIpAddress();
        GenerateQrCode();
    }

    private void GenerateQrCode()
    {
        // Encode a URI the Android app can scan to pre-fill host/port/code.
        var uri = $"ddisplay://pair?host={IpAddressBox.Text}&port={_port}&code={_pairingCode}&session={_sessionId}";
        using var qr = new QRCodeGenerator();
        var data = qr.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        using var code = new BitmapByteQRCode(data);
        var bytes = code.GetGraphic(6, new byte[] { 79, 142, 247 }, new byte[] { 26, 29, 39 });

        using var stream = new System.IO.MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        QrImage.Source = bitmap;
    }

    private static string GetLocalIpAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }

        return "127.0.0.1";
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
