namespace Modulus.Authorization.Governance;

using System.Collections.Concurrent;

/// <summary>
/// In-memory <see cref="IDelegationStore"/>: delegations created and revoked at runtime
/// (thread-safe). Seed baseline delegations at startup via <c>AddDelegation</c> and
/// create/revoke them from admin flows. Empty ⇒ no delegated authority (fail-closed).
/// </summary>
public sealed class InMemoryDelegationStore : IDelegationStore
{
    private readonly ConcurrentDictionary<Guid, Delegation> _delegations = new();

    /// <summary>
    /// Creates a delegation from <paramref name="fromUserId"/> (carrying their
    /// <paramref name="fromRoles"/> snapshot for capping) to <paramref name="toUserId"/>
    /// for <paramref name="permissions"/> over [<paramref name="notBefore"/>,
    /// <paramref name="notAfter"/>). Returns the stored delegation, including its
    /// generated <see cref="Delegation.Id"/> for later revocation.
    /// </summary>
    public Delegation Delegate(
        Guid fromUserId,
        IEnumerable<string> fromRoles,
        Guid toUserId,
        IEnumerable<string> permissions,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        ArgumentNullException.ThrowIfNull(fromRoles);
        ArgumentNullException.ThrowIfNull(permissions);
        if (notAfter <= notBefore)
            throw new ArgumentException("A delegation window must end after it begins.", nameof(notAfter));

        var delegation = new Delegation(
            Guid.NewGuid(),
            fromUserId,
            [.. fromRoles],
            toUserId,
            new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase),
            notBefore,
            notAfter);

        _delegations[delegation.Id] = delegation;
        return delegation;
    }

    /// <summary>Revokes the delegation with <paramref name="id"/> immediately. Returns false if unknown.</summary>
    public bool Revoke(Guid id)
    {
        while (_delegations.TryGetValue(id, out var existing))
        {
            if (existing.Revoked)
                return false;
            if (_delegations.TryUpdate(id, existing with { Revoked = true }, existing))
                return true;
        }

        return false;
    }

    public IReadOnlyCollection<Delegation> ActiveFor(Guid toUserId, DateTimeOffset now)
        => [.. _delegations.Values.Where(d => d.ToUserId == toUserId && d.IsActiveAt(now))];

    public IReadOnlyCollection<Delegation> All() => [.. _delegations.Values];
}
