using System.Net;
using System.Net.Sockets;

namespace Neura.Web.Networking;

/// <summary>
/// Socket connector for user-supplied reference URLs. DNS is resolved at
/// connect time and the socket is opened only to a globally routable address,
/// preventing private-network / loopback SSRF and DNS-rebinding bypasses.
/// Redirects are disabled on the corresponding named HttpClient.
/// </summary>
public static class PublicInternetSocketConnector
{
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var address = addresses.FirstOrDefault(IsGloballyRoutable)
            ?? throw new HttpRequestException("Reference URL resolved to a non-public network address.");

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsGloballyRoutable(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0];
            var b = bytes[1];
            var c = bytes[2];

            return !(a == 0 || a == 10 || a == 127 || a >= 224 ||
                     (a == 100 && b is >= 64 and <= 127) ||
                     (a == 169 && b == 254) ||
                     (a == 172 && b is >= 16 and <= 31) ||
                     (a == 192 && (b == 0 || b == 168)) ||
                     (a == 192 && b == 88 && c == 99) ||
                     (a == 192 && b == 175 && c == 48) ||
                     (a == 198 && b is 18 or 19) ||
                     (a == 198 && b == 51 && c == 100) ||
                     (a == 203 && b == 0 && c == 113));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
            return false;

        // Only permit IPv6 global-unicast space (2000::/3), excluding the
        // documentation-only 2001:db8::/32 prefix.
        var isGlobalUnicast = (bytes[0] & 0xE0) == 0x20;
        var isDocumentationPrefix = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
        return isGlobalUnicast && !isDocumentationPrefix;
    }
}
