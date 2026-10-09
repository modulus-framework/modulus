using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Grants;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IApprovalAuthorityStore"/>: approval limits as durable, tenant-filtered, effective-dated rows, so a
/// change takes effect on the next request.
/// </summary>
public sealed class EfApprovalAuthorityStore(IDbContextFactory<AuthorizationStoreDbContext> factory) : IApprovalAuthorityStore
{
    /// <summary>A stored limit with its id and who made it.</summary>
    /// <param name="Id">The row id (used to remove it).</param>
    /// <param name="Authority">The limit.</param>
    /// <param name="CreatedBy">The administrator who made it, or null.</param>
    /// <param name="CreatedAt">When it was made.</param>
    public sealed record Stored(Guid Id, ApprovalAuthority Authority, Guid? CreatedBy, DateTimeOffset CreatedAt);

    /// <inheritdoc />
    public IReadOnlyCollection<ApprovalAuthority> GetAuthorities(PrincipalGrantQuery principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        var userKey = principal.UserId?.ToString();
        var roles = principal.Roles.ToArray();
        using var db = factory.CreateDbContext();
        var rows = db.ApprovalAuthorities.AsNoTracking()
            .Where(a => a.Permission == permission
                        && ((a.HolderType == GrantHolderType.User && a.Holder == userKey)
                            || (a.HolderType == GrantHolderType.Role && roles.Contains(a.Holder))))
            .ToList();
        return [.. rows.Select(ToAuthority)];
    }

    /// <summary>Adds a limit.</summary>
    public async Task<Stored> AddAsync(ApprovalAuthority authority, Guid? createdBy, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.Holder);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.Permission);
        ArgumentOutOfRangeException.ThrowIfNegative(authority.MaxAmount);
        if (authority is { ValidFrom: { } from, ValidUntil: { } until } && until <= from)
            throw new ArgumentException("A limit must end after it begins.", nameof(authority));

        var row = new ApprovalAuthorityRow
        {
            Id = global::Modulus.GuidV7.Create(),
            HolderType = authority.HolderType,
            Holder = authority.Holder.Trim(),
            Permission = authority.Permission.Trim(),
            MaxAmount = authority.MaxAmount,
            Currency = Normalize(authority.Currency)?.ToUpperInvariant(),
            DocumentType = Normalize(authority.DocumentType),
            OrgUnitId = authority.OrgUnitId,
            ValidFrom = authority.ValidFrom,
            ValidUntil = authority.ValidUntil,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.ApprovalAuthorities.Add(row);
        await db.SaveChangesAsync(ct);
        return new Stored(row.Id, ToAuthority(row), row.CreatedBy, row.CreatedAt);
    }

    /// <summary>One limit by id; null when it does not exist (in this company).</summary>
    public async Task<Stored?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ApprovalAuthorities.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : new Stored(row.Id, ToAuthority(row), row.CreatedBy, row.CreatedAt);
    }

    /// <summary>Removes a limit; false when it did not exist.</summary>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ApprovalAuthorities.Where(a => a.Id == id).ExecuteDeleteAsync(ct) > 0;
    }

    /// <summary>Every limit of a holder, ended ones included, for review.</summary>
    public async Task<IReadOnlyCollection<Stored>> ListAsync(GrantHolderType holderType, string holder, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ApprovalAuthorities.AsNoTracking()
            .Where(a => a.HolderType == holderType && a.Holder == holder)
            .ToListAsync(ct);
        // Ordered in memory: SQLite cannot order by DateTimeOffset.
        return [.. rows.OrderBy(r => r.Permission, StringComparer.Ordinal).ThenBy(r => r.CreatedAt)
            .Select(r => new Stored(r.Id, ToAuthority(r), r.CreatedBy, r.CreatedAt))];
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ApprovalAuthority ToAuthority(ApprovalAuthorityRow r)
        => new(r.HolderType, r.Holder, r.Permission, r.MaxAmount, r.Currency, r.DocumentType, r.OrgUnitId, r.ValidFrom, r.ValidUntil);
}
