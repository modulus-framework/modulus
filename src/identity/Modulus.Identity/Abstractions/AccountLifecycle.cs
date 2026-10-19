namespace Modulus.Identity.Abstractions;

using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Where an account is in its life (ADR-02). Lifecycle is one of three independent dimensions: lockout (a temporary authentication
/// protection) and administrative suspension (a restriction with a reason) are kept apart, so a locked or suspended account keeps its
/// history and grants.
/// </summary>
public enum AccountLifecycle
{
    /// <summary>Created by an administrator or an invitation; the person has not yet been sent a way to set a credential.</summary>
    Invited = 1,

    /// <summary>A credential-setting link has been sent and is waiting to be used.</summary>
    PendingActivation = 2,

    /// <summary>May sign in (subject to lockout and suspension).</summary>
    Active = 0,

    /// <summary>Not allowed to start new access. Sessions and credentials are ended when the account enters this state.</summary>
    Disabled = 3,

    /// <summary>Kept for the record (the person left). Reactivation is a reviewed rehire, never a direct transition.</summary>
    Archived = 4,
}

/// <summary>
/// The legal lifecycle transitions. Anything not listed is refused, so an unexpected state cannot be reached by a shortcut.
/// </summary>
public static class AccountLifecycleRules
{
    /// <summary>Whether an account may move from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The requested state.</param>
    /// <param name="rehireReviewed">True when a reviewed rehire process approved bringing an archived account back.</param>
    public static bool CanTransition(AccountLifecycle from, AccountLifecycle to, bool rehireReviewed = false)
        => (from, to) switch
        {
            (AccountLifecycle.Invited, AccountLifecycle.PendingActivation) => true,
            (AccountLifecycle.Invited, AccountLifecycle.Archived) => true,
            (AccountLifecycle.PendingActivation, AccountLifecycle.Active) => true,
            (AccountLifecycle.PendingActivation, AccountLifecycle.Archived) => true,
            (AccountLifecycle.Active, AccountLifecycle.Disabled) => true,
            (AccountLifecycle.Disabled, AccountLifecycle.Active) => true,
            (AccountLifecycle.Disabled, AccountLifecycle.Archived) => true,
            (AccountLifecycle.Archived, AccountLifecycle.PendingActivation) => rehireReviewed,
            _ => false,
        };

    /// <summary>
    /// Moves the lifecycle, or throws <see cref="ConflictException"/> (409) when the transition is not legal. The caller records the
    /// transition (actor, reason, time) and ends sessions when the account is disabled.
    /// </summary>
    public static AccountLifecycle Transition(AccountLifecycle from, AccountLifecycle to, bool rehireReviewed = false)
        => CanTransition(from, to, rehireReviewed)
            ? to
            : throw new ConflictException($"An account cannot move from {from} to {to}{(from == AccountLifecycle.Archived && to == AccountLifecycle.PendingActivation ? " without a reviewed rehire" : string.Empty)}.");

    /// <summary>
    /// Whether the account may start new access in this state. Only an active account may. Sign-in and SCIM read this through
    /// <c>MayStartNewAccess</c>, which also requires the legacy <c>IsActive</c> flag to allow it.
    /// </summary>
    public static bool AllowsNewAccess(AccountLifecycle state) => state == AccountLifecycle.Active;
}

/// <summary>
/// Decides the lifecycle an account is saved with, from the stored state, the state in memory and the legacy <c>IsActive</c> flag.
/// A lifecycle the caller changed is authoritative and must be a legal transition. An unchanged lifecycle lets <c>IsActive</c> move it
/// only between <see cref="AccountLifecycle.Active"/> and <see cref="AccountLifecycle.Disabled"/>, so existing callers keep working
/// until <c>IsActive</c> is retired. Every other account state is read from the lifecycle alone, so a flag cannot reactivate it. Reads never use the flag.
/// </summary>
internal static class LifecycleReconciliation
{
    /// <summary>The state to save, or null when the change is not a legal transition.</summary>
    /// <param name="stored">The state as stored; null for an account that is being created or has no store.</param>
    /// <param name="lifecycle">The state in memory.</param>
    /// <param name="isActive">The legacy flag in memory.</param>
    /// <param name="rehireReviewed">True when a reviewed rehire approved the move from archived.</param>
    public static AccountLifecycle? Resolve(AccountLifecycle? stored, AccountLifecycle lifecycle, bool isActive, bool rehireReviewed = false)
    {
        var before = stored ?? lifecycle;
        var changedByCaller = stored is { } s && lifecycle != s;
        var target = changedByCaller || isActive == AccountLifecycleRules.AllowsNewAccess(before)
            ? lifecycle
            : FromFlag(before, isActive);

        if (stored is { } from && from != target && !AccountLifecycleRules.CanTransition(from, target, rehireReviewed))
            return null;
        return target;
    }

    private static AccountLifecycle FromFlag(AccountLifecycle before, bool isActive) => (before, isActive) switch
    {
        (AccountLifecycle.Active, false) => AccountLifecycle.Disabled,
        (AccountLifecycle.Disabled, true) => AccountLifecycle.Active,
        _ => before,
    };
}
