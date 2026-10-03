using System.Collections.Concurrent;

namespace Modulus.MultiTenancy;

/// <summary>
/// Which tenants (companies) a user may act in. One login can reach several tenants; the
/// tenant a request selects (header, subdomain, switcher) is allowed only when the user holds
/// an active membership in it. Checked by <see cref="TenantMiddleware"/> when
/// <c>RequireMembership()</c> is on. Implementations must be safe to call from a singleton.
/// </summary>
public interface ITenantMembershipStore
{
    /// <summary>Whether <paramref name="userId"/> holds an active membership in <paramref name="tenantId"/>.</summary>
    Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>The tenants <paramref name="userId"/> holds an active membership in (for a company switcher).</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="ITenantMembershipStore"/>: an in-process set, empty until seeded with
/// <see cref="Add"/>. Empty means nobody is a member, so <c>RequireMembership()</c> without a
/// real store fails closed. Use the EF store (<c>AddEfCoreTenantStore</c>) for persisted memberships.
/// </summary>
public sealed class InMemoryTenantMembershipStore : ITenantMembershipStore
{
    private readonly ConcurrentDictionary<(Guid UserId, Guid TenantId), byte> _memberships = new();

    /// <summary>Grants <paramref name="userId"/> membership in <paramref name="tenantId"/>.</summary>
    public void Add(Guid userId, Guid tenantId) => _memberships.TryAdd((userId, tenantId), 0);

    /// <summary>Revokes a membership; returns <see langword="false"/> when it did not exist.</summary>
    public bool Remove(Guid userId, Guid tenantId) => _memberships.TryRemove((userId, tenantId), out _);

    public Task<bool> IsMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(_memberships.ContainsKey((userId, tenantId)));

    public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(
            [.. _memberships.Keys.Where(k => k.UserId == userId).Select(k => k.TenantId).Order()]);
}
