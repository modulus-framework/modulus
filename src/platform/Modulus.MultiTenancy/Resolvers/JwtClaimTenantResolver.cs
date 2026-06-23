using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.Resolvers;

public sealed class JwtClaimTenantResolver(
    ITenantStore store,
    string       claimType = "tid")
    : ITenantResolver
{
    public Task<TenantInfo?> ResolveAsync(
        HttpContext ctx, CancellationToken ct)
    {
        var claim = ctx.User.FindFirst(claimType)?.Value;
        if (claim is null) return Task.FromResult<TenantInfo?>(null);

        return Guid.TryParse(claim, out var id)
            ? store.FindByIdAsync(id, ct)
            : store.FindBySlugAsync(claim, ct);
    }
}