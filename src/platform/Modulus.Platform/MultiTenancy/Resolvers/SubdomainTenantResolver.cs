using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.Resolvers;

public sealed class SubdomainTenantResolver(
    ITenantStore store,
    string baseDomain)
    : ITenantResolver
{
    /// <summary>
    /// Resolves the tenant from the subdomain left of the configured base
    /// domain. The comparison requires the dot boundary (<c>.{baseDomain}</c>,
    /// not just <c>{baseDomain}</c>) so a spoofed host like
    /// <c>attacker-modulus.app</c> cannot suffix-match its way into another
    /// tenant's slug, and the exact bare domain resolves to no tenant instead
    /// of throwing on an out-of-range slice.
    /// </summary>
    public Task<TenantInfo?> ResolveAsync(
        HttpContext ctx, CancellationToken ct)
    {
        var host = ctx.Request.Host.Host;

        if (string.IsNullOrWhiteSpace(baseDomain))
            return Task.FromResult<TenantInfo?>(null);

        // The bare domain itself hosts no tenant.
        if (host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<TenantInfo?>(null);

        var suffix = $".{baseDomain.TrimStart('.')}";
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<TenantInfo?>(null);

        var slug = host[..^suffix.Length];
        if (!IsValidSlug(slug))
            return Task.FromResult<TenantInfo?>(null);

        return store.FindBySlugAsync(slug, ct);
    }

    private static bool IsValidSlug(string slug)
    {
        if (slug.Length == 0 || slug.Length > 100)
            return false;

        // Reject leading/trailing/consecutive dots (empty labels in deep subdomains).
        if (slug.StartsWith('.') || slug.EndsWith('.') || slug.Contains(".."))
            return false;

        foreach (var ch in slug)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.'))
                return false;
        }

        return true;
    }
}
