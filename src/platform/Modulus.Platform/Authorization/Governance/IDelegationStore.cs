namespace Modulus.Authorization.Governance;

/// <summary>
/// Stores <see cref="Delegation"/>s as editable, revocable data and returns those in
/// force for a delegate. The source of truth for temporary/delegated authority
/// (blueprint §5.13); the effective delegated permission set is computed by
/// <see cref="IDelegationResolver"/>. Implementations must be safe for concurrent reads
/// while delegations are created or revoked.
/// </summary>
public interface IDelegationStore
{
    /// <summary>
    /// The delegations targeting <paramref name="toUserId"/> that are in force at
    /// <paramref name="now"/> (window contains <paramref name="now"/> and not revoked).
    /// Empty when the delegate has no active delegation — fail-closed.
    /// </summary>
    IReadOnlyCollection<Delegation> ActiveFor(Guid toUserId, DateTimeOffset now);

    /// <summary>Every delegation, active or not — for governance review and audit.</summary>
    IReadOnlyCollection<Delegation> All();
}
