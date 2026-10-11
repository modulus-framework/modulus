namespace Modulus.Webhooks;

using System.Net;
using Modulus.Networking;

/// <summary>The checks a subscription URL must pass when it is saved (the connect-time guard in <see cref="OutboundAddressGuard"/> still applies on every delivery).</summary>
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
            if (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var ip) && !OutboundAddressGuard.IsPublic(ip))
                return "Must not point at a private or reserved address.";
        }

        return null;
    }
}
