using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Modulus.AspNetCore.Security;

/// <summary>
/// Decides which proxies may set <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>. Trusting every sender (clearing the
/// known-proxy lists) lets any caller pick the client address the app sees: it defeats the per-IP rate limit on the sign-in
/// endpoints and forges the address in audit records.
/// </summary>
public static class TrustedProxiesExtensions
{
    /// <summary>The configuration section: <c>KnownProxies</c> (addresses), <c>KnownNetworks</c> (CIDR) and <c>ForwardLimit</c>.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Trusts only the proxies and networks named in <c>ForwardedHeaders:KnownProxies</c> / <c>KnownNetworks</c>. With none
    /// configured the framework default stays (loopback only), so a forwarded header from anywhere else is ignored and the
    /// connection's own address is used. <c>ForwardLimit</c> (default 1) is how many proxy hops are honoured.
    /// </summary>
    public static ForwardedHeadersOptions ApplyModulusTrustedProxies(
        this ForwardedHeadersOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException($"{SectionName}:KnownProxies contains '{proxy}', which is not an IP address.");
            options.KnownProxies.Add(address);
        }

        foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
#if NET10_0_OR_GREATER
            if (!IPNetwork.TryParse(network, out var parsed))
                throw new InvalidOperationException($"{SectionName}:KnownNetworks contains '{network}', which is not a CIDR network such as 10.0.0.0/8.");
            options.KnownIPNetworks.Add(parsed);
#else
            var slash = network.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0 || !IPAddress.TryParse(network[..slash], out var prefix) || !int.TryParse(network[(slash + 1)..], out var length))
                throw new InvalidOperationException($"{SectionName}:KnownNetworks contains '{network}', which is not a CIDR network such as 10.0.0.0/8.");
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
#endif
        }

        options.ForwardLimit = section.GetValue("ForwardLimit", 1);
        return options;
    }
}
