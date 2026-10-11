namespace Modulus.Networking;

using System.Net;
using System.Net.Http;
using System.Net.Sockets;

/// <summary>
/// Keeps outbound calls off the server's own network (SSRF). A URL that an external party registers (a webhook subscriber, a
/// back-channel logout address) could otherwise point at <c>localhost</c>, a cloud metadata endpoint or an internal service.
/// The check runs on the resolved address at connect time, so a public name that resolves (or later re-resolves) to an internal
/// address is refused as well. Used by Modulus.Webhooks and Modulus.Identity.
/// </summary>
public static class OutboundAddressGuard
{
    /// <summary>Whether <paramref name="address"/> is a public unicast address.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                   // 0.0.0.0/8 "this network"
                || b[0] == 10                                    // 10.0.0.0/8 private
                || (b[0] == 100 && b[1] is >= 64 and <= 127)     // 100.64.0.0/10 carrier-grade NAT
                || b[0] == 127                                   // loopback
                || (b[0] == 169 && b[1] == 254)                  // link-local (cloud metadata)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)      // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)  // IETF protocol assignments, TEST-NET-1
                || (b[0] == 192 && b[1] == 168)                  // 192.168.0.0/16 private
                || (b[0] == 198 && b[1] is 18 or 19)             // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // TEST-NET-3
                || b[0] >= 224);                                 // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Loopback)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return false;

            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC)                           // fc00::/7 unique local
                return false;
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) // 2001:db8::/32 documentation
                return false;
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) // 64:ff9b::/96 NAT64: check the IPv4 inside
                return IsPublic(new IPAddress(b[12..16]));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a URI's host names this machine or a non-public address, judged without DNS: <c>localhost</c> and literal
    /// private or reserved IPs. A public name passes here and is checked at connect time by <see cref="ConnectAsync"/>.
    /// </summary>
    public static bool IsReservedHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var ip) && !IsPublic(ip);
    }

    /// <summary>
    /// A <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host, drops non-public addresses and connects
    /// to the remaining ones; when none is left the request fails with <see cref="OutboundAddressRejectedException"/>.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var endPoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, ct).ConfigureAwait(false);

        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            throw new OutboundAddressRejectedException(endPoint.Host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, endPoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>A handler for outbound calls that must not reach private networks: no proxy (it would connect unchecked), no redirects.</summary>
    public static SocketsHttpHandler CreateCheckedHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConnectAsync,
    };
}

/// <summary>An outbound call whose host resolved only to non-public addresses (see <see cref="OutboundAddressGuard"/>).</summary>
public sealed class OutboundAddressRejectedException(string host)
    : HttpRequestException($"'{host}' does not resolve to a public address; the call is not sent to private networks.")
{
    /// <summary>The host that was refused.</summary>
    public string Host { get; } = host;
}
