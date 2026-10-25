using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// The EF Core store of explicit parent-to-child authorities (decision 6). A short-lived context per call, and no cache: the check
/// that consults it must see a revocation on the very next request.
/// </summary>
public sealed class EfCompanyAuthorityStore(IDbContextFactory<AuthorizationStoreDbContextBase> factory) : ICompanyAuthorityStore
{
    /// <inheritdoc />
    public async Task<bool> HasActiveGrantAsync(Guid parentId, Guid childId, Guid principalId, CompanyOperation operation, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.CompanyAuthorities.AnyAsync(a =>
            a.ParentId == parentId && a.ChildId == childId && a.PrincipalId == principalId
            && a.Operation == operation && a.RevokedAt == null, ct);
    }

    /// <inheritdoc />
    public async Task<CompanyAuthorityGrant> GrantAsync(Guid parentId, Guid childId, Guid principalId, CompanyOperation operation, Guid? grantedBy, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.CompanyAuthorities.FirstOrDefaultAsync(a =>
            a.ParentId == parentId && a.ChildId == childId && a.PrincipalId == principalId
            && a.Operation == operation && a.RevokedAt == null, ct);
        if (existing is not null)
            return ToGrant(existing);

        var row = new CompanyAuthorityRow
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            ChildId = childId,
            PrincipalId = principalId,
            Operation = operation,
            GrantedBy = grantedBy,
            GrantedAt = now,
        };
        db.CompanyAuthorities.Add(row);
        await db.SaveChangesAsync(ct);
        return ToGrant(row);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(Guid id, Guid? revokedBy, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CompanyAuthorities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null || row.RevokedAt is not null)
            return false;

        row.RevokedAt = now;
        row.RevokedBy = revokedBy;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompanyAuthorityGrant>> ListAsync(Guid parentId, Guid childId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CompanyAuthorities.AsNoTracking()
            .Where(a => a.ParentId == parentId && a.ChildId == childId)
            .ToListAsync(ct);
        // Sorted here: SQLite cannot order by DateTimeOffset in the database.
        return rows.OrderBy(a => a.GrantedAt).Select(ToGrant).ToList();
    }

    /// <inheritdoc />
    public async Task<CompanyAuthorityGrant?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CompanyAuthorities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : ToGrant(row);
    }

    private static CompanyAuthorityGrant ToGrant(CompanyAuthorityRow row)
        => new(row.Id, row.ParentId, row.ChildId, row.PrincipalId, row.Operation, row.GrantedBy, row.GrantedAt, row.RevokedAt, row.RevokedBy);
}
