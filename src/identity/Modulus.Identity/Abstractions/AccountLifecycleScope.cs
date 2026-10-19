namespace Modulus.Identity.Abstractions;

/// <summary>What the explicit lifecycle service is applying, so the user manager records the transition with the caller's actor and reason.</summary>
internal sealed record AccountLifecycleScopeState(Guid? ActorId, string? Reason, bool RehireReviewed);

/// <summary>Carries an explicit lifecycle change into the user manager for the save it causes. Restored when the scope is disposed.</summary>
internal static class AccountLifecycleScope
{
    private static readonly AsyncLocal<AccountLifecycleScopeState?> Slot = new();

    /// <summary>The change being applied on this flow, or null when the change came from a plain property update.</summary>
    public static AccountLifecycleScopeState? Current => Slot.Value;

    public static IDisposable Enter(Guid? actorId, string? reason, bool rehireReviewed)
    {
        var previous = Slot.Value;
        Slot.Value = new AccountLifecycleScopeState(actorId, reason, rehireReviewed);
        return new Restore(previous);
    }

    private sealed class Restore(AccountLifecycleScopeState? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
