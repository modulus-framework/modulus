using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Sharing;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IRecordShareStore"/>: shares survive a restart. Each query is also filtered by the company it names, so a
/// caller never sees another company's shares even from the host context.
/// </summary>
public sealed class EfRecordShareStore(IDbContextFactory<AuthorizationStoreDbContextBase> factory) : IRecordShareStore
{
    /// <inheritdoc />
    public async Task AddAsync(RecordShare share, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(share);
        await using var db = await factory.CreateDbContextAsync(ct);
        db.RecordShares.Add(ToRow(share));
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<RecordShare?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.RecordShares.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        return row is null ? null : ToShare(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordShare>> ForRecordAsync(Guid? tenantId, string resourceType, string resourceId, CancellationToken ct = default)
    {
        var company = tenantId ?? Guid.Empty;
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.RecordShares.AsNoTracking()
            .Where(s => s.TenantId == company && s.ResourceType == resourceType && s.ResourceId == resourceId)
            .ToListAsync(ct);
        return [.. rows.Select(ToShare)];
    }

    /// <inheritdoc />
    public async Task UpdateAsync(RecordShare share, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(share);
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.RecordShares.Where(s => s.Id == share.Id && s.TenantId == (share.TenantId ?? Guid.Empty))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, share.RevokedAt), ct);
    }

    private static RecordShareRow ToRow(RecordShare share) => new()
    {
        Id = share.Id,
        TenantId = share.TenantId ?? Guid.Empty,
        ResourceType = share.ResourceType,
        ResourceId = share.ResourceId,
        GranteeUserId = share.GranteeUserId,
        SharedByUserId = share.SharedByUserId,
        CreatedAt = share.CreatedAt,
        ExpiresAt = share.ExpiresAt,
        RevokedAt = share.RevokedAt,
    };

    private static RecordShare ToShare(RecordShareRow row) => new(
        row.Id,
        row.TenantId == Guid.Empty ? null : row.TenantId,
        row.ResourceType,
        row.ResourceId,
        row.GranteeUserId,
        row.SharedByUserId,
        row.CreatedAt,
        row.ExpiresAt,
        row.RevokedAt);
}
