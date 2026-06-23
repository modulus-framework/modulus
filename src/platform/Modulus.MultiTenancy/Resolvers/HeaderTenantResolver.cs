using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.Resolvers;

public sealed class HeaderTenantResolver(
    ITenantStore store,
    string       headerName = "X-Tenant-Id")
    : ITenantResolver
{
    public Task<TenantInfo?> ResolveAsync(
        HttpContext ctx, CancellationToken ct)
    {
        var value = ctx.Request.Headers[headerName].FirstOrDefault();
        if (value is null) return Task.FromResult<TenantInfo?>(null);

        return Guid.TryParse(value, out var id)
            ? store.FindByIdAsync(id, ct)
            : store.FindBySlugAsync(value, ct);
    }
}