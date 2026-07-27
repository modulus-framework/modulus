using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Grants;

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
/// Holder and permission comparisons follow the database collation, unlike the
/// in-memory store's ordinal-ignore-case dictionaries — keep grant data in the
/// same casing your identity provider emits, or use a case-insensitive
/// collation.
/// </remarks>
public sealed class EfPermissionGrantStore(
    IDbContextFactory<AuthorizationStoreDbContext> factory)
    : IPermissionGrantStore
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var roles = principal.Roles.ToList();
        var userKey = principal.UserId?.ToString();
        if (roles.Count == 0 && userKey is null)
            return [];

        using var db = factory.CreateDbContext();
        return db.Grants.AsNoTracking()
            .Where(g =>
                (g.HolderType == GrantHolderType.Role && roles.Contains(g.Holder))
                || (userKey != null
                    && g.HolderType == GrantHolderType.User
                    && g.Holder == userKey))
            .AsEnumerable()
            .Select(g => new PermissionGrant(g.HolderType, g.Holder, g.Permission, g.Type))
            .ToList();
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

            var existing = await db.Grants.FindAsync(
                [holderType, holder, permission], ct);
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
    }

    private async Task RemoveAsync(
        GrantHolderType holderType, string holder, string permission, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Grants
            .Where(g => g.HolderType == holderType
                     && g.Holder == holder
                     && g.Permission == permission)
            .ExecuteDeleteAsync(ct);
    }
}
