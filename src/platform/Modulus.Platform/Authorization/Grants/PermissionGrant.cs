using Modulus.Authorization.Scopes;

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

    /// <summary>
    /// Narrows what the permission covers without removing it: the holder keeps the permission but only over the grant's
    /// <see cref="PermissionGrant.Scope"/> (a role gives Tenant, this restricts the user to their Department). It never makes
    /// a permission effective by itself.
    /// </summary>
    Restrict = 2,
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
/// <param name="HolderType">Whether the holder is a role or a user.</param>
/// <param name="Holder">The role name, or the user id.</param>
/// <param name="Permission">The permission (or a <c>module:group:*</c> wildcard).</param>
/// <param name="Type">Allow, Deny or Restrict.</param>
/// <param name="Scope">The data the grant covers; null means the whole company (every grant made before scopes existed).</param>
/// <param name="ValidFrom">The grant applies from this instant on; null means from the start.</param>
/// <param name="ValidUntil">The grant stops applying at this instant, with no administrator action; null means it never expires.</param>
/// <param name="Reason">Why a temporary or scoped grant was made (kept for review and audit).</param>
public sealed record PermissionGrant(
    GrantHolderType HolderType,
    string Holder,
    string Permission,
    PermissionGrantType Type,
    PermissionScope? Scope = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null,
    string? Reason = null)
{
    /// <summary>Whether the grant applies at <paramref name="now"/>; evaluated at decision time, never by a cleanup job (BR-005).</summary>
    public bool IsValidAt(DateTimeOffset now)
        => (ValidFrom is null || now >= ValidFrom) && (ValidUntil is null || now < ValidUntil);

    /// <summary>The scope that applies: the grant's own, or the whole company.</summary>
    public PermissionScope EffectiveScope => Scope ?? PermissionScope.Tenant;
}

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
