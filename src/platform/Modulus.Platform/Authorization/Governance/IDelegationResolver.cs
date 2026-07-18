namespace Modulus.Authorization.Governance;

/// <summary>
/// One permission a delegate effectively holds through an active delegation, carrying the
/// <b>on-behalf-of</b> provenance the audit trail must preserve end-to-end (blueprint
/// §5.13).
/// </summary>
/// <param name="Permission">The delegated permission the delegate may exercise.</param>
/// <param name="OnBehalfOf">The delegator whose authority is being exercised.</param>
/// <param name="DelegationId">The delegation that confers it, for audit and revocation.</param>
public sealed record DelegatedPermission(string Permission, Guid OnBehalfOf, Guid DelegationId);

/// <summary>
/// Computes the permissions a delegate effectively holds <b>right now</b> through active
/// delegations — the decision-time enforcement of temporary/delegated access (blueprint
/// §5.13, §15). Two invariants make it safe:
/// <list type="bullet">
///   <item><b>Expiry at decision time:</b> only delegations whose window contains the
///     current instant and that are not revoked contribute — never a stale grant.</item>
///   <item><b>Capped by the delegator's own <i>direct</i> authority:</b> each delegated
///     permission survives only if the delegator currently holds it directly. This both
///     enforces "you cannot delegate what you do not have" and <b>bounds
///     sub-delegation</b> — a delegate cannot re-delegate authority that was itself
///     delegated to them, because the cap ignores the delegator's delegated permissions.</item>
/// </list>
/// </summary>
public interface IDelegationResolver
{
    /// <summary>
    /// The permissions delegated to <paramref name="delegateUserId"/> that are in force
    /// now and within the delegator's direct authority, each tagged with its
    /// on-behalf-of provenance. Empty when the user has no active, valid delegation.
    /// </summary>
    IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId);
}
