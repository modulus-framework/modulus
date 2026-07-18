namespace Modulus.Authorization.Governance;

/// <summary>
/// A first-class, <b>time-bounded, revocable</b> transfer of a subset of one user's
/// authority to another (blueprint §5.13, §15) — the alternative to the endemic
/// anti-patterns of sharing credentials or granting a permanent role "just for this
/// week". User <see cref="FromUserId"/> authorizes <see cref="ToUserId"/> to act with the
/// named <see cref="Permissions"/> for the window [<see cref="NotBefore"/>,
/// <see cref="NotAfter"/>). The delegator's roles are snapshotted in
/// <see cref="FromRoles"/> so the delegation can be <b>capped by the delegator's own
/// authority</b> at decision time — you cannot delegate what you do not hold.
/// <para>
/// Validity is enforced <b>at decision time</b> (never by a cleanup job): a delegation
/// confers nothing outside its window or once <see cref="Revoked"/>. Revocation is
/// immediate. The audit trail records the delegate acting <i>on behalf of</i> the
/// delegator.
/// </para>
/// </summary>
/// <param name="Id">Stable identifier, for revocation and audit.</param>
/// <param name="FromUserId">The delegator whose authority is lent.</param>
/// <param name="FromRoles">The delegator's roles, snapshotted so their effective authority (the cap) can be recomputed.</param>
/// <param name="ToUserId">The delegate who may act with the delegated authority.</param>
/// <param name="Permissions">The permissions delegated — a subset of the delegator's own, intersected with it at decision time.</param>
/// <param name="NotBefore">Inclusive start of the validity window.</param>
/// <param name="NotAfter">Exclusive end of the validity window.</param>
/// <param name="Revoked">Whether the delegation has been revoked (immediately inert).</param>
public sealed record Delegation(
    Guid Id,
    Guid FromUserId,
    IReadOnlyCollection<string> FromRoles,
    Guid ToUserId,
    IReadOnlySet<string> Permissions,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool Revoked = false)
{
    /// <summary>
    /// True when the delegation is in force at <paramref name="now"/>: not revoked and
    /// within [<see cref="NotBefore"/>, <see cref="NotAfter"/>). Fail-closed — any
    /// ambiguity about validity resolves to not-active.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now)
        => !Revoked && now >= NotBefore && now < NotAfter;
}
