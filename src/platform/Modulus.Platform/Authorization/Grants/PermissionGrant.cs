namespace Modulus.Authorization.Grants;

/// <summary>
/// Whether a grant <b>allows</b> a permission or explicitly <b>denies</b> it.
/// Denials always win over allows during resolution (deny-override) — see
/// <see cref="IPermissionResolver"/>.
/// </summary>
public enum PermissionGrantType
{
    Allow = 0,
    Deny = 1,
}

/// <summary>
/// The holder a grant is attached to: a role (assigned to many principals) or a
/// single user (a direct, principal-specific grant).
/// </summary>
public enum GrantHolderType
{
    Role = 0,
    User = 1,
}

/// <summary>
/// A single unit of authorization data: "this holder allows/denies this permission".
/// Grants are the editable, revocable truth (§5.2 of the authorization blueprint) —
/// distinct from the frozen permission <em>catalog</em> (<see cref="Modulus.Core.Abstractions.IPermissionRegistry"/>).
/// A permission name ending in <c>:*</c> is a wildcard covering every registered
/// permission under that prefix.
/// </summary>
public sealed record PermissionGrant(
    GrantHolderType HolderType,
    string Holder,
    string Permission,
    PermissionGrantType Type);

/// <summary>
/// Identifies the principal an authorization decision is being resolved for:
/// their user id (for direct grants) and the role names carried on their
/// authenticated identity. Roles come from the identity/claims; fine-grained
/// grants are resolved server-side against these (blueprint §22).
/// </summary>
public sealed record PrincipalGrantQuery(
    Guid? UserId,
    IReadOnlyCollection<string> Roles)
{
    /// <summary>An unauthenticated / empty principal — resolves to no permissions.</summary>
    public static readonly PrincipalGrantQuery Anonymous =
        new(null, []);
}
