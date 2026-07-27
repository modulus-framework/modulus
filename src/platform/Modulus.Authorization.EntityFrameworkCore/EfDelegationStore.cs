using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Governance;

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
    IDbContextFactory<AuthorizationStoreDbContext> factory)
    : IDelegationStore
{
    /// <inheritdoc />
    public IReadOnlyCollection<Delegation> ActiveFor(Guid toUserId, DateTimeOffset now)
    {
        using var db = factory.CreateDbContext();
        return db.Delegations.AsNoTracking()
            .Where(d => d.ToUserId == toUserId && !d.Revoked)
            .AsEnumerable()
            .Select(ToDelegation)
            .Where(d => d.IsActiveAt(now))
            .ToList();
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
