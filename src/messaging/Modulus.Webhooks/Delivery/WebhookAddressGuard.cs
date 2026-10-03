namespace Modulus.Webhooks;

using System.Net;
using System.Net.Http;
using System.Net.Sockets;

/// <summary>
/// Keeps deliveries off the server's own network (SSRF). A subscriber picks the URL, so it could otherwise point it at
/// <c>localhost</c>, a cloud metadata endpoint or an internal service. The check runs on the resolved address at connect
/// time, so a public name that resolves (or later re-resolves) to an internal address is refused as well.
/// </summary>
internal static class WebhookAddressGuard
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
    /// A <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host, drops non-public addresses and connects
    /// to the remaining ones; when none is left the request fails with <see cref="WebhookAddressRejectedException"/>.
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
            throw new WebhookAddressRejectedException(endPoint.Host);

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
}

/// <summary>A webhook endpoint resolved only to non-public addresses (see <c>Webhooks:AllowPrivateNetworks</c>).</summary>
public sealed class WebhookAddressRejectedException(string host)
    : HttpRequestException($"'{host}' does not resolve to a public address; webhooks are not delivered to private networks.");

/// <summary>The checks a subscription URL must pass when it is saved (the connect-time guard still applies on every delivery).</summary>
internal static class WebhookUrlRules
{
    public static string? Validate(string? url, ModulusWebhooksOptions options)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Must be an absolute URL.";
        if (url.Length > 2048)
            return "Must be at most 2048 characters.";
        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowHttp && uri.Scheme == Uri.UriSchemeHttp))
            return options.AllowHttp ? "Must be an http or https URL." : "Must be an https URL.";
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "Must not contain credentials.";
        if (!string.IsNullOrEmpty(uri.Fragment))
            return "Must not contain a fragment.";
        if (!options.AllowPrivateNetworks)
        {
            if (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                return "Must not point at this machine.";
            if (System.Net.IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var ip) && !WebhookAddressGuard.IsPublic(ip))
                return "Must not point at a private or reserved address.";
        }

        return null;
    }
}
