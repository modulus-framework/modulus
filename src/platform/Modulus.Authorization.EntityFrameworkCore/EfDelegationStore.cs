using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Governance;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IDelegationStore"/>: delegations as durable rows,
/// so a temporary transfer of authority survives restarts and its revocation is
/// immediately visible to every application instance. Window validity is
/// evaluated in memory via <see cref="Delegation.IsActiveAt"/> after narrowing
/// by delegate id, keeping decision-time semantics identical across database
/// providers.
/// </summary>
public sealed class EfDelegationStore(
    IDbContextFactory<AuthorizationStoreDbContextBase> factory,
    IServiceScopeFactory? scopes = null)
    : IDelegationStore
{
    /// <summary>
    /// The delegations in force for a user, in the companies the current request reads: a delegation made in a company above the current one
    /// applies below it, the same as a grant does. Admin listings and the role refresher keep the company's own rows.
    /// </summary>
    /// <inheritdoc />
    public IReadOnlyCollection<Delegation> ActiveFor(Guid toUserId, DateTimeOffset now)
    {
        using var db = factory.CreateDbContext();
        var reach = ReachNow();
        var rows = reach is null
            ? db.Delegations.AsNoTracking()
            : db.Delegations.IgnoreQueryFilters().AsNoTracking().Where(d => reach.Contains(d.TenantId));
        return rows
            .Where(d => d.ToUserId == toUserId && !d.Revoked)
            .AsEnumerable()
            .Select(ToDelegation)
            .Where(d => d.IsActiveAt(now))
            .ToList();
    }

    private IReadOnlyList<Guid>? ReachNow()
    {
        if (scopes is null)
            return null;
        using var scope = scopes.CreateScope();
        return TenantReach.Of(scope.ServiceProvider.GetService<ICurrentTenant>());
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Delegation> All()
    {
        using var db = factory.CreateDbContext();
        return db.Delegations.AsNoTracking()
            .AsEnumerable()
            .Select(ToDelegation)
            .ToList();
    }

    /// <summary>
    /// Creates a delegation from <paramref name="fromUserId"/> (carrying their
    /// <paramref name="fromRoles"/> snapshot for capping) to
    /// <paramref name="toUserId"/> for <paramref name="permissions"/> over
    /// [<paramref name="notBefore"/>, <paramref name="notAfter"/>). Returns the
    /// stored delegation, including its generated id for later revocation.
    /// </summary>
    public async Task<Delegation> DelegateAsync(
        Guid fromUserId,
        IEnumerable<string> fromRoles,
        Guid toUserId,
        IEnumerable<string> permissions,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fromRoles);
        ArgumentNullException.ThrowIfNull(permissions);
        if (notAfter <= notBefore)
            throw new ArgumentException(
                "A delegation window must end after it begins.", nameof(notAfter));

        var delegation = new Delegation(
            Guid.NewGuid(),
            fromUserId,
            [.. fromRoles],
            toUserId,
            new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase),
            notBefore,
            notAfter);

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Delegations.Add(new DelegationRow
        {
            Id = delegation.Id,
            FromUserId = delegation.FromUserId,
            FromRolesJson = JsonSerializer.Serialize(delegation.FromRoles),
            ToUserId = delegation.ToUserId,
            PermissionsJson = JsonSerializer.Serialize(delegation.Permissions),
            NotBefore = delegation.NotBefore,
            NotAfter = delegation.NotAfter,
        });
        await db.SaveChangesAsync(ct);
        return delegation;
    }

    /// <summary>Revokes the delegation with <paramref name="id"/> immediately.
    /// Returns false if unknown or already revoked.</summary>
    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var affected = await db.Delegations
            .Where(d => d.Id == id && !d.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Revoked, true), ct);
        return affected > 0;
    }

    /// <summary>Non-revoked delegations that have not ended, for the role refresher.</summary>
    internal IReadOnlyCollection<Delegation> Live(DateTimeOffset now)
    {
        using var db = factory.CreateDbContext();
        return db.Delegations.AsNoTracking()
            .Where(d => !d.Revoked)
            .AsEnumerable()
            .Select(ToDelegation)
            .Where(d => d.NotAfter > now)
            .ToList();
    }

    /// <summary>Replaces the delegator's role snapshot on one delegation (the cap is recomputed from it at every decision).</summary>
    internal async Task SetFromRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(roles.Order(StringComparer.Ordinal).ToArray());
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Delegations.Where(d => d.Id == id && !d.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.FromRolesJson, json), ct);
    }

    private static Delegation ToDelegation(DelegationRow row)
        => new(
            row.Id,
            row.FromUserId,
            Deserialize(row.FromRolesJson),
            row.ToUserId,
            new HashSet<string>(Deserialize(row.PermissionsJson), StringComparer.OrdinalIgnoreCase),
            row.NotBefore,
            row.NotAfter,
            row.Revoked);

    private static string[] Deserialize(string json)
        => JsonSerializer.Deserialize<string[]>(json) ?? [];
}
