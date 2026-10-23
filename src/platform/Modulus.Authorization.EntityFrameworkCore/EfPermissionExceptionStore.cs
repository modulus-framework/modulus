using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Grants;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// A recorded exception to the privileged-permission rules: why a holder keeps a Critical permission, who approved it, until when,
/// and who reviews it. The register records and reports; it does not grant or revoke access (the grants do that).
/// </summary>
/// <param name="Id">The exception id.</param>
/// <param name="Permission">The Critical permission the exception covers.</param>
/// <param name="HolderType">A role or a user.</param>
/// <param name="Holder">The role name or the user id.</param>
/// <param name="Justification">Why the holder needs it (required).</param>
/// <param name="CompensatingControl">What limits the risk while the exception stands.</param>
/// <param name="BusinessOwner">The business owner who approved the exception.</param>
/// <param name="RequestedBy">Who recorded it.</param>
/// <param name="ValidFrom">When the exception starts.</param>
/// <param name="ValidUntil">When it ends; it is not active after this.</param>
/// <param name="ReviewDueOn">When it must be reviewed.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <param name="ReviewedBy">Who reviewed it (never the requester).</param>
/// <param name="ReviewedAt">When it was reviewed.</param>
/// <param name="RevokedBy">Who ended it early.</param>
/// <param name="RevokedAt">When it was ended early.</param>
public sealed record PermissionException(
    Guid Id, string Permission, GrantHolderType HolderType, string Holder, string Justification, string? CompensatingControl,
    string BusinessOwner, Guid RequestedBy, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, DateTimeOffset ReviewDueOn,
    DateTimeOffset CreatedAt, Guid? ReviewedBy = null, DateTimeOffset? ReviewedAt = null, Guid? RevokedBy = null, DateTimeOffset? RevokedAt = null)
{
    /// <summary>Whether the exception covers the moment: started, not ended, not revoked.</summary>
    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ValidFrom <= now && now < ValidUntil;
}

/// <summary>A grant of a Critical permission that the register does not explain (no active exception covers it).</summary>
/// <param name="Permission">The Critical permission.</param>
/// <param name="HolderType">The role or user that holds it.</param>
/// <param name="Holder">The role name or user id.</param>
/// <param name="HadExpiredException">An exception existed for this holder and permission, but it has ended.</param>
public sealed record PermissionExceptionFinding(string Permission, GrantHolderType HolderType, string Holder, bool HadExpiredException);

/// <summary>The permission-exception register of the current company (table <c>ModulusPermissionExceptions</c>).</summary>
public sealed class EfPermissionExceptionStore(IDbContextFactory<AuthorizationStoreDbContextBase> factory)
{
    /// <summary>Records an exception.</summary>
    public async Task<PermissionException> CreateAsync(PermissionException exception, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var row = new PermissionExceptionRow
        {
            Id = exception.Id == Guid.Empty ? global::Modulus.GuidV7.Create() : exception.Id,
            Permission = exception.Permission,
            HolderType = exception.HolderType,
            Holder = exception.Holder,
            Justification = exception.Justification,
            CompensatingControl = exception.CompensatingControl,
            BusinessOwner = exception.BusinessOwner,
            RequestedBy = exception.RequestedBy,
            ValidFrom = exception.ValidFrom,
            ValidUntil = exception.ValidUntil,
            ReviewDueOn = exception.ReviewDueOn,
            CreatedAt = exception.CreatedAt,
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.PermissionExceptions.Add(row);
        await db.SaveChangesAsync(ct);
        return ToRecord(row);
    }

    /// <summary>One exception by id; null when it does not exist in this company.</summary>
    public async Task<PermissionException?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.PermissionExceptions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToRecord(row);
    }

    /// <summary>The register, newest first. With <paramref name="activeAt"/> only exceptions in force at that moment.</summary>
    public async Task<IReadOnlyList<PermissionException>> ListAsync(DateTimeOffset? activeAt = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // The time window is compared in memory: it is the same rule as PermissionException.IsActiveAt, and providers differ in how they translate offsets.
        var rows = (await db.PermissionExceptions.AsNoTracking().ToListAsync(ct)).Select(ToRecord);
        if (activeAt is { } now)
            rows = rows.Where(r => r.IsActiveAt(now));
        return [.. rows.OrderByDescending(r => r.CreatedAt)];
    }

    /// <summary>
    /// Records the review of an exception that is not yet reviewed or revoked. False when it is not one of those.
    /// The reviewer must differ from the requester; the caller checks that first.
    /// </summary>
    public async Task<bool> ReviewAsync(Guid id, Guid reviewer, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PermissionExceptions.Where(r => r.Id == id && r.ReviewedAt == null && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReviewedBy, reviewer)
                .SetProperty(r => r.ReviewedAt, now), ct) > 0;
    }

    /// <summary>Ends an exception early. False when it has already ended.</summary>
    public async Task<bool> RevokeAsync(Guid id, Guid by, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PermissionExceptions.Where(r => r.Id == id && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RevokedBy, by)
                .SetProperty(r => r.RevokedAt, now), ct) > 0;
    }

    /// <summary>
    /// Grants of the given Critical permissions that no active exception explains: role and user grants, and scoped grants that
    /// are valid now. A wildcard grant is not reported, because a wildcard never confers a Critical permission.
    /// </summary>
    public async Task<IReadOnlyList<PermissionExceptionFinding>> FindingsAsync(
        IEnumerable<string> criticalPermissions, DateTimeOffset now, CancellationToken ct = default)
    {
        var critical = criticalPermissions.Select(p => p.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (critical.Count == 0)
            return [];

        await using var db = await factory.CreateDbContextAsync(ct);
        var criticalList = critical.ToList();
        // Time windows are compared in memory (see ListAsync): the scoped grants are filtered the same way the resolver filters them.
        var grants = (await db.Grants.AsNoTracking()
            .Where(g => g.Type == PermissionGrantType.Allow && criticalList.Contains(g.Permission.ToLower()))
            .Select(g => new { g.HolderType, g.Holder, g.Permission })
            .ToListAsync(ct))
            .Select(g => (g.HolderType, g.Holder, g.Permission))
            .ToList();
        grants.AddRange((await db.ScopedGrants.AsNoTracking()
            .Where(g => g.Type == PermissionGrantType.Allow && criticalList.Contains(g.Permission.ToLower()))
            .Select(g => new { g.HolderType, g.Holder, g.Permission, g.ValidFrom, g.ValidUntil })
            .ToListAsync(ct))
            .Where(g => (g.ValidFrom is null || g.ValidFrom <= now) && (g.ValidUntil is null || g.ValidUntil > now))
            .Select(g => (g.HolderType, g.Holder, g.Permission)));

        var exceptions = await db.PermissionExceptions.AsNoTracking()
            .Where(r => criticalList.Contains(r.Permission.ToLower()))
            .ToListAsync(ct);

        var findings = new List<PermissionExceptionFinding>();
        foreach (var grant in grants.DistinctBy(g => (g.HolderType, g.Holder.ToLowerInvariant(), g.Permission.ToLowerInvariant())))
        {
            var matching = exceptions.Where(e => e.HolderType == grant.HolderType
                && string.Equals(e.Holder, grant.Holder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Permission, grant.Permission, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Any(e => ToRecord(e).IsActiveAt(now)))
                continue;
            findings.Add(new PermissionExceptionFinding(grant.Permission, grant.HolderType, grant.Holder, matching.Count > 0));
        }
        return findings;
    }

    private static PermissionException ToRecord(PermissionExceptionRow r) => new(
        r.Id, r.Permission, r.HolderType, r.Holder, r.Justification, r.CompensatingControl, r.BusinessOwner, r.RequestedBy,
        r.ValidFrom, r.ValidUntil, r.ReviewDueOn, r.CreatedAt, r.ReviewedBy, r.ReviewedAt, r.RevokedBy, r.RevokedAt);
}
