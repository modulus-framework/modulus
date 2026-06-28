using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy;

/// <summary>
/// Resolves the tenant for the current HTTP request.
/// Registered implementations are tried in order; the first non-null wins.
/// </summary>
public interface ITenantResolver
{
    Task<TenantInfo?> ResolveAsync(
        HttpContext ctx,
        CancellationToken ct = default);
}

/// <summary>Lookup of tenants by id / slug, backed by a module DbContext.</summary>
public interface ITenantStore
{
    Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct);
}
