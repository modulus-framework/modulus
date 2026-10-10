using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Positions: a post such as "Line Supervisor of Sewing Line 3" that grants roles to whoever holds it, for a period. The role
/// follows the post, not the person: when someone is moved the holding ends and the access goes with it. The grant and
/// approval-limit stores add the roles of a user's current positions to the roles they present (then expand role inclusions).
/// </summary>
public sealed class EfPositionStore(IDbContextFactory<AuthorizationStoreDbContext> factory, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>A position as stored.</summary>
    /// <param name="Id">The position id.</param>
    /// <param name="Code">The unique code within the company.</param>
    /// <param name="Name">The display name.</param>
    /// <param name="OrgUnitId">The org unit it belongs to, or null.</param>
    /// <param name="Roles">The roles it grants.</param>
    /// <param name="IsActive">False while the position grants nothing.</param>
    public sealed record Position(Guid Id, string Code, string Name, Guid? OrgUnitId, IReadOnlyList<string> Roles, bool IsActive);

    /// <summary>A user's holding of a position.</summary>
    /// <param name="Id">The holding id.</param>
    /// <param name="PositionId">The position.</param>
    /// <param name="UserId">The holder.</param>
    /// <param name="ValidFrom">When it starts.</param>
    /// <param name="ValidUntil">When it ends, or null for open-ended.</param>
    /// <param name="CreatedBy">The administrator who made it, or null.</param>
    /// <param name="CreatedAt">When it was made.</param>
    public sealed record Holding(Guid Id, Guid PositionId, Guid UserId, DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, Guid? CreatedBy, DateTimeOffset CreatedAt);

    /// <summary>Every position of the current company, by code.</summary>
    public async Task<IReadOnlyCollection<Position>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Positions.AsNoTracking().ToListAsync(ct);
        return [.. rows.Select(ToPosition).OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Adds a position; null when the code is taken in this company.</summary>
    public async Task<Position?> AddAsync(string code, string name, Guid? orgUnitId, IEnumerable<string> roles, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = NormalizeCode(code);

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Positions.AnyAsync(p => p.NormalizedCode == normalized, ct))
            return null;

        var row = new PositionRow
        {
            Id = global::Modulus.GuidV7.Create(),
            Code = code.Trim(),
            NormalizedCode = normalized,
            Name = name.Trim(),
            OrgUnitId = orgUnitId,
            Roles = SerializeRoles(roles),
            CreatedAt = _clock.GetUtcNow(),
        };
        db.Positions.Add(row);
        await db.SaveChangesAsync(ct);
        return ToPosition(row);
    }

    /// <summary>Replaces a position's name, org unit, roles and switch; null when it does not exist.</summary>
    public async Task<Position?> UpdateAsync(Guid id, string name, Guid? orgUnitId, IEnumerable<string> roles, bool isActive, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Positions.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
            return null;

        row.Name = name.Trim();
        row.OrgUnitId = orgUnitId;
        row.Roles = SerializeRoles(roles);
        row.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return ToPosition(row);
    }

    /// <summary>Removes a position and every holding of it; false when it did not exist.</summary>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.PositionAssignments.Where(a => a.PositionId == id).ExecuteDeleteAsync(ct);
        return await db.Positions.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0;
    }

    /// <summary>Puts a user in a position for a period. Null when the position does not exist; throws when the period is empty.</summary>
    public async Task<Holding?> AssignAsync(
        Guid positionId, Guid userId, DateTimeOffset validFrom, DateTimeOffset? validUntil, Guid? createdBy, CancellationToken ct = default)
    {
        if (validUntil is { } until && until <= validFrom)
            throw new ArgumentException("A holding must end after it starts.", nameof(validUntil));

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Positions.AnyAsync(p => p.Id == positionId, ct))
            return null;

        var row = new PositionAssignmentRow
        {
            Id = global::Modulus.GuidV7.Create(),
            PositionId = positionId,
            UserId = userId,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            CreatedBy = createdBy,
            CreatedAt = _clock.GetUtcNow(),
        };
        db.PositionAssignments.Add(row);
        await db.SaveChangesAsync(ct);
        return ToHolding(row);
    }

    /// <summary>Ends a holding now (it stays on record); false when it does not exist or already ended.</summary>
    public async Task<bool> EndAsync(Guid holdingId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.PositionAssignments.SingleOrDefaultAsync(a => a.Id == holdingId, ct);
        if (row is null || (row.ValidUntil is { } until && until <= now))
            return false;

        row.ValidUntil = now > row.ValidFrom ? now : row.ValidFrom.AddTicks(1);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The holdings of a position and/or of a user (both null lists everything).</summary>
    public async Task<IReadOnlyCollection<Holding>> HoldingsAsync(Guid? positionId, Guid? userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.PositionAssignments.AsNoTracking();
        if (positionId is { } p)
            query = query.Where(a => a.PositionId == p);
        if (userId is { } u)
            query = query.Where(a => a.UserId == u);
        var rows = await query.ToListAsync(ct);
        return [.. rows.OrderBy(a => a.ValidFrom).Select(ToHolding)];
    }

    /// <summary>The roles a user holds through current positions (active, started, not ended).</summary>
    public async Task<IReadOnlyCollection<string>> RolesOfAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return RolesOf(db, userId, _clock.GetUtcNow());
    }

    /// <summary>Synchronous lookup over an open context, for the grant and approval stores.</summary>
    internal static IReadOnlyList<string> RolesOf(AuthorizationStoreDbContext db, Guid userId, DateTimeOffset now)
    {
        var positionIds = db.PositionAssignments.AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToList()
            .Where(a => a.ValidFrom <= now && (a.ValidUntil == null || a.ValidUntil > now))
            .Select(a => a.PositionId)
            .ToList();
        if (positionIds.Count == 0)
            return [];

        return [.. db.Positions.AsNoTracking()
            .Where(p => positionIds.Contains(p.Id) && p.IsActive)
            .ToList()
            .SelectMany(p => ParseRoles(p.Roles))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The roles presented plus those of the user's current positions.</summary>
    internal static IReadOnlyCollection<string> WithPositionRoles(AuthorizationStoreDbContext db, IEnumerable<string> roles, Guid? userId)
    {
        var given = roles.ToList();
        if (userId is not { } id)
            return given;
        var seen = new HashSet<string>(given, StringComparer.OrdinalIgnoreCase);
        given.AddRange(RolesOf(db, id, TimeProvider.System.GetUtcNow()).Where(seen.Add));
        return given;
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static string SerializeRoles(IEnumerable<string> roles)
        => JsonSerializer.Serialize(roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string[] ParseRoles(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static Position ToPosition(PositionRow r) => new(r.Id, r.Code, r.Name, r.OrgUnitId, ParseRoles(r.Roles), r.IsActive);

    private static Holding ToHolding(PositionAssignmentRow r) => new(r.Id, r.PositionId, r.UserId, r.ValidFrom, r.ValidUntil, r.CreatedBy, r.CreatedAt);
}
