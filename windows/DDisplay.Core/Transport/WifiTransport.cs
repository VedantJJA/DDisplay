using System.Net;

namespace DDisplay.Core.Transport;

/// <summary>
/// Wi-Fi transport over LAN TCP. Listens on all interfaces for mDNS-discovered or
/// manually-entered Android connections.
/// </summary>
public sealed class WifiTransport : TcpLanTransport
{
    public const int DefaultPort = 7878;

    private readonly int _port;

    public WifiTransport(int port = DefaultPort)
    {
        _port = port;
    }

    public override string DisplayName => "Wi-Fi";

    protected override IPEndPoint GetListenEndPoint() =>
        new(IPAddress.Any, _port);
}
