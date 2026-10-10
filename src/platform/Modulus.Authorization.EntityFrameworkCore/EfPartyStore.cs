using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Grants;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// External-party users: a buyer contact, supplier or subcontractor is an ordinary account linked to one party (a buyer,
/// supplier or subcontractor record of the app) with a kind. Two things follow from the link:
/// <list type="bullet">
/// <item>An <c>Assigned</c> scope keyed on the kind (<c>assigned:buyer</c>) covers exactly that party, because the link
/// writes the user's assignment (type = kind, target = party id). A buyer sees only its own orders.</item>
/// <item>A permission ceiling per kind: whatever grants the account holds, only permissions the kind's ceiling lists take
/// effect (denies always do). A kind with no ceiling rows gets nothing, so a new kind fails closed.</item>
/// </list>
/// </summary>
public sealed class EfPartyStore(IDbContextFactory<AuthorizationStoreDbContext> factory)
{
    /// <summary>A user's link to a party.</summary>
    /// <param name="UserId">The account.</param>
    /// <param name="Kind">The kind, lower-case (for example <c>buyer</c>, <c>supplier</c>, <c>subcontractor</c>).</param>
    /// <param name="PartyId">The party record the account belongs to.</param>
    /// <param name="CreatedBy">The administrator who made it, or null.</param>
    /// <param name="CreatedAt">When it was made.</param>
    public sealed record Link(Guid UserId, string Kind, Guid PartyId, Guid? CreatedBy, DateTimeOffset CreatedAt);

    /// <summary>Links an account to a party, replacing an earlier link and its assignment. An account belongs to one party.</summary>
    public async Task<Link> LinkAsync(Guid userId, string kind, Guid partyId, Guid? createdBy, DateTimeOffset now, CancellationToken ct = default)
    {
        var normalized = NormalizeKind(kind);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.PartyLinks.SingleOrDefaultAsync(l => l.UserId == userId, ct);
        if (row is not null)
            await RemoveAssignmentAsync(db, row, ct);
        else
        {
            row = new PartyLinkRow { UserId = userId };
            db.PartyLinks.Add(row);
        }

        row.Kind = normalized;
        row.PartyId = partyId;
        row.CreatedBy = createdBy;
        row.CreatedAt = now;
        if (!await db.Assignments.AnyAsync(a => a.UserId == userId && a.AssignmentType == normalized && a.TargetId == partyId, ct))
            db.Assignments.Add(new AssignmentRow { UserId = userId, AssignmentType = normalized, TargetId = partyId });

        await db.SaveChangesAsync(ct);
        return ToLink(row);
    }

    /// <summary>Unlinks an account (and removes its party assignment); false when it had no link.</summary>
    public async Task<bool> UnlinkAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.PartyLinks.SingleOrDefaultAsync(l => l.UserId == userId, ct);
        if (row is null)
            return false;

        await RemoveAssignmentAsync(db, row, ct);
        db.PartyLinks.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Links of the company, optionally for one party.</summary>
    public async Task<IReadOnlyCollection<Link>> LinksAsync(Guid? partyId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.PartyLinks.AsNoTracking();
        if (partyId is { } p)
            query = query.Where(l => l.PartyId == p);
        var rows = await query.ToListAsync(ct);
        return [.. rows.OrderBy(l => l.Kind, StringComparer.Ordinal).Select(ToLink)];
    }

    /// <summary>The permissions a kind may ever use, by kind.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> CeilingsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.PartyCeilings.AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(c => c.Kind, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(c => c.Permission).Order(StringComparer.Ordinal)], StringComparer.Ordinal);
    }

    /// <summary>Lets a kind use a permission (an exact name or a <c>module:*</c> prefix); false when it already could.</summary>
    public async Task<bool> AllowAsync(string kind, string permission, CancellationToken ct = default)
    {
        var normalized = NormalizeKind(kind);
        var pattern = NormalizePermission(permission);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.PartyCeilings.AnyAsync(c => c.Kind == normalized && c.Permission == pattern, ct))
            return false;

        db.PartyCeilings.Add(new PartyCeilingRow { Kind = normalized, Permission = pattern });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Takes a permission out of a kind's ceiling; false when it was not there.</summary>
    public async Task<bool> DisallowAsync(string kind, string permission, CancellationToken ct = default)
    {
        var normalized = NormalizeKind(kind);
        var pattern = NormalizePermission(permission);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PartyCeilings.Where(c => c.Kind == normalized && c.Permission == pattern).ExecuteDeleteAsync(ct) > 0;
    }

    /// <summary>
    /// Applies the ceiling to the grants of a user: an account with no party link keeps them all; a linked one keeps its
    /// denies and the grants the kind's ceiling covers.
    /// </summary>
    internal static List<PermissionGrant> ApplyCeiling(AuthorizationStoreDbContext db, Guid? userId, List<PermissionGrant> grants)
    {
        if (userId is not { } id)
            return grants;
        var kind = db.PartyLinks.AsNoTracking().Where(l => l.UserId == id).Select(l => l.Kind).FirstOrDefault();
        if (kind is null)
            return grants;

        var ceiling = db.PartyCeilings.AsNoTracking().Where(c => c.Kind == kind).Select(c => c.Permission).ToList();
        return [.. grants.Where(g => g.Type == PermissionGrantType.Deny || Covered(ceiling, g.Permission))];
    }

    private static bool Covered(List<string> ceiling, string permission)
    {
        foreach (var allowed in ceiling)
        {
            if (string.Equals(allowed, permission, StringComparison.OrdinalIgnoreCase))
                return true;
            if (allowed.EndsWith('*') && permission.StartsWith(allowed[..^1], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Task RemoveAssignmentAsync(AuthorizationStoreDbContext db, PartyLinkRow link, CancellationToken ct)
        => db.Assignments
            .Where(a => a.UserId == link.UserId && a.AssignmentType == link.Kind && a.TargetId == link.PartyId)
            .ExecuteDeleteAsync(ct);

    private static string NormalizeKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return kind.Trim().ToLowerInvariant();
    }

    private static string NormalizePermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        var trimmed = permission.Trim();
        if (trimmed == "*")
            throw new ArgumentException("A ceiling cannot be the whole permission set.", nameof(permission));
        return trimmed;
    }

    private static Link ToLink(PartyLinkRow r) => new(r.UserId, r.Kind, r.PartyId, r.CreatedBy, r.CreatedAt);
}
