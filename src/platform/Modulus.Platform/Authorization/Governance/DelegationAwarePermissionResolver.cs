namespace Modulus.Authorization.Governance;

using Modulus.Authorization.Grants;

/// <summary>
/// Decorates the capability-layer <see cref="IPermissionResolver"/> so a principal's
/// effective set includes any permissions <b>delegated</b> to them and in force now — the
/// piece that makes temporary/delegated access actually take effect at
/// <see cref="Modulus.Core.Abstractions.ICurrentUser.HasPermission"/> without any change
/// to the permission checker (blueprint §5.13). Installed by <c>AddDelegation</c> in front
/// of the concrete <see cref="PermissionResolver"/>.
/// <para>
/// It composes the delegate's own direct authority (<paramref name="directAuthority"/>)
/// with the capped, decision-time delegated permissions from
/// <paramref name="delegationResolver"/>. Because the delegation resolver caps against the
/// delegator's <i>direct</i> authority, delegated permissions unioned here are never
/// themselves re-delegable — sub-delegation stays bounded.
/// </para>
/// </summary>
public sealed class DelegationAwarePermissionResolver(
    PermissionResolver directAuthority,
    IDelegationResolver delegationResolver) : IPermissionResolver
{
    public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var effective = directAuthority.Resolve(principal);
        if (principal.UserId is not { } userId)
            return effective;

        var delegated = delegationResolver.DelegatedPermissions(userId);
        if (delegated.Count == 0)
            return effective;

        return Union(effective, delegated);
    }

    /// <inheritdoc />
    public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal, IReadOnlyCollection<PermissionGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(grants);

        // Consume the supplied grants — no second store read. Delegated
        // permissions still union in: they are keyed on the user id, not on
        // grant rows.
        var effective = directAuthority.Resolve(principal, grants);
        if (principal.UserId is not { } userId)
            return effective;

        var delegated = delegationResolver.DelegatedPermissions(userId);
        if (delegated.Count == 0)
            return effective;

        return Union(effective, delegated);
    }

    private static IReadOnlySet<string> Union(
        IReadOnlySet<string> effective,
        IReadOnlyCollection<DelegatedPermission> delegated)
    {
        var combined = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);
        foreach (var permission in delegated)
            combined.Add(permission.Permission);

        return combined;
    }
}
