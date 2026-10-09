using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IPermissionGrantStore"/>: grants are durable rows,
/// editable at runtime through the async management methods and picked up by
/// the very next authorization decision (no restart, no re-issued token).
/// Registered as a singleton over <see cref="IDbContextFactory{TContext}"/>;
/// each lookup opens a short-lived context, and per-request memoisation happens
/// one layer up (the scoped permission checker computes a principal's effective
/// set once per request).
/// </summary>
/// <remarks>
/// Grant holder and permission matching is case-insensitive (OrdinalIgnoreCase),
/// matching the <see cref="InMemoryPermissionGrantStore"/> behavior. Both stores
/// treat role names and permission strings the same way regardless of casing.
/// </remarks>
public sealed class EfPermissionGrantStore(
    IDbContextFactory<AuthorizationStoreDbContext> factory,
    IServiceScopeFactory? scopes = null)
    : IPermissionGrantStore
{
    // Every write tells the access-change observers (the AI platform drops its cached scope), so a grant made by a seeder,
    // a job or app code is not left stale for minutes. Observers can be scoped, so they are resolved in a short scope; the
    // admin API reports the same change again, which only repeats an idempotent signal.
    private async Task NotifyAsync(string reason, CancellationToken ct)
    {
        if (scopes is null)
            return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetServices<IAccessChangeObserver>().NotifyAccessChangedAsync(
            new AccessChange { Kind = AccessChangeKinds.Grant, Reason = reason }, ct: ct);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var roles = principal.Roles.ToList();
        var userKey = principal.UserId?.ToString();
        if (roles.Count == 0 && userKey is null)
            return [];

        // Filter server-side so only the principal's rows cross the wire —
        // never the whole grants table. Holder matching is case-insensitive:
        // EF translates string.ToLower() to the provider's LOWER(), which is
        // deterministic (invariant) casing on the ASCII role/permission names
        // this store holds. HolderType is filtered as a plain enum predicate.
        var lowerRoles = roles.Select(r => r.ToLowerInvariant()).ToList();
        var lowerUser = userKey?.ToLowerInvariant();

        using var db = factory.CreateDbContext();
        var grantRows = db.Grants.AsNoTracking()
            .Where(g =>
                (g.HolderType == GrantHolderType.Role && lowerRoles.Contains(g.Holder.ToLower()))
                || (lowerUser != null
                    && g.HolderType == GrantHolderType.User
                    && g.Holder.ToLower() == lowerUser))
            .ToList();

        var result = grantRows
            .Select(g => new PermissionGrant(g.HolderType, g.Holder, g.Permission, g.Type))
            .ToList();

        var scopedRows = db.ScopedGrants.AsNoTracking()
            .Where(g =>
                (g.HolderType == GrantHolderType.Role && lowerRoles.Contains(g.Holder.ToLower()))
                || (lowerUser != null
                    && g.HolderType == GrantHolderType.User
                    && g.Holder.ToLower() == lowerUser))
            .ToList();
        result.AddRange(scopedRows.Select(ToGrant).OfType<PermissionGrant>());
        return result;
    }

    // A stored scope that no longer parses is dropped, never widened to "the whole company".
    private static PermissionGrant? ToGrant(ScopedGrantRow row)
        => PermissionScope.TryParse(row.Scope, out var scope)
            ? new PermissionGrant(row.HolderType, row.Holder, row.Permission, row.Type, scope, row.ValidFrom, row.ValidUntil, row.Reason)
            : null;

    /// <summary>A scoped, temporary or restricting grant as stored, with the id used to remove it.</summary>
    /// <param name="Id">The row id.</param>
    /// <param name="Grant">The grant.</param>
    /// <param name="CreatedBy">The administrator who made it, when known.</param>
    /// <param name="CreatedAt">When it was made.</param>
    public sealed record ScopedGrantRecord(Guid Id, PermissionGrant Grant, Guid? CreatedBy, DateTimeOffset CreatedAt);

    /// <summary>
    /// Adds (or replaces, for the same holder, permission, effect and scope) a grant that carries a scope, a validity window
    /// or a restriction. A <c>Deny</c> may carry a window (a temporary block) but never a scope: it removes the permission.
    /// </summary>
    public async Task<ScopedGrantRecord> AddScopedGrantAsync(
        PermissionGrant grant, Guid? createdBy, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.Holder);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.Permission);
        if (grant.Type is PermissionGrantType.Deny && grant.Scope is { Kind: not ScopeKind.Tenant })
            throw new ArgumentException("A deny removes the permission; it cannot carry a scope. Use a restriction to narrow one.", nameof(grant));
        if (grant.Type is PermissionGrantType.Restrict && grant.Scope is null or { Kind: ScopeKind.Tenant })
            throw new ArgumentException("A restriction needs a scope narrower than the whole company.", nameof(grant));
        if (grant is { ValidFrom: { } from, ValidUntil: { } until } && until <= from)
            throw new ArgumentException("A grant must end after it begins.", nameof(grant));

        var scope = grant.EffectiveScope.Format();
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ScopedGrants.SingleOrDefaultAsync(
            g => g.HolderType == grant.HolderType && g.Holder == grant.Holder && g.Permission == grant.Permission
                 && g.Type == grant.Type && g.Scope == scope, ct);
        if (row is null)
        {
            row = new ScopedGrantRow
            {
                Id = Guid.NewGuid(),
                HolderType = grant.HolderType,
                Holder = grant.Holder,
                Permission = grant.Permission,
                Type = grant.Type,
                Scope = scope,
                CreatedBy = createdBy,
                CreatedAt = now,
            };
            db.ScopedGrants.Add(row);
        }

        row.ValidFrom = grant.ValidFrom;
        row.ValidUntil = grant.ValidUntil;
        row.Reason = grant.Reason;
        await db.SaveChangesAsync(ct);
        await NotifyAsync("grant.scoped-saved", ct);
        return new ScopedGrantRecord(row.Id, ToGrant(row)!, row.CreatedBy, row.CreatedAt);
    }

    /// <summary>Removes a scoped grant by id; false when there is no such grant.</summary>
    public async Task<bool> RemoveScopedGrantAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var removed = await db.ScopedGrants.Where(g => g.Id == id).ExecuteDeleteAsync(ct) > 0;
        if (removed)
            await NotifyAsync("grant.scoped-removed", ct);
        return removed;
    }

    /// <summary>One scoped grant by id, or null.</summary>
    public async Task<ScopedGrantRecord?> GetScopedGrantAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ScopedGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct);
        return row is null || ToGrant(row) is not { } grant ? null : new ScopedGrantRecord(row.Id, grant, row.CreatedBy, row.CreatedAt);
    }

    /// <summary>Every scoped grant of one holder (expired ones included, for review).</summary>
    public async Task<IReadOnlyCollection<ScopedGrantRecord>> GetScopedGrantsForHolderAsync(
        GrantHolderType holderType, string holder, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ScopedGrants.AsNoTracking()
            .Where(g => g.HolderType == holderType && g.Holder == holder)
            .OrderBy(g => g.Permission).ThenBy(g => g.Scope)
            .ToListAsync(ct);
        return [.. rows.Select(r => ToGrant(r) is { } g ? new ScopedGrantRecord(r.Id, g, r.CreatedBy, r.CreatedAt) : null).OfType<ScopedGrantRecord>()];
    }

    /// <summary>Every grant attached to one holder — the admin/review read,
    /// complementing the principal-shaped <see cref="GetGrants"/>.</summary>
    public async Task<IReadOnlyCollection<PermissionGrant>> GetGrantsForHolderAsync(
        GrantHolderType holderType, string holder, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Grants.AsNoTracking()
            .Where(g => g.HolderType == holderType && g.Holder == holder)
            .ToListAsync(ct);
        return rows
            .Select(g => new PermissionGrant(g.HolderType, g.Holder, g.Permission, g.Type))
            .ToList();
    }

    /// <summary>All distinct user IDs with at least one grant (direct or role-based).</summary>
    public async Task<IReadOnlyCollection<Guid>> GetAllUsersWithGrantsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var userIds = await db.Grants.AsNoTracking()
            .Where(g => g.HolderType == GrantHolderType.User)
            .Select(g => g.Holder)
            .Distinct()
            .ToListAsync(ct);

        return userIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToList();
    }

    /// <summary>Grants one or more permissions to a role.</summary>
    public Task GrantToRoleAsync(string role, IEnumerable<string> permissions, CancellationToken ct = default)
        => SetAsync(GrantHolderType.Role, role, PermissionGrantType.Allow, permissions, ct);

    /// <summary>Explicitly denies one or more permissions to a role (overrides any allow).</summary>
    public Task DenyToRoleAsync(string role, IEnumerable<string> permissions, CancellationToken ct = default)
        => SetAsync(GrantHolderType.Role, role, PermissionGrantType.Deny, permissions, ct);

    /// <summary>Grants one or more permissions directly to a user.</summary>
    public Task GrantToUserAsync(Guid userId, IEnumerable<string> permissions, CancellationToken ct = default)
        => SetAsync(GrantHolderType.User, userId.ToString(), PermissionGrantType.Allow, permissions, ct);

    /// <summary>Explicitly denies one or more permissions directly to a user.</summary>
    public Task DenyToUserAsync(Guid userId, IEnumerable<string> permissions, CancellationToken ct = default)
        => SetAsync(GrantHolderType.User, userId.ToString(), PermissionGrantType.Deny, permissions, ct);

    /// <summary>Removes a role grant/denial (no-op if it was never set).</summary>
    public Task RevokeFromRoleAsync(string role, string permission, CancellationToken ct = default)
        => RemoveAsync(GrantHolderType.Role, role, permission, ct);

    /// <summary>Removes a direct user grant/denial (no-op if it was never set).</summary>
    public Task RevokeFromUserAsync(Guid userId, string permission, CancellationToken ct = default)
        => RemoveAsync(GrantHolderType.User, userId.ToString(), permission, ct);

    private async Task SetAsync(
        GrantHolderType holderType, string holder,
        PermissionGrantType type, IEnumerable<string> permissions,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        ArgumentNullException.ThrowIfNull(permissions);

        await using var db = await factory.CreateDbContextAsync(ct);
        foreach (var permission in permissions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);

            var existing = await db.Grants
                .Where(g => g.HolderType == holderType && g.Holder == holder && g.Permission == permission)
                .SingleOrDefaultAsync(ct);
            if (existing is null)
                db.Grants.Add(new PermissionGrantRow
                {
                    HolderType = holderType,
                    Holder = holder,
                    Permission = permission,
                    Type = type,
                });
            else
                existing.Type = type;
        }

        await db.SaveChangesAsync(ct);
        await NotifyAsync("grant.saved", ct);
    }

    private async Task RemoveAsync(
        GrantHolderType holderType, string holder, string permission, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var removed = await db.Grants
            .Where(g => g.HolderType == holderType
                     && g.Holder == holder
                     && g.Permission == permission)
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
            await NotifyAsync("grant.removed", ct);
    }
}
