using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Provisioning surface for the EF-backed tenant store: create tenants and toggle
/// their active state. Registered as a scoped service by
/// <c>AddEfCoreTenantStore</c>. Reads go through <see cref="ITenantStore"/> /
/// <see cref="EfTenantStore"/>; this is the write side, used by admin endpoints or
/// seed code.
/// </summary>
public sealed class TenantManager(TenantStoreDbContext db)
{
    /// <summary>
    /// Creates a new active tenant. Throws
    /// <see cref="InvalidOperationException"/> if <paramref name="slug"/> is already
    /// taken (also enforced by a unique index at the database level).
    /// </summary>
    public async Task<TenantInfo> CreateAsync(
        string slug,
        string? displayName = null,
        Guid? id = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            throw new InvalidOperationException(
                $"A tenant with slug '{slug}' already exists.");

        var entity = new TenantEntity
        {
            Id = id ?? Guid.NewGuid(),
            Slug = slug,
            DisplayName = displayName,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Tenants.Add(entity);
        await db.SaveChangesAsync(ct);
        return new TenantInfo(entity.Id, entity.Slug, entity.DisplayName);
    }

    /// <summary>
    /// Sets a tenant's active flag. A deactivated tenant stops resolving
    /// immediately (see <see cref="EfTenantStore"/>). Returns
    /// <see langword="false"/> if no tenant has the given id.
    /// </summary>
    public async Task<bool> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken ct = default)
    {
        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;

        entity.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
