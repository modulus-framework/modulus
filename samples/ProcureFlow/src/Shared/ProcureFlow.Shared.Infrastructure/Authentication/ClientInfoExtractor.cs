using Microsoft.AspNetCore.Http;

namespace ProcureFlow.Shared.Infrastructure.Authentication;

/// <summary>
/// Resolves client identity (User-Agent and IP) from an <see cref="HttpContext"/>, checking
/// standard headers first and falling back to common forwarded/variant header names used by
/// reverse proxies, CDNs and server-side renderers (e.g. Next.js) that may not forward the
/// standard <c>User-Agent</c>.
/// </summary>
internal static class ClientInfoExtractor
{
    // Standard first, then common non-standard variants some proxies/SSR frameworks use.
    private static readonly string[] UserAgentHeaders =
    [
        "User-Agent",
        "X-Forwarded-User-Agent",
        "X-User-Agent",
        "X-Original-User-Agent",
        "Original-User-Agent"
    ];

    /// <summary>
    /// Returns the first non-empty User-Agent found across known headers, or null.
    /// </summary>
    public static string? GetUserAgent(HttpContext context)
    {
        foreach (string header in UserAgentHeaders)
        {
            string? value = context.Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the real client IP address. Checks forwarded/proxy headers first
    /// (before <see cref="ConnectionInfo.RemoteIpAddress"/>) because in containerised
    /// environments (Docker, Kubernetes) RemoteIpAddress is the container-network
    /// gateway, not the actual client.
    /// </summary>
    public static string? GetClientIpAddress(HttpContext context)
    {
        // 1. X-Forwarded-For: client, proxy1, proxy2, ... — take the first (original client)
        string? xff = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            return xff.Split(',')[0].Trim();
        }

        // 2. X-Real-IP (nginx)
        string? realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(realIp))
        {
            return realIp.Trim();
        }

        // 3. CF-Connecting-IP (Cloudflare)
        string? cfIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(cfIp))
        {
            return cfIp.Trim();
        }

        // 4. RFC 7239 Forwarded header (for=...)
        string? forwarded = context.Request.Headers["Forwarded"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            string? parsed = ParseForwardedFor(forwarded);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        // 5. Fall back to RemoteIpAddress (correct when no proxy is in front,
        //    or when ForwardedHeaders middleware has already rewritten it).
        string? remote = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(remote))
        {
            return remote;
        }

        return null;
    }

    private static string? ParseForwardedFor(string forwarded)
    {
        foreach (string part in forwarded.Split(';', ',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("for=", StringComparison.OrdinalIgnoreCase))
            {
                string value = part[4..].Trim('"', ' ', '[', ']');
                return value;
            }
        }

        return null;
    }
}
