namespace Modulus.Authorization.Governance;

using Modulus.Authorization.Grants;

/// <summary>
/// Default <see cref="IDelegationResolver"/>. For each delegation in force at the current
/// instant (per <see cref="TimeProvider"/>), it intersects the delegated permissions with
/// the delegator's <b>direct</b> effective set — resolved from the concrete
/// <see cref="PermissionResolver"/> using the roles snapshotted on the delegation — so
/// the delegate never receives authority the delegator does not currently hold, and
/// delegated-through authority is not itself re-delegable. Pure; every call recomputes
/// from the store, so revocation and expiry take effect immediately.
/// </summary>
public sealed class DelegationResolver(
    IDelegationStore store,
    PermissionResolver directAuthority,
    TimeProvider timeProvider) : IDelegationResolver
{
    public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId)
    {
        var now = timeProvider.GetUtcNow();
        var active = store.ActiveFor(delegateUserId, now);
        if (active.Count == 0)
            return [];

        var result = new List<DelegatedPermission>();
        foreach (var delegation in active)
        {
            // The cap: what the delegator holds DIRECTLY (grants by their user id + the
            // roles snapshotted on the delegation) — never their own delegated authority.
            var delegatorAuthority = directAuthority.Resolve(
                new PrincipalGrantQuery(delegation.FromUserId, delegation.FromRoles));

            foreach (var permission in delegation.Permissions)
            {
                if (delegatorAuthority.Contains(permission))
                    result.Add(new DelegatedPermission(permission, delegation.FromUserId, delegation.Id));
            }
        }

        return result;
    }
}

/// <summary>
/// The delegation resolver in effect before <c>AddDelegation</c> is called: no
/// delegations exist, so no user holds delegated authority. Lets
/// <see cref="EffectiveAccessService"/> and the resolver decorator compose unconditionally
/// while keeping delegation strictly opt-in.
/// </summary>
internal sealed class EmptyDelegationResolver : IDelegationResolver
{
    public static readonly EmptyDelegationResolver Instance = new();

    public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId) => [];
}
