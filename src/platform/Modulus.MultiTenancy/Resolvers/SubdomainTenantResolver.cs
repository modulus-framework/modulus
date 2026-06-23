using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.Resolvers;

public sealed class SubdomainTenantResolver(
    ITenantStore store,
    string       baseDomain)
    : ITenantResolver
{
    public Task<TenantInfo?> ResolveAsync(
        HttpContext ctx, CancellationToken ct)
    {
    var host = ctx.Request.Host.Host;
    if (!host.EndsWith(baseDomain,
            StringComparison.OrdinalIgnoreCase))
        return Task.FromResult<TenantInfo?>(null);

    var slug = host[..^(baseDomain.Length + 1)];
    return store.FindBySlugAsync(slug, ct);
    }
}