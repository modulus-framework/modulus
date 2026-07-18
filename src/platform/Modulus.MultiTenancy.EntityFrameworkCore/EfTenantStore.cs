using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantStore"/> backed by <see cref="TenantStoreDbContext"/>.
/// Only <b>active</b> tenants resolve — a deactivated tenant (or an unknown
/// id/slug) returns <see langword="null"/>, which the resolvers treat as
/// "no tenant", keeping the pipeline fail-closed.
/// </summary>
public sealed class EfTenantStore(TenantStoreDbContext db) : ITenantStore
{
    public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct);
        return Map(entity);
    }

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        var entity = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive, ct);
        return Map(entity);
    }

    private static TenantInfo? Map(TenantEntity? entity)
        => entity is null
            ? null
            : new TenantInfo(entity.Id, entity.Slug, entity.DisplayName);
}
