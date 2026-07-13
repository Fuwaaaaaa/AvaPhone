using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VrcPhoneRelay.Server.Ui;

/// <summary>QRコードに載せるLAN側IPアドレスの選定。</summary>
public static class NetworkInfo
{
    /// <summary>
    /// 既定ルートに向けたUDPソケットのローカルアドレスからLAN IPを推定する。
    /// 失敗時はインターフェース列挙にフォールバックし、それも無ければループバック。
    /// </summary>
    public static IPAddress GetLanAddress()
    {
        try
        {
            // 実際にパケットは送らない。ルーティング解決だけに使う
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
            {
                return ep.Address;
            }
        }
        catch (SocketException)
        {
            // オフライン等。フォールバックへ
        }

        return ListCandidates().FirstOrDefault() ?? IPAddress.Loopback;
    }

    /// <summary>稼働中インターフェースのIPv4アドレス候補(複数NIC時の選択肢)。</summary>
    public static IReadOnlyList<IPAddress> ListCandidates()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(addr => addr.Address)
                .Where(ip => !IPAddress.IsLoopback(ip))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
