namespace Modulus.Identity.Abstractions;

/// <summary>
/// One explicit lifecycle change: who made it, to which state, and why. The actor is passed in, not read from the ambient user, so
/// a background job or an administrator acting for someone else records the right actor.
/// </summary>
/// <param name="UserId">The account to change.</param>
/// <param name="To">The state to move to. The move must be legal (<see cref="AccountLifecycleRules"/>).</param>
/// <param name="ActorId">The person or process making the change, or null for the system.</param>
/// <param name="Reason">
/// The suspension reason code (<see cref="SuspensionReasons"/>) when <paramref name="To"/> is <see cref="AccountLifecycle.Disabled"/>,
/// required then. Otherwise an optional free-text reason kept in the transition history.
/// </param>
/// <param name="Note">The note that goes with the suspension; required for the reasons <see cref="SuspensionReasons.RequiresNote"/> names.</param>
/// <param name="RehireReviewed">True when a reviewed rehire approved bringing an archived account back.</param>
public sealed record AccountLifecycleChange(
    Guid UserId, AccountLifecycle To, Guid? ActorId, string? Reason = null, string? Note = null, bool RehireReviewed = false);

/// <summary>
/// Changes an account's lifecycle explicitly (ADR-02). Every change is checked against the transition matrix, recorded in the history
/// with its actor and reason, audited, and ends new access when the account is disabled.
/// </summary>
public interface IAccountLifecycleService
{
    /// <summary>Applies the change and returns the new state. A state the account is already in is a no-op.</summary>
    /// <exception cref="Modulus.Core.Abstractions.Exceptions.NotFoundException">No account has that identifier.</exception>
    /// <exception cref="Modulus.Core.Abstractions.Exceptions.ConflictException">The move is not legal, or the account changed meanwhile.</exception>
    /// <exception cref="ArgumentException">A disable has no valid suspension reason, or the reason needs a note and has none.</exception>
    Task<AccountLifecycle> ChangeAsync(AccountLifecycleChange change, CancellationToken ct = default);
}
